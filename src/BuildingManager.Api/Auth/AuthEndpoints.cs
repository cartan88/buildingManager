using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BuildingManager.Api.Email;
using BuildingManager.Api.Endpoints;
using BuildingManager.Core.Entities;
using BuildingManager.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BuildingManager.Api.Auth;

public record CredentialsInput(string Username, string Password);
public record ChangePasswordInput(string CurrentPassword, string NewPassword);
public record ForgotPasswordInput(string Username);
public record ResetPasswordInput(string Username, string Code, string NewPassword);

/// <summary>
/// Sign-in with a single owner account. The first visit creates it; after that every /api call needs the
/// sign-in cookie, except these endpoints and the business name and logo shown on the sign-in page.
/// </summary>
public static class AuthEndpoints
{
    public const int MinPasswordLength = 8;
    public static readonly TimeSpan ResetCodeLifetime = TimeSpan.FromMinutes(30);
    public const int MaxResetCodeAttempts = 5;
    /// <summary>Unambiguous letters and digits only (no 0/O, 1/I/L), so a code read off a phone is easy to type.</summary>
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    /// <summary>Claim holding the password's change time, checked on every request (see <see cref="ValidateSessionAsync"/>).</summary>
    private const string PasswordVersionClaim = "pwd";

    private static readonly PasswordHasher<AppUser> Hasher = new();
    /// <summary>Checked against when the username is unknown, so a wrong username takes as long as a wrong password.</summary>
    private static readonly string DummyHash = Hasher.HashPassword(new AppUser { Username = "", PasswordHash = "" }, "not-a-real-password");

    public static void AddAppAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<LoginThrottle>();
        services.AddAuthorization();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
        {
            o.Cookie.Name = "BuildingManager.Auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.ExpireTimeSpan = TimeSpan.FromHours(12);
            o.SlidingExpiration = true;
            // This is an API for a single-page app: answer 401/403 instead of redirecting to a login URL.
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            o.Events.OnValidatePrincipal = ValidateSessionAsync;
        });
    }

    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/auth").AllowAnonymous();
        MapPasswordReset(g);

        g.MapGet("/status", async (HttpContext http, AppDbContext db, IEmailSender email) => new
        {
            SetupRequired = !await db.Users.AnyAsync(),
            SignedIn = http.User.Identity?.IsAuthenticated == true,
            Username = http.User.Identity?.Name,
            // Whether the sign-in page offers "Forgot password?" by email.
            EmailReset = await db.Users.AnyAsync(u => u.Email != null) && await email.IsConfiguredAsync(),
        });

        g.MapPost("/setup", async (CredentialsInput input, HttpContext http, AppDbContext db) =>
        {
            var username = input.Username?.Trim() ?? "";
            if (username.Length is 0 or > 100) return Validation.Fail("username", "Enter a username of up to 100 characters.");
            if (PasswordProblem(input.Password) is { } problem) return Validation.Fail("password", problem);

            AppUser? user = null;
            await db.InAppLockAsync(AppLocks.AccountSetup, async () =>
            {
                if (await db.Users.AnyAsync()) return; // someone finished setting up first
                // A username that's an email address is also where reset codes go (changeable in Settings).
                user = new AppUser { Username = username, PasswordHash = "", Email = MailAddress.TryCreate(username, out _) ? username : null };
                user.PasswordHash = Hasher.HashPassword(user, input.Password);
                db.Users.Add(user);
                await db.SaveChangesAsync();
            });
            if (user is null) return Validation.Fail("setup", "An account already exists. Sign in instead.");

            await SignInAsync(http, user);
            return Results.NoContent();
        });

        g.MapPost("/login", async (CredentialsInput input, HttpContext http, AppDbContext db, LoginThrottle throttle) =>
        {
            if (throttle.RetryAfter() is { } wait)
                return TooManyAttempts(http, wait);

            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == (input.Username ?? "").Trim());
            var result = Hasher.VerifyHashedPassword(user!, user?.PasswordHash ?? DummyHash, input.Password ?? "");
            if (user is null) result = PasswordVerificationResult.Failed;
            if (result == PasswordVerificationResult.Failed)
            {
                throttle.Failed();
                return throttle.RetryAfter() is { } lockout
                    ? TooManyAttempts(http, lockout)
                    : Results.Problem(title: "Wrong username or password.", statusCode: StatusCodes.Status401Unauthorized);
            }

            throttle.Succeeded();
            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user!.PasswordHash = Hasher.HashPassword(user, input.Password!);
                await db.SaveChangesAsync();
            }
            await SignInAsync(http, user!);
            return Results.NoContent();
        });

        g.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        g.MapPost("/password", async (ChangePasswordInput input, HttpContext http, AppDbContext db) =>
        {
            var user = await CurrentUserAsync(http, db);
            if (user is null) return Results.Unauthorized();
            if (Hasher.VerifyHashedPassword(user, user.PasswordHash, input.CurrentPassword ?? "") == PasswordVerificationResult.Failed)
                return Validation.Fail("currentPassword", "Your current password isn't right.");
            if (PasswordProblem(input.NewPassword) is { } problem) return Validation.Fail("newPassword", problem);

            user.PasswordHash = Hasher.HashPassword(user, input.NewPassword);
            user.PasswordChangedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await SignInAsync(http, user); // keep this session; any other session is signed out
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static void MapPasswordReset(RouteGroupBuilder g)
    {
        // Emails a one-time code (and a link with it filled in) to the account's email address.
        g.MapPost("/forgot", async (ForgotPasswordInput input, HttpContext http, AppDbContext db, IEmailSender email, TimeProvider clock) =>
        {
            const string sent = "If that username is right, a reset code is on its way to its email address. It works for 30 minutes.";
            if (!await email.IsConfiguredAsync())
                return Validation.Fail("email", "Password reset by email isn't set up. Use the reset command in the README instead.");

            var username = (input.Username ?? "").Trim();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user?.Email is null) return Results.Ok(new { Message = sent });

            var now = clock.GetUtcNow().UtcDateTime;
            // One email a minute at most, however often the button is pressed.
            var recent = now.AddMinutes(-1);
            if (await db.PasswordResetCodes.AnyAsync(c => c.UserId == user.Id && c.CreatedAt > recent))
                return Results.Ok(new { Message = sent });

            var code = NewResetCode();
            await db.PasswordResetCodes.Where(c => c.UserId == user.Id).ExecuteDeleteAsync(); // only the newest code works
            db.PasswordResetCodes.Add(new PasswordResetCode
            {
                UserId = user.Id, CodeHash = HashResetCode(code), CreatedAt = now, ExpiresAt = now + ResetCodeLifetime,
            });
            await db.SaveChangesAsync();

            var business = await db.BusinessProfiles.Select(p => p.Name).FirstOrDefaultAsync() is { Length: > 0 } n ? n : "Building Manager";
            var link = $"{http.Request.Scheme}://{http.Request.Host}/reset-password?user={Uri.EscapeDataString(user.Username)}&code={code}";
            try
            {
                await email.SendAsync(new OutgoingEmail(user.Email, $"Reset your password – {business}",
                    $"Someone (hopefully you) asked to reset the password for {user.Username}.\n\n" +
                    $"Your reset code is: {code}\n\nType it on the sign-in page, or open this link on the PC that runs the app:\n{link}\n\n" +
                    "The code works once, for 30 minutes. If you didn't ask for this, you can ignore this email.",
                    $"<p>Someone (hopefully you) asked to reset the password for <b>{WebUtility.HtmlEncode(user.Username)}</b>.</p>" +
                    $"<p>Your reset code is:</p><p style=\"font-size:24px;font-weight:bold;letter-spacing:3px\">{code}</p>" +
                    $"<p>Type it on the sign-in page, or <a href=\"{WebUtility.HtmlEncode(link)}\">open this link</a> on the PC that runs the app.</p>" +
                    "<p>The code works once, for 30 minutes. If you didn't ask for this, you can ignore this email.</p>"));
            }
            catch (EmailException e)
            {
                // Saying so reveals that the username was right, but only to someone at this PC, and it beats waiting for an email that never comes.
                return Validation.Fail("email", e.Message);
            }
            return Results.Ok(new { Message = sent });
        });

        g.MapPost("/reset", async (ResetPasswordInput input, HttpContext http, AppDbContext db, LoginThrottle throttle, TimeProvider clock) =>
        {
            const string wrong = "That code is wrong or has expired. Ask for a new one if you need to.";
            if (PasswordProblem(input.NewPassword) is { } problem) return Validation.Fail("newPassword", problem);

            var now = clock.GetUtcNow().UtcDateTime;
            var username = (input.Username ?? "").Trim();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            var code = user is null ? null : await db.PasswordResetCodes
                .Where(c => c.UserId == user.Id && c.UsedAt == null && c.ExpiresAt > now)
                .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync();
            if (code is null) return Validation.Fail("code", wrong);

            var matches = CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(code.CodeHash), Convert.FromHexString(HashResetCode(input.Code)));
            if (!matches)
            {
                if (++code.FailedAttempts >= MaxResetCodeAttempts) code.UsedAt = now; // too many guesses: the code is spent
                await db.SaveChangesAsync();
                return Validation.Fail("code", wrong);
            }

            user!.PasswordHash = Hasher.HashPassword(user, input.NewPassword);
            user.PasswordChangedAt = now; // signs out any other session
            code.UsedAt = now;
            await db.SaveChangesAsync();
            throttle.Succeeded();
            await SignInAsync(http, user);
            return Results.NoContent();
        });
    }

    /// <summary>8 characters shown as XXXX-XXXX: about 40 bits, plenty for a code that allows 5 tries and lasts 30 minutes.</summary>
    private static string NewResetCode()
    {
        var c = RandomNumberGenerator.GetItems<char>(CodeAlphabet, 8);
        return $"{new string(c, 0, 4)}-{new string(c, 4, 4)}";
    }

    /// <summary>Ignores case, spaces and dashes, so "k7qf 9m2x" matches "K7QF-9M2X".</summary>
    private static string HashResetCode(string? code)
    {
        var normalized = new string((code ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    /// <summary>For "forgot my password": removes the account so the next visit shows the set-up screen. Data is untouched.</summary>
    public static async Task<int> ResetLoginAsync(AppDbContext db) => await db.Users.ExecuteDeleteAsync();

    private static string? PasswordProblem(string? password) =>
        password is null || password.Length < MinPasswordLength ? $"Use a password of at least {MinPasswordLength} characters."
        : password.Length > 128 ? "Use a password of up to 128 characters."
        : null;

    private static IResult TooManyAttempts(HttpContext http, TimeSpan wait)
    {
        var seconds = (int)Math.Ceiling(wait.TotalSeconds);
        http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        return Results.Problem(title: $"Too many wrong passwords. Try again in {seconds} seconds.", statusCode: StatusCodes.Status429TooManyRequests);
    }

    private static Task SignInAsync(HttpContext http, AppUser user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(PasswordVersionClaim, user.PasswordChangedAt.Ticks.ToString(CultureInfo.InvariantCulture)),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    private static async Task<AppUser?> CurrentUserAsync(HttpContext http, AppDbContext db) =>
        int.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? await db.Users.FindAsync(id) : null;

    /// <summary>
    /// Ends a session whose account was removed (password reset) or whose password changed since it signed in.
    /// </summary>
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext ctx)
    {
        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var id = int.TryParse(ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : 0;
        var changedAt = await db.Users.Where(u => u.Id == id).Select(u => (DateTime?)u.PasswordChangedAt).FirstOrDefaultAsync();
        if (changedAt?.Ticks.ToString(CultureInfo.InvariantCulture) != ctx.Principal?.FindFirstValue(PasswordVersionClaim))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
