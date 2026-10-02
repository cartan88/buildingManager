using System.Globalization;
using System.Security.Claims;
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

/// <summary>
/// Sign-in with a single owner account. The first visit creates it; after that every /api call needs the
/// sign-in cookie, except these endpoints and the business name and logo shown on the sign-in page.
/// </summary>
public static class AuthEndpoints
{
    public const int MinPasswordLength = 8;
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

        g.MapGet("/status", async (HttpContext http, AppDbContext db) => new
        {
            SetupRequired = !await db.Users.AnyAsync(),
            SignedIn = http.User.Identity?.IsAuthenticated == true,
            Username = http.User.Identity?.Name,
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
                user = new AppUser { Username = username, PasswordHash = "" };
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
