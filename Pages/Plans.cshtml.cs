using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PocketLedger.Models;

namespace PocketLedger.Pages;

public class PlansModel(UserManager<ApplicationUser> userManager) : PageModel
{
    public ApplicationUser? Account { get; private set; }

    public string? PaymentStatus { get; private set; }

    public string? BillingStatus { get; private set; }

    public async Task OnGetAsync(string? payment, string? billing)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            Account = await userManager.GetUserAsync(User);
        }

        PaymentStatus = payment;
        BillingStatus = billing;
    }
}
