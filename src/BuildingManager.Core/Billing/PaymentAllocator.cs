namespace BuildingManager.Core.Billing;

public record OpenCharge(int ChargeId, DateOnly DueDate, decimal Outstanding);
public record UnappliedPayment(int PaymentId, DateOnly PaymentDate, decimal Unapplied);
public record Allocation(int PaymentId, int ChargeId, decimal Amount);

/// <summary>
/// Applies payment money to charges oldest-first (FIFO). Money left over stays as tenant
/// credit and is applied automatically when the next charge is created.
/// </summary>
public static class PaymentAllocator
{
    public static List<Allocation> Allocate(IEnumerable<OpenCharge> charges, IEnumerable<UnappliedPayment> payments)
    {
        var open = charges.Where(c => c.Outstanding > 0)
            .OrderBy(c => c.DueDate).ThenBy(c => c.ChargeId).ToList();
        var balances = open.Select(c => c.Outstanding).ToArray();
        var result = new List<Allocation>();
        var i = 0;

        foreach (var payment in payments.Where(p => p.Unapplied > 0).OrderBy(p => p.PaymentDate).ThenBy(p => p.PaymentId))
        {
            var remaining = payment.Unapplied;
            while (remaining > 0 && i < open.Count)
            {
                var amount = Math.Min(remaining, balances[i]);
                result.Add(new Allocation(payment.PaymentId, open[i].ChargeId, amount));
                remaining -= amount;
                balances[i] -= amount;
                if (balances[i] == 0) i++;
            }
        }
        return result;
    }

    /// <summary>
    /// Replays the payments against the charges from scratch and returns what is still owed on each
    /// charge. Used for "as of" reports, where only money received by that date may count.
    /// </summary>
    public static Dictionary<int, decimal> OutstandingByCharge(IReadOnlyCollection<OpenCharge> charges, IEnumerable<UnappliedPayment> payments)
    {
        var outstanding = charges.ToDictionary(c => c.ChargeId, c => c.Outstanding);
        foreach (var a in Allocate(charges, payments)) outstanding[a.ChargeId] -= a.Amount;
        return outstanding;
    }
}
