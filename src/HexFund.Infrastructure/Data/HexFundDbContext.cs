using HexFund.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace HexFund.Infrastructure.Data;

/// <summary>
/// Database context for the Finance Planner application
/// </summary>
public class HexFundDbContext : DbContext
{
    public HexFundDbContext(DbContextOptions<HexFundDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<UserCategory> UserCategories => Set<UserCategory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var converter = new UtcDateTimeOffsetOffsetConverter();

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                    property.SetValueConverter(converter);

                if (property.ClrType == typeof(DateTimeOffset?))
                    property.SetValueConverter(converter);
            }
        }

        base.OnModelCreating(modelBuilder);

        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Email).HasMaxLength(255).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(255).IsRequired();
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(u => u.HasCustomName).HasDefaultValue(false);
        });

        // Account configuration
        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(e => e.AccountId);
            entity.Property(e => e.AccountName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.InitialBalance).HasPrecision(18, 2);
            entity.Property(e => e.CurrentBalance).HasPrecision(18, 2);
            entity.Property(e => e.Currency).HasMaxLength(3).HasDefaultValue("USD");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.User)
                .WithMany(u => u.Accounts)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Transaction configuration
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.TransactionId);
            entity.Property(e => e.Description).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2).IsRequired();
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.Frequency).IsRequired();
            entity.Property(e => e.StartDate).IsRequired();
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Color).HasMaxLength(7).IsRequired(false);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne<Transaction>()
                .WithMany()
                .HasForeignKey(e => e.PredecessorTransactionId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            entity.HasOne(e => e.Account)
                .WithMany(a => a.Transactions)
                .HasForeignKey(e => e.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // UserCategory configuration
        modelBuilder.Entity<UserCategory>(entity =>
        {
            entity.HasKey(c => c.CategoryId);

            entity.Property(c => c.Name)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.HasIndex(c => new { c.UserId, c.Name })
                  .IsUnique()
                  .HasDatabaseName("IX_UserCategories_UserId_Name");

            entity.HasOne(c => c.User)
                  .WithMany(u => u.Categories)
                  .HasForeignKey(c => c.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}