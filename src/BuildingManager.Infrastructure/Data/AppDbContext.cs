using BuildingManager.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace BuildingManager.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Lease> Leases => Set<Lease>();
    public DbSet<Charge> Charges => Set<Charge>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<PasswordResetCode> PasswordResetCodes => Set<PasswordResetCode>();
    public DbSet<EmailSettings> EmailSettings => Set<EmailSettings>();
    public DbSet<BusinessProfile> BusinessProfiles => Set<BusinessProfile>();
    public DbSet<BusinessLogo> BusinessLogos => Set<BusinessLogo>();
    public DbSet<StatementLogo> StatementLogos => Set<StatementLogo>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseReceipt> ExpenseReceipts => Set<ExpenseReceipt>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<decimal>().HavePrecision(18, 2);
        // Enums stored as text so the tables stay readable when querying in SSMS.
        builder.Properties<LeaseStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<RentFrequency>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<EmailSecurity>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<ChargeType>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<PaymentMethod>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<InvoiceStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<ExpenseCategoryKind>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Property>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(500);
        });

        b.Entity<Unit>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => new { x.PropertyId, x.Name }).IsUnique();
        });

        b.Entity<Tenant>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.Tin).HasMaxLength(20);
            e.Property(x => x.BusinessType).HasMaxLength(100);
        });

        b.Entity<Charge>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(300);
            // Guarantees the rent generator can never bill the same period twice, even if it runs concurrently.
            // Rent voided by a lease ending is left out so the period can be re-billed if the lease is extended.
            e.HasIndex(x => new { x.LeaseId, x.PeriodStart })
                .IsUnique()
                .HasFilter("[Type] = 'Rent' AND [PeriodStart] IS NOT NULL AND [VoidedByLeaseEnd] = 0");
        });

        b.Entity<Payment>(e => e.Property(x => x.Reference).HasMaxLength(100));

        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.Username).HasMaxLength(100);
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(500);
            e.Property(x => x.Email).HasMaxLength(200);
        });

        b.Entity<PasswordResetCode>(e =>
        {
            e.Property(x => x.CodeHash).HasMaxLength(64);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<EmailSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Host).HasMaxLength(200);
            e.Property(x => x.Username).HasMaxLength(200);
            e.Property(x => x.ProtectedPassword).HasMaxLength(2000);
            e.Property(x => x.FromAddress).HasMaxLength(200);
            e.Property(x => x.FromName).HasMaxLength(200);
        });

        b.Entity<BusinessLogo>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ContentType).HasMaxLength(100);
        });

        b.Entity<StatementLogo>(e =>
        {
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.HasIndex(x => x.Sha256).IsUnique();
            e.Property(x => x.ContentType).HasMaxLength(100);
        });

        b.Entity<BusinessProfile>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.Tin).HasMaxLength(20);
            e.Property(x => x.Contact).HasMaxLength(200);
            e.Property(x => x.PaymentInstructions).HasMaxLength(1000);
            e.Property(x => x.DocumentTitle).HasMaxLength(100);
            e.Property(x => x.NumberPrefix).HasMaxLength(10);
            e.Property(x => x.FooterNote).HasMaxLength(500);
        });

        b.Entity<Invoice>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(30);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasOne(x => x.Logo).WithMany().HasForeignKey(x => x.LogoId).OnDelete(DeleteBehavior.Restrict);
            // Backstop for the numbering lock: a number can never be issued twice.
            e.HasIndex(x => new { x.Year, x.Sequence }).IsUnique();
            e.Property(x => x.VoidReason).HasMaxLength(300);
            e.Property(x => x.DocumentTitle).HasMaxLength(100);
            e.Property(x => x.BusinessName).HasMaxLength(200);
            e.Property(x => x.BusinessAddress).HasMaxLength(500);
            e.Property(x => x.BusinessTin).HasMaxLength(20);
            e.Property(x => x.BusinessContact).HasMaxLength(200);
            e.Property(x => x.PaymentInstructions).HasMaxLength(1000);
            e.Property(x => x.FooterNote).HasMaxLength(500);
            e.Property(x => x.TenantName).HasMaxLength(200);
            e.Property(x => x.TenantTin).HasMaxLength(20);
            e.Property(x => x.PropertyName).HasMaxLength(200);
            e.Property(x => x.PropertyAddress).HasMaxLength(500);
            e.Property(x => x.UnitName).HasMaxLength(100);
            e.Property(x => x.Notes).HasMaxLength(1000);
        });

        b.Entity<InvoiceLine>(e => e.Property(x => x.Description).HasMaxLength(300));

        b.Entity<ExpenseCategory>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasData(DefaultExpenseCategories.All);
        });

        b.Entity<Vendor>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Contact).HasMaxLength(200);
            e.Property(x => x.Tin).HasMaxLength(20);
        });

        b.Entity<Expense>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasIndex(x => x.Date);
        });

        b.Entity<ExpenseReceipt>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.ContentType).HasMaxLength(100);
        });

        // Financial history must never disappear through a cascade; SQL Server also rejects
        // the multiple cascade paths Lease -> Charge/Payment -> Allocation would create.
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }
}
