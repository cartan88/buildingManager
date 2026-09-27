namespace BuildingManager.Core.Entities;

public class Property
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public List<Unit> Units { get; set; } = [];
}

public class Unit
{
    public int Id { get; set; }
    public int PropertyId { get; set; }
    public Property? Property { get; set; }
    public required string Name { get; set; }
    /// <summary>Default asking rent; each lease stores its own agreed rent.</summary>
    public decimal DefaultMonthlyRent { get; set; }
    public string? Notes { get; set; }
    public List<Lease> Leases { get; set; } = [];
}

public class Tenant
{
    public int Id { get; set; }
    public required string FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    /// <summary>BIR Taxpayer Identification Number, needed for invoices to business tenants.</summary>
    public string? Tin { get; set; }
    public string? Notes { get; set; }
    public List<Lease> Leases { get; set; } = [];
}

public enum LeaseStatus { Active = 0, Ended = 1 }

public class Lease
{
    public int Id { get; set; }
    public int UnitId { get; set; }
    public Unit? Unit { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public DateOnly StartDate { get; set; }
    /// <summary>Null means month-to-month / open-ended.</summary>
    public DateOnly? EndDate { get; set; }
    public decimal MonthlyRent { get; set; }
    /// <summary>Day of month rent is due (1-31). Clamped to the last day in shorter months.</summary>
    public int DueDay { get; set; } = 1;
    /// <summary>Days after the due date before a charge counts as overdue.</summary>
    public int GracePeriodDays { get; set; }
    /// <summary>Held on behalf of the tenant: a liability, not income.</summary>
    public decimal SecurityDeposit { get; set; }
    public LeaseStatus Status { get; set; } = LeaseStatus.Active;
    public string? Notes { get; set; }

    public List<Charge> Charges { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}

public enum ChargeType { Rent = 0, Utility = 1, LateFee = 2, Other = 3 }

public class Charge
{
    public int Id { get; set; }
    public int LeaseId { get; set; }
    public Lease? Lease { get; set; }
    public ChargeType Type { get; set; }
    public required string Description { get; set; }
    /// <summary>For rent: first day of the rental period covered. Used to keep generation idempotent.</summary>
    public DateOnly? PeriodStart { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Financial records are never deleted; they are voided.</summary>
    public bool IsVoided { get; set; }
    /// <summary>
    /// Set when rent was voided automatically because the lease ended before this period.
    /// Unlike a manual void (e.g. waived rent), the period is billed again if the lease is extended.
    /// </summary>
    public bool VoidedByLeaseEnd { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<PaymentAllocation> Allocations { get; set; } = [];
}

public enum PaymentMethod { Cash = 0, BankTransfer = 1, GCash = 2, Maya = 3, Check = 4, Other = 5 }

public class Payment
{
    public int Id { get; set; }
    public int LeaseId { get; set; }
    public Lease? Lease { get; set; }
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    /// <summary>Bank / GCash reference no., check no., etc.</summary>
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public bool IsVoided { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<PaymentAllocation> Allocations { get; set; } = [];
}

/// <summary>Applies part (or all) of a payment to a charge.</summary>
public class PaymentAllocation
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    public Payment? Payment { get; set; }
    public int ChargeId { get; set; }
    public Charge? Charge { get; set; }
    public decimal Amount { get; set; }
}
