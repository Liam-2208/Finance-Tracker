using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PocketLedger.Pages;

public class InvestmentsModel(IHttpClientFactory clients, IConfiguration configuration, IMemoryCache cache) : PageModel
{
    private static readonly (string Symbol, string Name)[] Watchlist = [("NVDA", "NVIDIA"), ("AAPL", "Apple"), ("MSFT", "Microsoft"), ("AMZN", "Amazon"), ("GOOGL", "Alphabet")];
    public List<MarketCompany> Companies { get; private set; } = [];
    public List<MarketStory> Stories { get; private set; } = [];
    public bool FeedConfigured { get; private set; }

    public async Task OnGetAsync()
    {
        var token = configuration["MarketData:FinnhubApiKey"];
        FeedConfigured = !string.IsNullOrWhiteSpace(token);
        if (!FeedConfigured) return;
        if (cache.TryGetValue<MarketSnapshot>("finance-freedom-market-snapshot", out var snapshot) && snapshot is not null)
        {
            Companies = snapshot.Companies;
            Stories = snapshot.Stories;
            return;
        }
        var client = clients.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(8);
        client.BaseAddress = new Uri("https://finnhub.io/api/v1/");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Finnhub-Token", token);
        foreach (var item in Watchlist)
        {
            try
            {
                var quote = await client.GetFromJsonAsync<Quote>($"quote?symbol={item.Symbol}");
                if (quote is not null && quote.Current > 0)
                    Companies.Add(new(item.Symbol, item.Name, quote.Current, quote.ChangePercent, DateTimeOffset.FromUnixTimeSeconds(quote.Timestamp)));
            }
            catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException) { }
        }
        try
        {
            var news = await client.GetFromJsonAsync<List<News>>("news?category=general");
            Stories = (news ?? []).Where(x => Uri.TryCreate(x.Url, UriKind.Absolute, out var uri) && uri.Scheme == "https")
                .Take(8).Select(x => new MarketStory(x.Headline, x.Source, x.Url, DateTimeOffset.FromUnixTimeSeconds(x.Timestamp))).ToList();
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException) { }
        snapshot = new MarketSnapshot(Companies, Stories);
        cache.Set("finance-freedom-market-snapshot", snapshot, TimeSpan.FromMinutes(1));
    }
    public record MarketCompany(string Symbol, string Name, decimal Price, decimal ChangePercent, DateTimeOffset UpdatedAt);
    public record MarketStory(string Headline, string Source, string Url, DateTimeOffset PublishedAt);
    private sealed record MarketSnapshot(List<MarketCompany> Companies, List<MarketStory> Stories);
    private sealed class Quote { [JsonPropertyName("c")] public decimal Current { get; set; } [JsonPropertyName("dp")] public decimal ChangePercent { get; set; } [JsonPropertyName("t")] public long Timestamp { get; set; } }
    private sealed class News { [JsonPropertyName("headline")] public string Headline { get; set; } = ""; [JsonPropertyName("source")] public string Source { get; set; } = ""; [JsonPropertyName("url")] public string Url { get; set; } = ""; [JsonPropertyName("datetime")] public long Timestamp { get; set; } }
}
