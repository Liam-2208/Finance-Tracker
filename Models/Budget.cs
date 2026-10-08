using System.ComponentModel.DataAnnotations;

namespace PocketLedger.Models;

public class Budget
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [StringLength(7)]
    public string YearMonth { get; set; } = string.Empty;

    [StringLength(40)]
    public string Category { get; set; } = string.Empty;

    public decimal LimitAmount { get; set; }
}
