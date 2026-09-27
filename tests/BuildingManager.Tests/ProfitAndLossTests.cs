using BuildingManager.Core.Entities;
using BuildingManager.Core.Reports;
using BuildingManager.Infrastructure.Expenses;
using BuildingManager.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;

namespace BuildingManager.Tests;

public class ProfitAndLossBuilderTests
{
    private static readonly List<PnlColumn> Cols = [new("a", "Bldg A"), new("b", "Bldg B")];

    [Fact]
    public void Totals_net_income_per_column_and_leaves_capital_out()
    {
        var report = ProfitAndLoss.Build(
        [
            new("a", PnlSection.Income, "Rent", 30000), new("b", PnlSection.Income, "Rent", 15000),
            new("a", PnlSection.OperatingExpense, "Repairs", 5000), new("a", PnlSection.OperatingExpense, "Repairs", 1000),
            new("b", PnlSection.CapitalExpense, "Renovation", 80000),
        ], Cols, []);

        Assert.Equal(24000, report.NetIncome.ByColumn["a"]);
        Assert.Equal(15000, report.NetIncome.ByColumn["b"]); // the 80k renovation doesn't reduce net income
        Assert.Equal(39000, report.NetIncome.Total);
        Assert.Equal(80000, report.CapitalExpenses.Total);
        Assert.Equal(6000, report.Lines.Single(l => l.Label == "Repairs").Amounts["a"]);
    }

    [Fact]
    public void Lines_follow_the_given_order_then_alphabetical_and_zero_lines_are_dropped()
    {
        var report = ProfitAndLoss.Build(
        [
            new("a", PnlSection.OperatingExpense, "Zebra fees", 1), new("a", PnlSection.OperatingExpense, "Apple fees", 1),
            new("a", PnlSection.OperatingExpense, "Utilities", 1), new("a", PnlSection.Income, "Other income", 1),
            new("a", PnlSection.Income, "Rent", 1), new("a", PnlSection.OperatingExpense, "Nothing", 0),
        ], Cols, ["Rent", "Other income", "Utilities"]);

        Assert.Equal(["Rent", "Other income", "Utilities", "Apple fees", "Zebra fees"], report.Lines.Select(l => l.Label));
    }

    [Fact]
    public void Entries_for_unknown_columns_are_ignored()
    {
        var report = ProfitAndLoss.Build([new("zzz", PnlSection.Income, "Rent", 999)], Cols, []);

        Assert.Empty(report.Lines);
        Assert.Equal(0, report.Income.Total);
    }

    [Fact]
    public void Month_columns_cover_every_month_the_range_touches()
    {
        var cols = ProfitAndLoss.MonthColumns(DateOnly.Parse("2026-11-15"), DateOnly.Parse("2027-02-03"));

        Assert.Equal(["2026-11", "2026-12", "2027-01", "2027-02"], cols.Select(c => c.Key));
        Assert.Equal("Nov 2026", cols[0].Label);
    }
}

public class ProfitAndLossServiceTests : DatabaseTest
{
    private ExpenseService Expenses(Infrastructure.Data.AppDbContext db) => new(db);
    private ProfitAndLossService Pnl(Infrastructure.Data.AppDbContext db) => new(db);

    private async Task<int> PropertyIdOfLeaseAsync(int leaseId)
    {
        await using var db = NewContext();
        return await db.Leases.Where(l => l.Id == leaseId).Select(l => l.Unit!.PropertyId).SingleAsync();
    }

    private async Task<int> AddExpenseAsync(int? propertyId, int categoryId, decimal amount, string date = "2026-09-10", string? vendor = null)
    {
        await using var db = NewContext();
        return await Expenses(db).CreateAsync(new ExpenseInput(D(date), propertyId, categoryId, vendor, "Test expense", amount, PaymentMethod.Cash, null, null));
    }

    [Fact]
    public async Task Cash_basis_counts_money_received_split_by_what_it_paid_for()
    {
        var leaseId = await SeedLeaseAsync(start: "2026-09-01", dueDay: 1);
        await using (var db = NewContext())
        {
            await Billing(db).GenerateRentChargesAsync(D("2026-09-30"));
            db.Charges.Add(new Charge { LeaseId = leaseId, Type = ChargeType.Utility, Description = "Water", DueDate = D("2026-09-05"), Amount = 800 });
            await db.SaveChangesAsync();
        }
        await PayAsync(leaseId, "2026-09-05", 20000); // 12,000 rent + 800 water + 7,200 advance
        await PayAsync(leaseId, "2026-10-01", 5000);  // outside the period
        var propertyId = await PropertyIdOfLeaseAsync(leaseId);
        await AddExpenseAsync(propertyId, categoryId: 1, amount: 3000); // repairs
        await AddExpenseAsync(null, categoryId: 8, amount: 2000);       // accountant, general
        await AddExpenseAsync(propertyId, categoryId: 16, amount: 25000); // aircon: capital

        await using var check = NewContext();
        var report = await Pnl(check).BuildAsync(D("2026-09-01"), D("2026-09-30"), PnlBasis.Cash, PnlGrouping.Property);

        var p = $"p{propertyId}";
        Assert.Equal(12000, report.Lines.Single(l => l.Label == "Rent").Amounts[p]);
        Assert.Equal(800, report.Lines.Single(l => l.Label == "Utilities billed to tenants").Amounts[p]);
        Assert.Equal(7200, report.Lines.Single(l => l.Label == ProfitAndLossService.UnappliedLine).Amounts[p]);
        Assert.Equal(20000, report.Income.Total);
        Assert.Equal(3000, report.OperatingExpenses.ByColumn[p]);
        Assert.Equal(2000, report.OperatingExpenses.ByColumn[ProfitAndLossService.GeneralColumn]);
        Assert.Equal(15000, report.NetIncome.Total); // 20,000 - 3,000 - 2,000
        Assert.Equal(25000, report.CapitalExpenses.Total);
        Assert.Equal(["Test Apartments", "General"], report.Columns.Select(c => c.Label));
    }

    [Fact]
    public async Task Accrual_basis_counts_billed_charges_whether_paid_or_not()
    {
        var leaseId = await SeedLeaseAsync(start: "2026-08-01", dueDay: 1);
        await using (var db = NewContext()) await Billing(db).GenerateRentChargesAsync(D("2026-09-30")); // Aug + Sep
        await PayAsync(leaseId, "2026-09-02", 5000);

        await using var check = NewContext();
        var sep = await Pnl(check).BuildAsync(D("2026-09-01"), D("2026-09-30"), PnlBasis.Accrual, PnlGrouping.Property);
        var cash = await Pnl(check).BuildAsync(D("2026-09-01"), D("2026-09-30"), PnlBasis.Cash, PnlGrouping.Property);

        Assert.Equal(12000, sep.Income.Total);  // only September's charge
        Assert.Equal(5000, cash.Income.Total);  // only what was received
    }

    [Fact]
    public async Task By_month_puts_each_amount_in_its_month_and_voided_expenses_are_left_out()
    {
        var leaseId = await SeedLeaseAsync(start: "2026-07-01", dueDay: 1);
        await using (var db = NewContext()) await Billing(db).GenerateRentChargesAsync(D("2026-09-30"));
        await PayAsync(leaseId, "2026-07-01", 12000);
        await PayAsync(leaseId, "2026-09-03", 24000);
        var propertyId = await PropertyIdOfLeaseAsync(leaseId);
        await AddExpenseAsync(propertyId, 1, 1500, "2026-08-20");
        var voided = await AddExpenseAsync(propertyId, 1, 99999, "2026-08-21");
        await using (var db = NewContext()) await Expenses(db).VoidAsync(voided);

        await using var check = NewContext();
        var report = await Pnl(check).BuildAsync(D("2026-07-01"), D("2026-09-30"), PnlBasis.Cash, PnlGrouping.Month);

        Assert.Equal(["Jul 2026", "Aug 2026", "Sep 2026"], report.Columns.Select(c => c.Label));
        Assert.Equal(12000, report.Income.ByColumn["2026-07"]);
        Assert.Equal(0, report.Income.ByColumn["2026-08"]);
        Assert.Equal(24000, report.Income.ByColumn["2026-09"]);
        Assert.Equal(1500, report.OperatingExpenses.ByColumn["2026-08"]);
    }

    [Fact]
    public async Task Filtering_by_property_leaves_out_other_properties_and_general_costs()
    {
        var a = await SeedLeaseAsync(start: "2026-09-01", dueDay: 1);
        var b = await SeedLeaseAsync(start: "2026-09-01", dueDay: 1);
        await using (var db = NewContext()) await Billing(db).GenerateRentChargesAsync(D("2026-09-30"));
        await PayAsync(a, "2026-09-01", 12000);
        await PayAsync(b, "2026-09-01", 12000);
        var propA = await PropertyIdOfLeaseAsync(a);
        await AddExpenseAsync(null, 8, 2000);

        await using var check = NewContext();
        var report = await Pnl(check).BuildAsync(D("2026-09-01"), D("2026-09-30"), PnlBasis.Cash, PnlGrouping.Property, propA);

        Assert.Single(report.Columns);
        Assert.Equal(12000, report.Income.Total);
        Assert.Equal(0, report.OperatingExpenses.Total);
    }
}

public class ExpenseServiceTests : DatabaseTest
{
    private ExpenseService Expenses(Infrastructure.Data.AppDbContext db) => new(db);

    private static ExpenseInput Input(string? vendor = null, int categoryId = 1, decimal amount = 500) =>
        new(DateOnly.Parse("2026-09-10"), null, categoryId, vendor, "Test", amount, PaymentMethod.GCash, "OR-1", null);

    [Fact]
    public async Task Default_categories_are_seeded()
    {
        await using var db = NewContext();
        var cats = await Expenses(db).CategoriesAsync();
        Assert.Contains(cats, c => c.Name == "Real property tax (amilyar)" && c.Kind == ExpenseCategoryKind.Operating);
        Assert.Contains(cats, c => c.Name == "Capital improvements (renovations)" && c.Kind == ExpenseCategoryKind.Capital);
        // New categories must not collide with the seeded ids.
        Assert.True(await Expenses(db).SaveCategoryAsync(null, "Pest control", ExpenseCategoryKind.Operating, false) > 16);
        // Operating categories are listed before capital ones.
        var kinds = (await Expenses(db).CategoriesAsync()).Select(c => c.Kind).ToList();
        Assert.Equal(kinds.OrderBy(k => k == ExpenseCategoryKind.Capital ? 1 : 0), kinds);
    }

    [Fact]
    public async Task Vendor_names_are_reused_ignoring_case_and_created_when_new()
    {
        await using var db = NewContext();
        await Expenses(db).CreateAsync(Input("Meralco"));
        await Expenses(db).CreateAsync(Input("meralco "));
        await Expenses(db).CreateAsync(Input("Maynilad"));
        await Expenses(db).CreateAsync(Input(null));

        Assert.Equal(["Maynilad", "Meralco"], await Expenses(db).VendorNamesAsync());
    }

    [Fact]
    public async Task Archived_categories_block_new_expenses_but_not_existing_ones()
    {
        await using var db = NewContext();
        var id = await Expenses(db).CreateAsync(Input(categoryId: 11));
        await Expenses(db).SaveCategoryAsync(11, "Advertising & listing fees", ExpenseCategoryKind.Operating, isArchived: true);

        await Assert.ThrowsAsync<ExpenseValidationException>(() => Expenses(db).CreateAsync(Input(categoryId: 11)));
        await Expenses(db).UpdateAsync(id, Input(categoryId: 11, amount: 750)); // editing an existing one is fine
        Assert.Equal(750, (await db.Expenses.AsNoTracking().SingleAsync()).Amount);
    }

    [Fact]
    public async Task Voided_expenses_cannot_be_edited_and_are_hidden_by_default()
    {
        await using var db = NewContext();
        var id = await Expenses(db).CreateAsync(Input());
        await Expenses(db).VoidAsync(id);

        await Assert.ThrowsAsync<ExpenseValidationException>(() => Expenses(db).UpdateAsync(id, Input()));
        Assert.Empty(await Expenses(db).ListAsync(new ExpenseFilter()));
        Assert.Single(await Expenses(db).ListAsync(new ExpenseFilter(IncludeVoided: true)));
    }

    [Theory]
    [InlineData("receipt.exe", 100, "PDF or a photo")]
    [InlineData("receipt.pdf", 0, "empty")]
    [InlineData("receipt.pdf", ExpenseService.MaxReceiptBytes + 1, "10 MB")]
    public async Task Receipt_uploads_are_checked(string name, long length, string expectedMessage)
    {
        await using var db = NewContext();
        var id = await Expenses(db).CreateAsync(Input());

        var e = await Assert.ThrowsAsync<ExpenseValidationException>(() =>
            Expenses(db).AddReceiptAsync(id, name, new MemoryStream(new byte[Math.Min(length, 16)]), length));
        Assert.Contains(expectedMessage, e.Message);
    }

    [Fact]
    public async Task Receipt_file_name_is_stripped_of_any_path()
    {
        await using var db = NewContext();
        var id = await Expenses(db).CreateAsync(Input());

        var receipt = await Expenses(db).AddReceiptAsync(id, @"..\..\secret\receipt.JPG", new MemoryStream([1, 2, 3]), 3);

        Assert.Equal("receipt.JPG", receipt.FileName);
        Assert.Equal("image/jpeg", receipt.ContentType);
    }
}
