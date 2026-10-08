using Microsoft.AspNetCore.Identity;

namespace PocketLedger.Models;

public class ApplicationUser : IdentityUser
{
    public string Plan { get; set; } = "Free";

    public string? StripeCustomerId { get; set; }

    public string? StripeSubscriptionId { get; set; }

    public string? SubscriptionStatus { get; set; }

    public bool IsPlus => Plan == "Plus" && SubscriptionStatus is "active" or "trialing";

    public bool HasOngoingSubscription => SubscriptionStatus is "active" or "trialing" or "past_due" or "unpaid";
}
