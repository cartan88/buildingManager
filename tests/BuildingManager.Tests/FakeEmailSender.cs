using System.Collections.Concurrent;
using BuildingManager.Api.Email;

namespace BuildingManager.Tests;

/// <summary>Stands in for the SMTP sender in API tests: keeps what would have been emailed.</summary>
public class FakeEmailSender : IEmailSender
{
    public bool Configured { get; set; } = true;
    private readonly ConcurrentQueue<OutgoingEmail> _sent = new();
    public IReadOnlyCollection<OutgoingEmail> Sent => _sent;

    public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(Configured);

    public Task SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        _sent.Enqueue(email);
        return Task.CompletedTask;
    }
}
