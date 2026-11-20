using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Accounts;
using Treasury.Api.Auth;
using Treasury.Api.Transactions;

namespace Treasury.Api.Data;

// Registered with DbContext pooling (Aspire AddNpgsqlDbContext): keep a single public
// constructor taking DbContextOptions and don't inject scoped services.
public class TreasuryDbContext(DbContextOptions<TreasuryDbContext> options)
    : IdentityDbContext<Member, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    /// Cookie encryption keys, kept in the database so restarts don't log everyone out.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Account>(a =>
        {
            a.Ignore(x => x.Owner);
            a.Property(x => x.Name).HasMaxLength(AccountEndpoints.MaxNameLength);
            a.Property(x => x.Slug).HasMaxLength(64);
            a.HasIndex(x => x.Slug).IsUnique();
            a.Property(x => x.IntegrationKey).HasMaxLength(64);
            a.Property(x => x.Currency).HasMaxLength(3);
            a.Property(x => x.OpeningBalance).HasPrecision(19, 4);
            a.Property(x => x.Identifier).HasMaxLength(AccountEndpoints.MaxIdentifierLength);
            // Owner: null = the Family. A Member who owns Accounts can't be deleted (handover first, #5).
            a.HasOne<Member>().WithMany().HasForeignKey(x => x.OwnerMemberId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Transaction>(t =>
        {
            t.Ignore(x => x.Owner);
            t.Ignore(x => x.Type);
            t.Ignore(x => x.FactsEditable);
            t.Property(x => x.Origin).HasConversion<string>().HasMaxLength(16);
            t.Property(x => x.Amount).HasPrecision(19, 4);
            t.Property(x => x.Description).HasMaxLength(TransactionEndpoints.MaxDescriptionLength);
            t.Property(x => x.Note).HasMaxLength(TransactionEndpoints.MaxNoteLength);
            t.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            t.HasIndex(x => new { x.AccountId, x.Date });
            // Owner: null = the Family (same storage as Account.Owner).
            t.HasOne<Member>().WithMany().HasForeignKey(x => x.OwnerMemberId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
