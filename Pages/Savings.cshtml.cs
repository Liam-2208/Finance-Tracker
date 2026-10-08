using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models;

namespace PocketLedger.Pages;

public class SavingsModel(FinanceDbContext database, UserManager<ApplicationUser> users) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Month { get; set; } = DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    [BindProperty(SupportsGet = true)] public decimal SavingsRate { get; set; } = 20;
    [BindProperty(SupportsGet = true)] public decimal TargetAmount { get; set; } = 1000;
    public decimal Income { get; private set; }
    public decimal Spending { get; private set; }
    public decimal Remaining { get; private set; }
    public decimal GoalAmount => Math.Round(Math.Max(0, Income) * Math.Clamp(SavingsRate, 0, 100) / 100, 2);
    public decimal SuggestedMonthlySavings => Math.Min(GoalAmount, Math.Max(0, Remaining));
    public decimal AfterGoal => Remaining - SuggestedMonthlySavings;
    public decimal YearlyProjection => SuggestedMonthlySavings * 12;
    public decimal WeeklyProjection => Math.Round(SuggestedMonthlySavings * 12 / 52, 2);
    public int? MonthsToTarget => TargetAmount <= 0 || SuggestedMonthlySavings <= 0
        ? null
        : (int)Math.Ceiling(TargetAmount / SuggestedMonthlySavings);
    public string MonthLabel { get; private set; } = string.Empty;
    public List<CategorySpend> TopCategories { get; private set; } = [];
    public decimal LargestCategorySpend { get; private set; }

    public async Task OnGetAsync()
    {
        if (!DateTime.TryParseExact(Month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            parsed = DateTime.Today;
        var start = new DateTime(parsed.Year, parsed.Month, 1);
        Month = start.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        MonthLabel = start.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-GB"));
        SavingsRate = Math.Clamp(SavingsRate, 0, 100);
        TargetAmount = Math.Clamp(TargetAmount, 0, 10_000_000);
        var end = start.AddMonths(1);
        var owner = users.GetUserId(User);
        var rows = await database.Transactions.AsNoTracking()
            .Where(t => t.UserId == owner && t.Date >= start && t.Date < end).ToListAsync();
        Income = rows.Where(t => t.Type == "Income").Sum(t => t.Amount);
        Spending = rows.Where(t => t.Type == "Expense").Sum(t => t.Amount);
        Remaining = Income - Spending;
        TopCategories = rows.Where(t => t.Type == "Expense")
            .GroupBy(t => t.Category.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new CategorySpend(group.Key, group.Sum(t => t.Amount)))
            .OrderByDescending(category => category.Amount)
            .Take(5)
            .ToList();
        LargestCategorySpend = TopCategories.FirstOrDefault()?.Amount ?? 0;
    }

    public Task OnPostAsync() => OnGetAsync();

    public record CategorySpend(string Name, decimal Amount);
}
