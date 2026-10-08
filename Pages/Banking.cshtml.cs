using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models;

namespace PocketLedger.Pages;

public class BankingModel(
    FinanceDbContext database,
    UserManager<ApplicationUser> users,
    IHttpClientFactory clients,
    IConfiguration configuration) : PageModel
{
    public List<BankConnection> Connections { get; private set; } = [];
    public string? Error { get; private set; }
    public string? Message { get; private set; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Banking:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["Banking:ClientSecret"])
        && !string.IsNullOrWhiteSpace(configuration["Banking:RedirectUri"]);

    public async Task<IActionResult> OnGetAsync(string? result)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        var owner = users.GetUserId(User)!;
        Connections = await database.BankConnections.AsNoTracking().Where(x => x.UserId == owner).ToListAsync();
        if (result == "connected") Message = "Your bank provider returned successfully. Review the linked cards below.";
        if (result == "failed") Error = "The bank connection could not be completed. Check the provider setup and try again.";
        return Page();
    }

    public async Task<IActionResult> OnPostConnectAsync()
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        if (!IsConfigured) return RedirectToPage(new { result = "failed" });
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var owner = users.GetUserId(User)!;
        database.BankLinkStates.Add(new BankLinkState
        {
            UserId = owner,
            StateHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state))),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        });
        await database.SaveChangesAsync();
        var clientId = configuration["Banking:ClientId"]!;
        var redirect = configuration["Banking:RedirectUri"]!;
        var authHost = IsSandbox ? "https://auth.truelayer-sandbox.com" : "https://auth.truelayer.com";
        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code", ["client_id"] = clientId, ["redirect_uri"] = redirect,
            ["scope"] = configuration["Banking:Scopes"] ?? "info accounts balance cards offline_access",
            ["state"] = state
        };
        return Redirect(QueryHelpers.AddQueryString($"{authHost}/", query));
    }

    public async Task<IActionResult> OnGetCallbackAsync(string? code, string? state, string? error)
    {
        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return RedirectToPage("/Banking", new { result = "failed" });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
        var linkState = await database.BankLinkStates.FirstOrDefaultAsync(x => x.StateHash == hash && x.ConsumedAt == null);
        if (linkState is null || linkState.ExpiresAt < DateTimeOffset.UtcNow)
            return RedirectToPage("/Banking", new { result = "failed" });
        linkState.ConsumedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync();

        try
        {
            var token = await ExchangeCodeAsync(code);
            if (string.IsNullOrWhiteSpace(token.AccessToken)) throw new InvalidOperationException("Missing access token.");
            var cards = await FetchCardsAsync(token.AccessToken);
            if (cards.Count == 0)
                cards.Add(new CardSummary(null, "Bank connection", null, null, null));
            foreach (var card in cards)
            {
                database.BankConnections.Add(new BankConnection
                {
                    UserId = linkState.UserId,
                    ProviderCardId = card.Id,
                    DisplayName = Clean(card.Name, 80) ?? "Linked card",
                    Network = Clean(card.Network, 40),
                    LastFour = ExtractLastFour(card.LastFour),
                    CardType = Clean(card.Type, 40),
                    ConnectedAt = DateTimeOffset.UtcNow
                });
            }
            await database.SaveChangesAsync();
            return RedirectToPage("/Banking", new { result = "connected" });
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
        {
            return RedirectToPage("/Banking", new { result = "failed" });
        }
    }

    public async Task<IActionResult> OnPostDisconnectAsync(int id)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        var owner = users.GetUserId(User)!;
        var connection = await database.BankConnections.FirstOrDefaultAsync(x => x.Id == id && x.UserId == owner);
        if (connection is not null)
        {
            database.BankConnections.Remove(connection);
            await database.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    private bool IsSandbox => string.Equals(configuration["Banking:Environment"], "sandbox", StringComparison.OrdinalIgnoreCase);
    private string AuthHost => IsSandbox ? "https://auth.truelayer-sandbox.com" : "https://auth.truelayer.com";

    private async Task<TokenResponse> ExchangeCodeAsync(string code)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = configuration["Banking:ClientId"]!,
            ["client_secret"] = configuration["Banking:ClientSecret"]!, ["code"] = code,
            ["redirect_uri"] = configuration["Banking:RedirectUri"]!
        };
        using var response = await clients.CreateClient().PostAsync($"{AuthHost}/connect/token", new FormUrlEncodedContent(form));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TokenResponse>() ?? throw new InvalidOperationException("Empty token response.");
    }

    private async Task<List<CardSummary>> FetchCardsAsync(string accessToken)
    {
        var apiHost = IsSandbox ? "https://api.truelayer-sandbox.com" : "https://api.truelayer.com";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{apiHost}/data/v1/cards");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await clients.CreateClient().SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var output = new List<CardSummary>();
        ReadResults(json.RootElement, output, isCard: true);
        using var accountRequest = new HttpRequestMessage(HttpMethod.Get, $"{apiHost}/data/v1/accounts");
        accountRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var accountResponse = await clients.CreateClient().SendAsync(accountRequest);
        accountResponse.EnsureSuccessStatusCode();
        using var accountsJson = await JsonDocument.ParseAsync(await accountResponse.Content.ReadAsStreamAsync());
        ReadResults(accountsJson.RootElement, output, isCard: false);
        return output;
    }

    private static void ReadResults(JsonElement root, List<CardSummary> output, bool isCard)
    {
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return;
        foreach (var item in results.EnumerateArray())
            output.Add(new(Read(item, "account_id", "card_id", "id"), Read(item, "display_name", "name"),
                Read(item, "card_network", "network"), isCard ? Read(item, "partial_card_number", "last_four") : null,
                isCard ? Read(item, "card_type", "type") : Read(item, "account_type", "type")));
    }

    private static string? Read(JsonElement item, params string[] keys)
    {
        foreach (var key in keys)
            if (item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String) return value.GetString();
        return null;
    }
    private static string? ExtractLastFour(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : null;
    }
    private static string? Clean(string? value, int limit) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, limit)];
    private sealed record CardSummary(string? Id, string? Name, string? Network, string? LastFour, string? Type);
    private sealed class TokenResponse { [JsonPropertyName("access_token")] public string? AccessToken { get; set; } }
}
