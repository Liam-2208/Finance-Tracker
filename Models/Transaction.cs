using System.ComponentModel.DataAnnotations;

namespace PocketLedger.Models;

public class Transaction
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    public DateTime Date { get; set; }

    [StringLength(80)]
    public string Description { get; set; } = string.Empty;

    [StringLength(40)]
    public string Category { get; set; } = string.Empty;

    [StringLength(10)]
    public string Type { get; set; } = "Expense";

    public decimal Amount { get; set; }
}
