using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models;

namespace PocketLedger.Pages;

public class IndexModel(FinanceDbContext database, UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Month { get; set; } = DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    [BindProperty(SupportsGet = true)]
    public string WeekOf { get; set; } = GetMonday(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [BindProperty]
    public TransactionInput Input { get; set; } = new();

    [BindProperty]
    public TransactionInput EditInput { get; set; } = new();

    [BindProperty]
    public BudgetInput BudgetForm { get; set; } = new();

    public DateTime SelectedMonth { get; private set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public DateTime SelectedWeek { get; private set; } = GetMonday(DateTime.Today);

    public bool Editing => EditInput.Id > 0;

    public bool IsPlus { get; private set; }

    public bool BudgetLimitReached { get; private set; }

    private string CurrentUserId => userManager.GetUserId(User) ?? string.Empty;

    public List<Transaction> Transactions { get; private set; } = [];

    public List<CategorySummary> CategorySummaries { get; private set; } = [];

    public decimal TotalIncome { get; private set; }

    public decimal TotalSpending { get; private set; }

    public decimal WeeklyIncome { get; private set; }

    public decimal WeeklySpending { get; private set; }

    public decimal NetTotal => TotalIncome - TotalSpending;

    public decimal WeeklyNet => WeeklyIncome - WeeklySpending;

    public async Task OnGetAsync(int? editId)
    {
        NormalizeMonth();
        NormalizeWeek();
        BudgetForm.YearMonth = Month;
        await LoadMonthAsync();

        if (editId is not null)
        {
            var transaction = await database.Transactions.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == editId.Value && item.UserId == CurrentUserId);

            if (transaction is not null)
            {
                EditInput = new TransactionInput
                {
                    Id = transaction.Id,
                    Date = transaction.Date,
                    Description = transaction.Description,
                    Category = transaction.Category,
                    Type = transaction.Type,
                    Amount = transaction.Amount
                };
            }
        }
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        NormalizeMonth();
        NormalizeWeek();
        ValidateTransaction(Input, "Input");

        if (!ModelState.IsValid)
        {
            await LoadMonthAsync();
            return Page();
        }

        var transaction = ToTransaction(Input);
        transaction.UserId = CurrentUserId;
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        return RedirectToPage(new { month = Month, weekOf = WeekOf });
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        NormalizeMonth();
        NormalizeWeek();
        ValidateTransaction(EditInput, "EditInput");

        if (!ModelState.IsValid)
        {
            await LoadMonthAsync();
            return Page();
        }

        var transaction = await database.Transactions
            .FirstOrDefaultAsync(item => item.Id == EditInput.Id && item.UserId == CurrentUserId);
        if (transaction is null)
        {
            return NotFound();
        }

        transaction.Date = EditInput.Date;
        transaction.Description = EditInput.Description.Trim();
        transaction.Category = EditInput.Category.Trim();
        transaction.Type = EditInput.Type;
        transaction.Amount = EditInput.Amount;
        await database.SaveChangesAsync();
        return RedirectToPage(new { month = Month, weekOf = WeekOf });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        NormalizeMonth();
        NormalizeWeek();
        var transaction = await database.Transactions
            .FirstOrDefaultAsync(item => item.Id == id && item.UserId == CurrentUserId);
        if (transaction is not null)
        {
            database.Transactions.Remove(transaction);
            await database.SaveChangesAsync();
        }

        return RedirectToPage(new { month = Month, weekOf = WeekOf });
    }

    public async Task<IActionResult> OnPostSaveBudgetAsync()
    {
        NormalizeMonth();
        NormalizeWeek();
        var account = await userManager.GetUserAsync(User);
        IsPlus = account?.IsPlus == true;
        BudgetForm.YearMonth = Month;

        if (!TryParseMonth(Month, out _))
        {
            Month = DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            BudgetForm.YearMonth = Month;
        }

        if (string.IsNullOrWhiteSpace(BudgetForm.Category))
        {
            ModelState.AddModelError("BudgetForm.Category", "Enter a category.");
        }

        if (BudgetForm.LimitAmount <= 0)
        {
            ModelState.AddModelError("BudgetForm.LimitAmount", "Enter a budget greater than zero.");
        }

        if (!ModelState.IsValid)
        {
            await LoadMonthAsync();
            return Page();
        }

        var category = BudgetForm.Category.Trim();
        var existingBudgets = await database.Budgets
            .Where(budget => budget.YearMonth == Month && budget.UserId == CurrentUserId)
            .ToListAsync();
        var existing = existingBudgets.FirstOrDefault(budget =>
            string.Equals(budget.Category, category, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            if (!IsPlus && existingBudgets.Count >= 3)
            {
                ModelState.AddModelError(string.Empty, "The Free plan includes up to 3 category budgets per month. Plus unlocks unlimited budgets.");
                await LoadMonthAsync();
                return Page();
            }

            database.Budgets.Add(new Budget
            {
                YearMonth = Month,
                Category = category,
                LimitAmount = BudgetForm.LimitAmount,
                UserId = CurrentUserId
            });
        }
        else
        {
            existing.LimitAmount = BudgetForm.LimitAmount;
        }

        await database.SaveChangesAsync();
        return RedirectToPage(new { month = Month, weekOf = WeekOf });
    }

    public async Task<IActionResult> OnPostDeleteBudgetAsync(int id)
    {
        NormalizeMonth();
        NormalizeWeek();
        var budget = await database.Budgets
            .FirstOrDefaultAsync(item => item.Id == id && item.UserId == CurrentUserId);
        if (budget is not null)
        {
            database.Budgets.Remove(budget);
            await database.SaveChangesAsync();
        }

        return RedirectToPage(new { month = Month, weekOf = WeekOf });
    }

    public async Task<IActionResult> OnGetExportCsvAsync()
    {
        NormalizeMonth();
        NormalizeWeek();
        var account = await userManager.GetUserAsync(User);
        if (account?.IsPlus != true)
        {
            return RedirectToPage("/Plans");
        }
        var transactions = await GetMonthTransactionsAsync();
        var csv = new StringBuilder();
        csv.AppendLine("Date,Description,Category,Type,Amount");

        foreach (var transaction in transactions)
        {
            csv.Append(Csv(transaction.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append(',')
                .Append(Csv(transaction.Description)).Append(',')
                .Append(Csv(transaction.Category)).Append(',')
                .Append(Csv(transaction.Type)).Append(',')
                .AppendLine(transaction.Amount.ToString("0.00", CultureInfo.InvariantCulture));
        }

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"transactions-{Month}.csv");
    }

    private async Task LoadMonthAsync()
    {
        NormalizeMonth();
        NormalizeWeek();
        var account = await userManager.GetUserAsync(User);
        IsPlus = account?.IsPlus == true;
        Transactions = await GetMonthTransactionsAsync();
        TotalIncome = Transactions.Where(item => item.Type == "Income").Sum(item => item.Amount);
        TotalSpending = Transactions.Where(item => item.Type == "Expense").Sum(item => item.Amount);

        var budgets = await database.Budgets.AsNoTracking()
            .Where(budget => budget.YearMonth == Month && budget.UserId == CurrentUserId)
            .OrderBy(budget => budget.Category)
            .ToListAsync();
        BudgetLimitReached = !IsPlus && budgets.Count >= 3;
        var spendingByCategory = Transactions
            .Where(item => item.Type == "Expense")
            .GroupBy(item => item.Category.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount), StringComparer.OrdinalIgnoreCase);
        var categories = new HashSet<string>(spendingByCategory.Keys, StringComparer.OrdinalIgnoreCase);
        categories.UnionWith(budgets.Select(budget => budget.Category));

        CategorySummaries = categories
            .OrderBy(category => category)
            .Select(category =>
            {
                var budget = budgets.FirstOrDefault(item =>
                    string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));
                var spent = spendingByCategory.GetValueOrDefault(category);
                var percent = budget is null || budget.LimitAmount <= 0
                    ? 0
                    : Math.Min((double)(spent / budget.LimitAmount * 100), 100);
                return new CategorySummary(category, spent, budget?.LimitAmount, budget?.Id, percent);
            })
            .ToList();

        var weekEnd = SelectedWeek.AddDays(7);
        var weeklyTransactions = await database.Transactions.AsNoTracking()
            .Where(item => item.UserId == CurrentUserId && item.Date >= SelectedWeek && item.Date < weekEnd)
            .ToListAsync();
        WeeklyIncome = weeklyTransactions.Where(item => item.Type == "Income").Sum(item => item.Amount);
        WeeklySpending = weeklyTransactions.Where(item => item.Type == "Expense").Sum(item => item.Amount);
    }

    private Task<List<Transaction>> GetMonthTransactionsAsync()
    {
        var start = SelectedMonth;
        var end = start.AddMonths(1);
        return database.Transactions.AsNoTracking()
            .Where(item => item.UserId == CurrentUserId && item.Date >= start && item.Date < end)
            .OrderByDescending(item => item.Date)
            .ThenByDescending(item => item.Id)
            .ToListAsync();
    }

    private void NormalizeMonth()
    {
        if (!TryParseMonth(Month, out var selected))
        {
            Month = DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            selected = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        }

        SelectedMonth = selected;
    }

    private void NormalizeWeek()
    {
        if (!DateTime.TryParseExact(WeekOf, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var selected))
        {
            selected = DateTime.Today;
        }

        SelectedWeek = GetMonday(selected);
        WeekOf = SelectedWeek.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static DateTime GetMonday(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static bool TryParseMonth(string? value, out DateTime selected)
    {
        if (DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            selected = new DateTime(parsed.Year, parsed.Month, 1);
            return true;
        }

        selected = default;
        return false;
    }

    private void ValidateTransaction(TransactionInput input, string keyPrefix)
    {
        if (input.Amount <= 0)
        {
            ModelState.AddModelError($"{keyPrefix}.Amount", "Enter an amount greater than zero.");
        }

        if (input.Type is not ("Income" or "Expense"))
        {
            ModelState.AddModelError($"{keyPrefix}.Type", "Choose income or expense.");
        }
    }

    private static Transaction ToTransaction(TransactionInput input) => new()
    {
        Date = input.Date,
        Description = input.Description.Trim(),
        Category = input.Category.Trim(),
        Type = input.Type,
        Amount = input.Amount
    };

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    public class TransactionInput
    {
        public int Id { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime Date { get; set; } = DateTime.Today;

        [Required]
        [StringLength(80)]
        public string Description { get; set; } = string.Empty;

        [Required]
        [StringLength(40)]
        public string Category { get; set; } = string.Empty;

        [Required]
        public string Type { get; set; } = "Expense";

        [Range(typeof(decimal), "0.01", "999999999")]
        public decimal Amount { get; set; }
    }

    public class BudgetInput
    {
        public string YearMonth { get; set; } = string.Empty;

        [StringLength(40)]
        public string Category { get; set; } = string.Empty;

        [Range(typeof(decimal), "0.01", "999999999")]
        public decimal LimitAmount { get; set; }
    }

    public record CategorySummary(string Category, decimal Spent, decimal? BudgetLimit, int? BudgetId, double PercentUsed);
}
