using BuildingManager.Core.Entities;

namespace BuildingManager.Infrastructure.Data;

/// <summary>Starting categories for a Philippine rental business; users can rename, add or archive them.</summary>
public static class DefaultExpenseCategories
{
    public static readonly ExpenseCategory[] All =
    [
        Op(1, "Repairs & maintenance"),
        Op(2, "Utilities (water, electricity, internet)"),
        Op(3, "Association / condo dues"),
        Op(4, "Real property tax (amilyar)"),
        Op(5, "Insurance"),
        Op(6, "Mortgage / loan interest"),
        Op(7, "Property management fees"),
        Op(8, "Professional fees (accounting, legal)"),
        Op(9, "Cleaning, security & garbage"),
        Op(10, "Supplies"),
        Op(11, "Advertising & listing fees"),
        Op(12, "Taxes, permits & licenses"),
        Op(13, "Bank & payment fees"),
        Op(14, "Other expenses"),
        Cap(15, "Capital improvements (renovations)"),
        Cap(16, "Furniture & appliances"),
    ];

    private static ExpenseCategory Op(int id, string name) =>
        new() { Id = id, Name = name, Kind = ExpenseCategoryKind.Operating, SortOrder = id * 10 };

    private static ExpenseCategory Cap(int id, string name) =>
        new() { Id = id, Name = name, Kind = ExpenseCategoryKind.Capital, SortOrder = id * 10 };
}
