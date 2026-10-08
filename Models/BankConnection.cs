namespace PocketLedger.Models;

public class BankConnection
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Provider { get; set; } = "TrueLayer";
    public string? ProviderCardId { get; set; }
    public string DisplayName { get; set; } = "Linked card";
    public string? Network { get; set; }
    public string? LastFour { get; set; }
    public string? CardType { get; set; }
    public DateTimeOffset ConnectedAt { get; set; }
}

public class BankLinkState
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string StateHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}
