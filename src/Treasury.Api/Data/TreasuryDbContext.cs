using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Accounts;
using Treasury.Api.Auth;
using Treasury.Api.Imports;
using Treasury.Api.Categories;
using Treasury.Api.ExchangeRates;
using Treasury.Api.Rules;
using Treasury.Api.Tags;
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

    public DbSet<MultiCurrencyAccount> MultiCurrencyAccounts => Set<MultiCurrencyAccount>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<SourceFile> SourceFiles => Set<SourceFile>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<TransactionTag> TransactionTags => Set<TransactionTag>();

    public DbSet<TransactionTagRemoval> TransactionTagRemovals => Set<TransactionTagRemoval>();

    public DbSet<Rule> Rules => Set<Rule>();

    public DbSet<RuleTag> RuleTags => Set<RuleTag>();

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

        builder.Entity<MultiCurrencyAccount>(g =>
        {
            g.Ignore(x => x.Owner);
            g.Property(x => x.Name).HasMaxLength(AccountEndpoints.MaxNameLength);
            g.Property(x => x.Slug).HasMaxLength(64);
            g.HasIndex(x => x.Slug).IsUnique();
            g.Property(x => x.IntegrationKey).HasMaxLength(64);
            g.Property(x => x.MainCurrency).HasMaxLength(3);
            g.HasOne<Member>().WithMany().HasForeignKey(x => x.OwnerMemberId).OnDelete(DeleteBehavior.Restrict);
            // Its Accounts are never detached; a group with Accounts can't be deleted.
            g.HasMany(x => x.Accounts).WithOne().HasForeignKey(x => x.MultiCurrencyAccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Transaction>(t =>
        {
            t.Ignore(x => x.Owner);
            t.Ignore(x => x.Type);
            t.Ignore(x => x.IsTransfer);
            // The other leg of a Transfer/Contribution. Deleting a leg (manual delete, or its Source file with #10's
            // undo) breaks the link: the other leg's pointer becomes null, so it is no longer a Transfer.
            t.HasOne<Transaction>().WithOne().HasForeignKey<Transaction>(x => x.LinkedTransactionId)
                .OnDelete(DeleteBehavior.SetNull);
            t.Property(x => x.LinkKind).HasConversion<string>().HasMaxLength(16);
            t.Ignore(x => x.FactsEditable);
            t.Property(x => x.Origin).HasConversion<string>().HasMaxLength(16);
            t.Property(x => x.Amount).HasPrecision(19, 4);
            t.Property(x => x.Description).HasMaxLength(TransactionEndpoints.MaxDescriptionLength);
            t.Property(x => x.Note).HasMaxLength(TransactionEndpoints.MaxNoteLength);
            t.Property(x => x.DisplayName).HasMaxLength(TransactionEndpoints.MaxDisplayNameLength);
            t.Property(x => x.Counterparty).HasMaxLength(TransactionEndpoints.MaxDescriptionLength);
            t.Property(x => x.Reference).HasMaxLength(ImportPipeline.MaxReferenceLength);
            t.Property(x => x.OriginalAmount).HasPrecision(19, 4);
            t.Property(x => x.OriginalCurrency).HasMaxLength(3);
            // Undoing an import (#10) = deleting its Source file, which takes its Transactions with it.
            t.HasOne(x => x.SourceFile).WithMany().HasForeignKey(x => x.SourceFileId).OnDelete(DeleteBehavior.Cascade);
            t.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            // Duplicate detection (#9) looks rows up by Account + date (facts key) or Account + reference.
            t.HasIndex(x => new { x.AccountId, x.Date });
            t.HasIndex(x => new { x.AccountId, x.Reference }).HasFilter("\"Reference\" IS NOT NULL");
            // Owner: null = the Family (same storage as Account.Owner).
            t.HasOne<Member>().WithMany().HasForeignKey(x => x.OwnerMemberId).OnDelete(DeleteBehavior.Restrict);
            // Null = Uncategorised. A used Category can only be removed after moving or uncategorising its Transactions.
            t.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            t.HasIndex(x => x.CategoryId);
        });

        builder.Entity<Category>(c =>
        {
            c.Ignore(x => x.IsBuiltIn);
            c.Property(x => x.Name).HasMaxLength(CategoryEndpoints.MaxNameLength);
            c.Property(x => x.BuiltInKey).HasMaxLength(64);
            c.HasIndex(x => x.BuiltInKey).IsUnique();
            c.HasOne<Category>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SourceFile>(f =>
        {
            f.Property(x => x.FileName).HasMaxLength(255);
            f.Property(x => x.StoredPath).HasMaxLength(500);
            f.Property(x => x.ContentHash).HasMaxLength(64);
            f.Property(x => x.ClosingBalanceAmount).HasPrecision(19, 4);
            f.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            f.HasIndex(x => new { x.AccountId, x.ImportedAt });
            f.HasOne<Member>().WithMany().HasForeignKey(x => x.ImportedByMemberId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Tag>(t =>
        {
            t.Ignore(x => x.HasDateRange);
            t.Ignore(x => x.IsOwnerLimited);
            t.Ignore(x => x.LimitedToOwners);
            t.Property(x => x.Name).HasMaxLength(TagEndpoints.MaxNameLength);
        });

        // A Transaction carries a Tag when at least one link (any source) exists. Deleting the Tag or the
        // Transaction deletes its links and removals.
        builder.Entity<TransactionTag>(l =>
        {
            l.HasKey(x => new { x.TransactionId, x.TagId, x.Source });
            l.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            l.HasOne<Transaction>().WithMany(x => x.TagLinks).HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
            l.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
            l.HasIndex(x => x.TagId);
        });

        builder.Entity<TransactionTagRemoval>(r =>
        {
            r.HasKey(x => new { x.TransactionId, x.TagId });
            r.HasOne<Transaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
            r.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
            r.HasIndex(x => x.TagId);
        });

        builder.Entity<Rule>(r =>
        {
            r.Ignore(x => x.Owner);
            r.Property(x => x.Field).HasConversion<string>().HasMaxLength(16);
            r.Property(x => x.Match).HasConversion<string>().HasMaxLength(16);
            r.Property(x => x.Text).HasMaxLength(RuleEndpoints.MaxTextLength);
            r.HasIndex(x => x.Priority);
            // Removing a Category takes it out of Rules (moving its Transactions moves the Rules too, #13 endpoint).
            r.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
            r.HasOne<Member>().WithMany().HasForeignKey(x => x.OwnerMemberId).OnDelete(DeleteBehavior.Restrict);
            r.HasOne<Member>().WithMany().HasForeignKey(x => x.AuthorMemberId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RuleTag>(l =>
        {
            l.HasKey(x => new { x.RuleId, x.TagId });
            l.HasOne<Rule>().WithMany(x => x.TagLinks).HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);
            l.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
            l.HasIndex(x => x.TagId);
        });

        builder.Entity<ExchangeRate>(r =>
        {
            r.HasKey(x => new { x.Date, x.Currency });
            r.Property(x => x.Currency).HasMaxLength(3);
            r.Property(x => x.PerEur).HasPrecision(19, 6);
        });
    }
}
