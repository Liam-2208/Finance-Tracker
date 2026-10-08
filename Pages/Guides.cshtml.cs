using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PocketLedger.Models;

namespace PocketLedger.Pages;

public class GuidesModel(UserManager<ApplicationUser> users) : PageModel
{
    public bool ShowAds { get; private set; }

    public async Task OnGetAsync()
    {
        ShowAds = (await users.GetUserAsync(User))?.IsPlus != true;
    }
}
