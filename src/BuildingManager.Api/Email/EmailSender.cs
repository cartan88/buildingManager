using BuildingManager.Core.Entities;
using BuildingManager.Infrastructure.Data;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using MimeKit;

namespace BuildingManager.Api.Email;

public record OutgoingEmail(string To, string Subject, string Text, string Html);

/// <summary>Sends email. Tests swap in a fake that records what would have been sent.</summary>
public interface IEmailSender
{
    /// <summary>True when an email account is set up in Settings.</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <exception cref="EmailException">The email couldn't be sent; the message says why in plain words.</exception>
    Task SendAsync(OutgoingEmail email, CancellationToken ct = default);
}

public class EmailException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Sends through the SMTP account saved under Settings (e.g. smtp.gmail.com with an app password).</summary>
public class SmtpEmailSender(AppDbContext db, IDataProtectionProvider protection) : IEmailSender
{
    public const string PasswordPurpose = "BuildingManager.EmailPassword";

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        await db.EmailSettings.FindAsync([EmailSettings.SingletonId], ct) is { Host.Length: > 0, FromAddress.Length: > 0 };

    public async Task SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        var settings = await db.EmailSettings.FindAsync([EmailSettings.SingletonId], ct);
        if (settings is not { Host.Length: > 0, FromAddress.Length: > 0 })
            throw new EmailException("Email isn't set up yet. Fill in Settings → Password reset email.");

        string? password = null;
        if (settings.ProtectedPassword is { } protectedPassword)
        {
            try { password = protection.CreateProtector(PasswordPurpose).Unprotect(protectedPassword); }
            catch (Exception e) // e.g. the encryption keys were lost when Windows was reinstalled
            {
                throw new EmailException("The saved email password can't be read any more. Enter it again in Settings.", e);
            }
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName ?? "", settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(email.To));
        message.Subject = email.Subject;
        message.Body = new BodyBuilder { TextBody = email.Text, HtmlBody = email.Html }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 20_000 };
        try
        {
            await client.ConnectAsync(settings.Host, settings.Port,
                settings.Security == EmailSecurity.SslOnConnect ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
            if (!string.IsNullOrEmpty(settings.Username)) await client.AuthenticateAsync(settings.Username, password ?? "", ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
        }
        catch (AuthenticationException e)
        {
            throw new EmailException("The email server didn't accept the username or password. For Gmail, use an app password, not your normal password.", e);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new EmailException($"Couldn't send the email: {e.Message}", e);
        }
    }
}
