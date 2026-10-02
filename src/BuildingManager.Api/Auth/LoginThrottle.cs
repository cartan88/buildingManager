namespace BuildingManager.Api.Auth;

/// <summary>
/// Slows down password guessing: after <see cref="FreeAttempts"/> wrong passwords in a row, sign-in is
/// refused for <see cref="LockoutDuration"/>, and again after every further wrong password, until one is right.
/// Kept in memory: a restart clears it, which is fine for an app only reachable from this PC.
/// </summary>
public class LoginThrottle(TimeProvider clock)
{
    public const int FreeAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private int _failures;
    private DateTimeOffset _lockedUntil;

    /// <summary>How long until sign-in is allowed again, or null if it's allowed now.</summary>
    public TimeSpan? RetryAfter()
    {
        lock (_gate)
        {
            var wait = _lockedUntil - clock.GetUtcNow();
            return wait > TimeSpan.Zero ? wait : null;
        }
    }

    public void Failed()
    {
        lock (_gate)
        {
            if (++_failures >= FreeAttempts) _lockedUntil = clock.GetUtcNow() + LockoutDuration;
        }
    }

    public void Succeeded()
    {
        lock (_gate) (_failures, _lockedUntil) = (0, default);
    }
}
