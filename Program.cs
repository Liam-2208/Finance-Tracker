using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Npgsql;
using PocketLedger.Data;
using PocketLedger.Models;

var builder = WebApplication.CreateBuilder(args);

var usePostgres = string.Equals(builder.Configuration["Database:Provider"], "Postgres", StringComparison.OrdinalIgnoreCase);
if (!builder.Environment.IsDevelopment() && !usePostgres)
{
    throw new InvalidOperationException(
        "Production must use the managed PostgreSQL database. Configure Database__Provider=Postgres and Database__Postgres__Url to avoid storing account data on ephemeral local disk.");
}
var postgresConnection = usePostgres ? CreatePostgresConnectionString(builder.Configuration) : null;

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Public reverse-proxy deployments must restrict this to the hosting
    // provider's proxy ranges when the service is also reachable directly.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var britishCulture = CultureInfo.GetCultureInfo("en-GB");
    options.DefaultRequestCulture = new RequestCulture(britishCulture);
    options.SupportedCultures = [britishCulture];
    options.SupportedUICultures = [britishCulture];
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizePage("/Index");
    options.Conventions.AuthorizePage("/Account/Manage");
    options.Conventions.AuthorizePage("/Savings");
    options.Conventions.AuthorizePage("/Account/Logout");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/Register");
    options.Conventions.AllowAnonymousToPage("/Privacy");
    options.Conventions.AllowAnonymousToPage("/Plans");
});
builder.Services.AddDbContext<FinanceDbContext>(options =>
{
    if (usePostgres) options.UseNpgsql(postgresConnection);
    else options.UseSqlite(builder.Configuration.GetConnectionString("FinanceDatabase"));
});
builder.Services.AddDbContext<AccountsDbContext>(options =>
{
    if (usePostgres) options.UseNpgsql(postgresConnection);
    else options.UseSqlite(builder.Configuration.GetConnectionString("AccountsDatabase"));
});
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<AccountsDbContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.SlidingExpiration = true;
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/Login";
});
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddDataProtection().PersistKeysToDbContext<FinanceDbContext>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 240,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseForwardedHeaders();
app.UseRequestLocalization();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        if (context.User.Identity?.IsAuthenticated == true)
            context.Response.Headers.CacheControl = "no-store, private";
        return Task.CompletedTask;
    });
    await next();
});
app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

StripeBillingEndpoints.Map(app);

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/ads.txt", (IConfiguration configuration) =>
{
    var publisherId = configuration["Advertising:PublisherId"];
    if (publisherId is null || !publisherId.StartsWith("ca-pub-", StringComparison.Ordinal)
        || publisherId.Length != 23 || !publisherId[7..].All(char.IsAsciiDigit))
    {
        return Results.NotFound();
    }

    var adsTxt = $"google.com, pub-{publisherId[7..]}, DIRECT, f08c47fec0942fa0\n";
    return Results.Text(adsTxt, "text/plain; charset=utf-8");
}).AllowAnonymous();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

static string CreatePostgresConnectionString(IConfiguration configuration)
{
    var connectionUrl = configuration["Database:Postgres:Url"];
    var builder = new NpgsqlConnectionStringBuilder
    {
        SslMode = SslMode.Require,
        Timeout = 15,
        CommandTimeout = 30
    };
    if (Uri.TryCreate(connectionUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme is "postgres" or "postgresql")
    {
        var credentials = uri.UserInfo.Split(':', 2);
        builder.Host = uri.Host;
        builder.Port = uri.Port;
        builder.Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        builder.Username = Uri.UnescapeDataString(credentials[0]);
        builder.Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : string.Empty;
    }
    else
    {
        builder.Host = configuration["Database:Postgres:Host"] ?? throw new InvalidOperationException("PostgreSQL host is not configured.");
        builder.Port = configuration.GetValue("Database:Postgres:Port", 5432);
        builder.Database = configuration["Database:Postgres:Name"] ?? throw new InvalidOperationException("PostgreSQL database name is not configured.");
        builder.Username = configuration["Database:Postgres:User"] ?? throw new InvalidOperationException("PostgreSQL user is not configured.");
        builder.Password = configuration["Database:Postgres:Password"] ?? throw new InvalidOperationException("PostgreSQL password is not configured.");
    }
    return builder.ConnectionString;
}
