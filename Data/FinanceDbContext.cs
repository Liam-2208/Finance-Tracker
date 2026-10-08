using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using PocketLedger.Models;

namespace PocketLedger.Data;

public class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<BankConnection> BankConnections => Set<BankConnection>();
    public DbSet<BankLinkState> BankLinkStates => Set<BankLinkState>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (Database.IsNpgsql()) modelBuilder.HasDefaultSchema("finance");
        modelBuilder.Entity<Transaction>()
            .Property(transaction => transaction.Amount)
            .HasPrecision(12, 2);
        if (Database.IsNpgsql())
        {
            // Transaction dates are calendar dates entered by users, not UTC instants.
            // Keep the EF mapping aligned with DatabaseInitializer's PostgreSQL schema.
            modelBuilder.Entity<Transaction>()
                .Property(transaction => transaction.Date)
                .HasColumnType("timestamp without time zone");
        }

        modelBuilder.Entity<Budget>()
            .Property(budget => budget.LimitAmount)
            .HasPrecision(12, 2);
    }
}
