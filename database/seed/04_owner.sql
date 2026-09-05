/*  Saad's Shop — the first Owner account
    ------------------------------------------------------------------------
    Staff accounts are created by an Owner through POST /api/auth/staff, which
    leaves exactly one account nobody can create that way: the first. This is
    it, and it exists only to break that circle.

    ┌───────────────────────────────────────────────────────────────────────┐
    │  THE PASSWORD IN THIS FILE IS PUBLIC. It is in the repository, so     │
    │  treat it as known to everyone.                                       │
    │                                                                       │
    │      saad@saadsshop.pk / ChangeMe!Saad2026                            │
    │                                                                       │
    │  Sign in with it once, enrol an authenticator, and change it. Until    │
    │  the second factor is enrolled the account cannot reach the panel at   │
    │  all — /auth/login hands back a challenge token and nothing else — so  │
    │  a known password alone opens nothing. That is the only reason         │
    │  shipping one here is tolerable.                                       │
    │                                                                       │
    │  On a real deployment: run this, sign in, enrol, change the password,  │
    │  and create the actual staff accounts from the panel.                  │
    └───────────────────────────────────────────────────────────────────────┘

    Idempotent, like the other seeds: it inserts only when the account is
    absent, and never touches an existing row. Re-running it cannot reset a
    password that has since been changed, nor restore a role an Owner removed.
*/

SET NOCOUNT ON;
GO

DECLARE @UserId NVARCHAR(128) = N'00000000000000000000000000000001';
DECLARE @Email  NVARCHAR(256) = N'saad@saadsshop.pk';

/*  PBKDF2-HMAC-SHA512, the format ASP.NET Identity's PasswordHasher writes and
    reads (version marker 0x01 followed by the iteration count and salt). It is
    a real hash of the password above, produced by that same hasher — not a
    placeholder, so the account works the moment this runs.                    */
DECLARE @PasswordHash NVARCHAR(MAX) =
    N'AQAAAAIAAYagAAAAEBpp8CoW3f5Sg1B+yH1sUm/e9lzhlaRQsfPmg8J2V+9v/DDFhWZbzIWXR+m1t/pfLw==';

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE NormalizedEmail = UPPER(@Email))
BEGIN
    INSERT INTO dbo.Users
        (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
         PasswordHash, SecurityStamp, ConcurrencyStamp, FullName,
         /*  Off, and it must stay off until an authenticator is actually
             enrolled: the flag says "this account has a second factor", and
             setting it here with no shared key would lock the account out of
             its own panel.                                                   */
         TwoFactorEnabled, LockoutEnabled, IsActive)
    VALUES
        (@UserId, @Email, UPPER(@Email), @Email, UPPER(@Email), 1,
         @PasswordHash, CONVERT(NVARCHAR(128), NEWID()), CONVERT(NVARCHAR(64), NEWID()),
         N'Saad', 0, 1, 1);

    PRINT 'Seeded the first Owner: saad@saadsshop.pk / ChangeMe!Saad2026 — change it after enrolling 2FA.';
END
ELSE
    PRINT 'Owner account already present; left untouched.';
GO

/*  The role grant is separate and separately guarded, so a database where the
    user exists but the grant was lost still repairs itself.                   */
INSERT INTO dbo.UserRoles (UserId, RoleId)
SELECT u.Id, r.Id
FROM   dbo.Users AS u
CROSS  JOIN dbo.Roles AS r
WHERE  u.NormalizedEmail = N'SAAD@SAADSSHOP.PK'
AND    r.NormalizedName  = N'OWNER'
AND    NOT EXISTS (
           SELECT 1 FROM dbo.UserRoles AS ur
           WHERE  ur.UserId = u.Id AND ur.RoleId = r.Id);
GO
