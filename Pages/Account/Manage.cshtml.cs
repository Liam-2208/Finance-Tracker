using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models;

namespace PocketLedger.Pages.Account;

public class ManageModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    FinanceDbContext financeDatabase) : PageModel
{
    [BindProperty]
    public DeleteAccountInput Input { get; set; } = new();

    public ApplicationUser Account { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync()
    {
        Account = (await userManager.GetUserAsync(User))!;
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        Account = (await userManager.GetUserAsync(User))!;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Input.Confirmation != "DELETE")
        {
            ModelState.AddModelError("Input.Confirmation", "Type DELETE to confirm account removal.");
            return Page();
        }

        if (!await userManager.CheckPasswordAsync(Account, Input.Password))
        {
            ModelState.AddModelError("Input.Password", "That password is incorrect.");
            return Page();
        }

        if (Account.HasOngoingSubscription)
        {
            ModelState.AddModelError(string.Empty, "Cancel your Plus subscription first, then return here after it ends.");
            return Page();
        }

        var deletion = await userManager.DeleteAsync(Account);
        if (!deletion.Succeeded)
        {
            foreach (var error in deletion.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        var transactions = await financeDatabase.Transactions.Where(item => item.UserId == Account.Id).ToListAsync();
        var budgets = await financeDatabase.Budgets.Where(item => item.UserId == Account.Id).ToListAsync();
        var bankConnections = await financeDatabase.BankConnections.Where(item => item.UserId == Account.Id).ToListAsync();
        var bankLinkStates = await financeDatabase.BankLinkStates.Where(item => item.UserId == Account.Id).ToListAsync();
        financeDatabase.Transactions.RemoveRange(transactions);
        financeDatabase.Budgets.RemoveRange(budgets);
        financeDatabase.BankConnections.RemoveRange(bankConnections);
        financeDatabase.BankLinkStates.RemoveRange(bankLinkStates);
        await financeDatabase.SaveChangesAsync();

        await signInManager.SignOutAsync();
        return RedirectToPage("/Home");
    }

    public class DeleteAccountInput
    {
        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required, StringLength(6)]
        public string Confirmation { get; set; } = string.Empty;
    }
}
