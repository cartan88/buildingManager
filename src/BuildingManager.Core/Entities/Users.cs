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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Changes whenever the password does, which signs out sessions started with the old one.</summary>
    public DateTime PasswordChangedAt { get; set; } = DateTime.UtcNow;
}
