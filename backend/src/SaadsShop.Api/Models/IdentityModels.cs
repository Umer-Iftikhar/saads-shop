namespace SaadsShop.Api.Models;

/// <summary>
/// A shop account. Shaped like ASP.NET Identity's user because Identity's
/// managers operate on it, but it is a plain POCO reached through Dapper and
/// stored procedures rather than EF Core.
/// </summary>
public class AppUser
{
    public string  Id                 { get; set; } = string.Empty;
    public string  UserName           { get; set; } = string.Empty;
    public string  NormalizedUserName { get; set; } = string.Empty;
    public string  Email              { get; set; } = string.Empty;
    public string  NormalizedEmail    { get; set; } = string.Empty;
    public bool    EmailConfirmed     { get; set; }

    /// <summary>PBKDF2-HMAC-SHA512 via Identity's hasher. Never logged, never returned.</summary>
    public string? PasswordHash       { get; set; }

    public string? SecurityStamp      { get; set; }
    public string? ConcurrencyStamp   { get; set; }
    public string? PhoneNumber        { get; set; }
    public string  FullName           { get; set; } = string.Empty;
    public bool    TwoFactorEnabled   { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public bool    LockoutEnabled     { get; set; }
    public int     AccessFailedCount  { get; set; }

    /// <summary>
    /// Distinct from lockout: lockout is temporary and automatic, this is an
    /// owner deciding an account should stop working. A disabled account's
    /// refresh tokens are revoked the next time they are used.
    /// </summary>
    public bool     IsActive          { get; set; } = true;

    public DateTime CreatedAt         { get; set; }

    public IReadOnlyList<string> Roles { get; set; } = [];
}

public class AppRole
{
    public string Id             { get; set; } = string.Empty;
    public string Name           { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
}

/// <summary>A staff row for the Owner's account management screen.</summary>
public class StaffAccount
{
    public string    Id                 { get; set; } = string.Empty;
    public string    FullName           { get; set; } = string.Empty;
    public string    Email              { get; set; } = string.Empty;
    public string?   PhoneNumber        { get; set; }
    public bool      TwoFactorEnabled   { get; set; }
    public bool      IsActive           { get; set; }
    public DateTimeOffset? LockoutEnd   { get; set; }
    public DateTime  CreatedAt          { get; set; }
    public string?   Roles              { get; set; }
    public int       ExternalLoginCount { get; set; }
}

/// <summary>
/// The row the refresh-token table holds. The token itself is never stored —
/// only its SHA-256 hash — so a database leak yields nothing usable.
/// </summary>
public class RefreshTokenRecord
{
    public long      RefreshTokenId    { get; set; }
    public string    UserId            { get; set; } = string.Empty;

    /// <summary>
    /// Chains a lineage of rotated tokens. Redeeming a spent member means the
    /// lineage leaked, and the whole family is revoked at once.
    /// </summary>
    public Guid      FamilyId          { get; set; }

    public DateTime  CreatedAt         { get; set; }
    public DateTime  ExpiresAt         { get; set; }
    public DateTime? UsedAt            { get; set; }
    public DateTime? RevokedAt         { get; set; }
    public string?   RevokedReason     { get; set; }
    public long?     ReplacedByTokenId { get; set; }
}
