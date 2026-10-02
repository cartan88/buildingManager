namespace BuildingManager.Core.Entities;

/// <summary>
/// Someone who can sign in. The app has a single owner account for now; the table allows more later.
/// The password is stored only as a salted hash (ASP.NET Core Identity's PasswordHasher), never as text.
/// </summary>
public class AppUser
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    /// <summary>Where "Forgot password?" sends a reset code. Null means email reset isn't available for this account.</summary>
    public string? Email { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Changes whenever the password does, which signs out sessions started with the old one.</summary>
    public DateTime PasswordChangedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A one-time code emailed by "Forgot password?". Only a SHA-256 hash of the code is stored. It works once, until
/// it expires, and stops working after a few wrong tries.
/// </summary>
public class PasswordResetCode
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public required string CodeHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public int FailedAttempts { get; set; }
}

public enum EmailSecurity { StartTls = 0, SslOnConnect = 1 }

/// <summary>The email account the app sends password reset codes from (e.g. Gmail with an app password).</summary>
public class EmailSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public EmailSecurity Security { get; set; } = EmailSecurity.StartTls;
    public string? Username { get; set; }
    /// <summary>Encrypted with ASP.NET Core Data Protection, never stored as plain text.</summary>
    public string? ProtectedPassword { get; set; }
    public string FromAddress { get; set; } = "";
    public string? FromName { get; set; }
}
