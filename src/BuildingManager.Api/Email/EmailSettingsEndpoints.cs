using System.Net.Mail;
using System.Security.Claims;
using BuildingManager.Api.Endpoints;
using BuildingManager.Core.Entities;
using BuildingManager.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace BuildingManager.Api.Email;

/// <param name="Password">Null keeps the saved password; it's never sent back to the browser.</param>
public record EmailSettingsInput(string Host, int Port, EmailSecurity Security, string? Username, string? Password,
    string FromAddress, string? FromName, string? AccountEmail);

/// <summary>Settings → Password reset email: the account reset codes are sent from, and where they go.</summary>
public static class EmailSettingsEndpoints
{
    public static void MapEmailSettingsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/settings/email", async (HttpContext http, AppDbContext db) =>
        {
            var s = await db.EmailSettings.FindAsync(EmailSettings.SingletonId) ?? new EmailSettings();
            var user = await CurrentUserAsync(http, db);
            return new
            {
                s.Host, s.Port, s.Security, s.Username, HasPassword = s.ProtectedPassword != null, s.FromAddress, s.FromName,
                AccountEmail = user?.Email,
            };
        });

        api.MapPut("/settings/email", async (EmailSettingsInput input, HttpContext http, AppDbContext db, IDataProtectionProvider protection) =>
        {
            var host = input.Host?.Trim() ?? "";
            if (host.Length is 0 or > 200) return Validation.Fail("host", "Enter the email server, e.g. smtp.gmail.com.");
            if (input.Port is < 1 or > 65535) return Validation.Fail("port", "Port must be 1–65535 (usually 587 or 465).");
            if (!Enum.IsDefined(input.Security)) return Validation.Fail("security", "Choose a security option.");
            if (!IsEmail(input.FromAddress)) return Validation.Fail("fromAddress", "Enter the address emails are sent from.");
            if (!IsEmail(input.AccountEmail)) return Validation.Fail("accountEmail", "Enter the address to send reset codes to.");

            var s = await db.EmailSettings.FindAsync(EmailSettings.SingletonId);
            if (s is null) db.EmailSettings.Add(s = new EmailSettings());
            (s.Host, s.Port, s.Security, s.Username, s.FromAddress, s.FromName) =
                (host, input.Port, input.Security, Clean(input.Username), input.FromAddress.Trim(), Clean(input.FromName));
            if (!string.IsNullOrEmpty(input.Password))
                s.ProtectedPassword = protection.CreateProtector(SmtpEmailSender.PasswordPurpose).Protect(input.Password);

            var user = await CurrentUserAsync(http, db);
            if (user is not null) user.Email = input.AccountEmail!.Trim();
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/settings/email/test", async (HttpContext http, AppDbContext db, IEmailSender email) =>
        {
            var to = (await CurrentUserAsync(http, db))?.Email;
            if (to is null) return Validation.Fail("accountEmail", "Save an address to send reset codes to first.");
            try
            {
                await email.SendAsync(new OutgoingEmail(to, "Test email from Building Manager",
                    "Email is working. Password reset codes will be sent to this address.",
                    "<p>Email is working. Password reset codes will be sent to this address.</p>"));
            }
            catch (EmailException e) { return Validation.Fail("email", e.Message); }
            return Results.Ok(new { SentTo = to });
        });
    }

    private static bool IsEmail(string? s) => !string.IsNullOrWhiteSpace(s) && s.Trim().Length <= 200 && MailAddress.TryCreate(s.Trim(), out _);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static async Task<AppUser?> CurrentUserAsync(HttpContext http, AppDbContext db) =>
        int.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? await db.Users.FindAsync(id) : null;
}
