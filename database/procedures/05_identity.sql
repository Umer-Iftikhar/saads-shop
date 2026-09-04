/*  Saad's Shop — identity, sessions and two-factor
    ========================================================================
    ASP.NET Core Identity's stores are implemented over these procedures
    instead of EF Core, so the "Dapper calls stored procedures only" rule
    holds for authentication too — the part of the system where a stray bit
    of inline SQL would matter most.

    Nothing here ever returns a usable credential. Refresh tokens and recovery
    codes are stored as SHA-256 hashes computed by the application; the
    database only ever compares hashes.
*/

SET NOCOUNT ON;
GO

/* ═════════════════════════════════════════════════════════════════════════
   Users
   ═════════════════════════════════════════════════════════════════════════ */

CREATE OR ALTER PROCEDURE dbo.usp_User_Get
    @UserId             NVARCHAR(128) = NULL,
    @NormalizedEmail    NVARCHAR(256) = NULL,
    @NormalizedUserName NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @FoundId NVARCHAR(128) = NULL;

    BEGIN TRY
        IF @UserId IS NULL AND @NormalizedEmail IS NULL AND @NormalizedUserName IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'A user id, email or username is required.';
        ELSE
            SELECT @FoundId = Id
            FROM   dbo.Users
            WHERE  (@UserId             IS NOT NULL AND Id = @UserId)
               OR  (@UserId IS NULL AND @NormalizedEmail    IS NOT NULL AND NormalizedEmail = @NormalizedEmail)
               OR  (@UserId IS NULL AND @NormalizedEmail IS NULL
                    AND @NormalizedUserName IS NOT NULL AND NormalizedUserName = @NormalizedUserName);
    END TRY
    BEGIN CATCH
        SET @FoundId = NULL;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not load the account.';
    END CATCH

    /*  1 — the user (empty when not found; "no such user" is not an error
        here, it is an ordinary answer the caller must handle without
        revealing which accounts exist)                                      */
    SELECT  Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
            PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, FullName,
            TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount, IsActive, CreatedAt
    FROM    dbo.Users
    WHERE   Id = @FoundId;

    /*  2 — their roles                                                      */
    SELECT  r.Name
    FROM    dbo.UserRoles AS ur
    JOIN    dbo.Roles AS r ON r.Id = ur.RoleId
    WHERE   ur.UserId = @FoundId;

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_Create
    @Id                 NVARCHAR(128),
    @UserName           NVARCHAR(256),
    @NormalizedUserName NVARCHAR(256),
    @Email              NVARCHAR(256),
    @NormalizedEmail    NVARCHAR(256),
    @EmailConfirmed     BIT           = 0,
    @PasswordHash       NVARCHAR(MAX) = NULL,
    @SecurityStamp      NVARCHAR(128) = NULL,
    @ConcurrencyStamp   NVARCHAR(64)  = NULL,
    @PhoneNumber        NVARCHAR(32)  = NULL,
    @FullName           NVARCHAR(128),
    @RoleName           NVARCHAR(64)  = N'Staff'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @RoleId NVARCHAR(128);

    BEGIN TRY
        IF NULLIF(LTRIM(RTRIM(@Id)), N'') IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'User id is required.';
        ELSE IF NULLIF(LTRIM(RTRIM(@Email)), N'') IS NULL OR @Email NOT LIKE N'%_@_%._%'
            SELECT @ResponseCode = 400, @ResponseMessage = N'A valid email address is required.';
        ELSE IF NULLIF(LTRIM(RTRIM(@FullName)), N'') IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'A name is required.';
        ELSE IF LEN(@FullName) > 128
            SELECT @ResponseCode = 400, @ResponseMessage = N'That name is too long.';
        ELSE IF EXISTS (SELECT 1 FROM dbo.Users WHERE NormalizedEmail = @NormalizedEmail)
            SELECT @ResponseCode = 409, @ResponseMessage = N'An account with that email already exists.';
        ELSE IF EXISTS (SELECT 1 FROM dbo.Users WHERE NormalizedUserName = @NormalizedUserName)
            SELECT @ResponseCode = 409, @ResponseMessage = N'That username is taken.';
        ELSE
        BEGIN
            SELECT @RoleId = Id FROM dbo.Roles WHERE NormalizedName = UPPER(@RoleName);
            IF @RoleId IS NULL
                SELECT @ResponseCode = 404, @ResponseMessage = N'That role does not exist.';
        END

        IF @ResponseCode = 200
        BEGIN
            BEGIN TRANSACTION;

                INSERT INTO dbo.Users (Id, UserName, NormalizedUserName, Email, NormalizedEmail,
                                       EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp,
                                       PhoneNumber, FullName, TwoFactorEnabled, LockoutEnabled)
                VALUES (@Id, @UserName, @NormalizedUserName, @Email, @NormalizedEmail,
                        @EmailConfirmed, @PasswordHash, @SecurityStamp, @ConcurrencyStamp,
                        @PhoneNumber, LTRIM(RTRIM(@FullName)), 0, 1);

                INSERT INTO dbo.UserRoles (UserId, RoleId) VALUES (@Id, @RoleId);

            COMMIT TRANSACTION;
            SET @ResponseMessage = N'Account created.';
        END
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not create the account.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_Update
    @Id                NVARCHAR(128),
    @UserName          NVARCHAR(256) = NULL,
    @NormalizedUserName NVARCHAR(256) = NULL,
    @Email             NVARCHAR(256) = NULL,
    @NormalizedEmail   NVARCHAR(256) = NULL,
    @EmailConfirmed    BIT           = NULL,
    @PasswordHash      NVARCHAR(MAX) = NULL,
    @SecurityStamp     NVARCHAR(128) = NULL,
    @ConcurrencyStamp  NVARCHAR(64)  = NULL,
    @PhoneNumber       NVARCHAR(32)  = NULL,
    @FullName          NVARCHAR(128) = NULL,
    @TwoFactorEnabled  BIT           = NULL,
    @LockoutEnd        DATETIMEOFFSET = NULL,
    @ClearLockout      BIT           = 0,
    @LockoutEnabled    BIT           = NULL,
    @AccessFailedCount INT           = NULL,
    @IsActive          BIT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        IF NULLIF(LTRIM(RTRIM(@Id)), N'') IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'User id is required.';
        ELSE IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @Id)
            SELECT @ResponseCode = 404, @ResponseMessage = N'That account no longer exists.';
        ELSE IF @Email IS NOT NULL AND @Email NOT LIKE N'%_@_%._%'
            SELECT @ResponseCode = 400, @ResponseMessage = N'A valid email address is required.';
        ELSE IF @NormalizedEmail IS NOT NULL
                AND EXISTS (SELECT 1 FROM dbo.Users WHERE NormalizedEmail = @NormalizedEmail AND Id <> @Id)
            SELECT @ResponseCode = 409, @ResponseMessage = N'Another account already uses that email.';

        IF @ResponseCode = 200
        BEGIN
            /*  COALESCE per column: Identity updates one facet at a time
                (a failed sign-in bumps only AccessFailedCount), so a NULL
                means "leave alone", not "set to null".                       */
            UPDATE dbo.Users
            SET    UserName           = COALESCE(@UserName, UserName),
                   NormalizedUserName = COALESCE(@NormalizedUserName, NormalizedUserName),
                   Email              = COALESCE(@Email, Email),
                   NormalizedEmail    = COALESCE(@NormalizedEmail, NormalizedEmail),
                   EmailConfirmed     = COALESCE(@EmailConfirmed, EmailConfirmed),
                   PasswordHash       = COALESCE(@PasswordHash, PasswordHash),
                   SecurityStamp      = COALESCE(@SecurityStamp, SecurityStamp),
                   ConcurrencyStamp   = COALESCE(@ConcurrencyStamp, ConcurrencyStamp),
                   PhoneNumber        = COALESCE(@PhoneNumber, PhoneNumber),
                   FullName           = COALESCE(@FullName, FullName),
                   TwoFactorEnabled   = COALESCE(@TwoFactorEnabled, TwoFactorEnabled),
                   LockoutEnd         = CASE WHEN @ClearLockout = 1 THEN NULL
                                             ELSE COALESCE(@LockoutEnd, LockoutEnd) END,
                   LockoutEnabled     = COALESCE(@LockoutEnabled, LockoutEnabled),
                   AccessFailedCount  = COALESCE(@AccessFailedCount, AccessFailedCount),
                   IsActive           = COALESCE(@IsActive, IsActive),
                   UpdatedAt          = SYSUTCDATETIME()
            WHERE  Id = @Id;

            SET @ResponseMessage = N'Account updated.';
        END
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not update the account.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/*  Staff list for the Owner's account management.                           */
CREATE OR ALTER PROCEDURE dbo.usp_Staff_GetList
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        SELECT  u.Id, u.FullName, u.Email, u.PhoneNumber, u.TwoFactorEnabled,
                u.IsActive, u.LockoutEnd, u.CreatedAt,
                STUFF((SELECT N', ' + r.Name
                       FROM dbo.UserRoles AS ur JOIN dbo.Roles AS r ON r.Id = ur.RoleId
                       WHERE ur.UserId = u.Id ORDER BY r.Name
                       FOR XML PATH(N''), TYPE).value(N'.', N'NVARCHAR(200)'), 1, 2, N'') AS Roles,
                (SELECT COUNT(*) FROM dbo.UserLogins AS l WHERE l.UserId = u.Id) AS ExternalLoginCount
        FROM    dbo.Users AS u
        ORDER BY u.FullName;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not load staff accounts.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/* ═════════════════════════════════════════════════════════════════════════
   Roles
   ═════════════════════════════════════════════════════════════════════════ */

CREATE OR ALTER PROCEDURE dbo.usp_Role_SetForUser
    @UserId   NVARCHAR(128),
    @RoleName NVARCHAR(64),
    @Attach   BIT           -- 1 = add, 0 = remove
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @RoleId NVARCHAR(128);

    BEGIN TRY
        SELECT @RoleId = Id FROM dbo.Roles WHERE NormalizedName = UPPER(@RoleName);

        IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @UserId)
            SELECT @ResponseCode = 404, @ResponseMessage = N'That account no longer exists.';
        ELSE IF @RoleId IS NULL
            SELECT @ResponseCode = 404, @ResponseMessage = N'That role does not exist.';
        /*  The shop must never end up with no Owner — nobody could then
            change settings or manage staff again.                           */
        ELSE IF @Attach = 0 AND UPPER(@RoleName) = N'OWNER'
                AND (SELECT COUNT(*) FROM dbo.UserRoles AS ur
                     JOIN dbo.Roles AS r ON r.Id = ur.RoleId
                     WHERE r.NormalizedName = N'OWNER') <= 1
            SELECT @ResponseCode = 409, @ResponseMessage = N'The shop must keep at least one owner.';

        IF @ResponseCode = 200
        BEGIN
            IF @Attach = 1
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE UserId = @UserId AND RoleId = @RoleId)
                    INSERT INTO dbo.UserRoles (UserId, RoleId) VALUES (@UserId, @RoleId);
                SET @ResponseMessage = N'Role granted.';
            END
            ELSE
            BEGIN
                DELETE FROM dbo.UserRoles WHERE UserId = @UserId AND RoleId = @RoleId;
                SET @ResponseMessage = N'Role removed.';
            END
        END
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not change the role.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/* ═════════════════════════════════════════════════════════════════════════
   External logins (Google)
   ═════════════════════════════════════════════════════════════════════════ */

CREATE OR ALTER PROCEDURE dbo.usp_UserLogin_Add
    @UserId              NVARCHAR(128),
    @LoginProvider       NVARCHAR(128),
    @ProviderKey         NVARCHAR(256),
    @ProviderDisplayName NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @UserId AND IsActive = 1)
            SELECT @ResponseCode = 404, @ResponseMessage = N'That account is not active.';
        /*  One Google identity, one staff account. Letting a provider key
            point at two accounts would make "who just signed in" ambiguous. */
        ELSE IF EXISTS (SELECT 1 FROM dbo.UserLogins
                        WHERE LoginProvider = @LoginProvider AND ProviderKey = @ProviderKey AND UserId <> @UserId)
            SELECT @ResponseCode = 409, @ResponseMessage = N'That Google account is already linked to another user.';
        ELSE IF NOT EXISTS (SELECT 1 FROM dbo.UserLogins
                            WHERE LoginProvider = @LoginProvider AND ProviderKey = @ProviderKey)
        BEGIN
            INSERT INTO dbo.UserLogins (LoginProvider, ProviderKey, ProviderDisplayName, UserId)
            VALUES (@LoginProvider, @ProviderKey, @ProviderDisplayName, @UserId);
            SET @ResponseMessage = N'Google account linked.';
        END
        ELSE
            SET @ResponseMessage = N'Already linked.';
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not link that account.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserLogin_Remove
    @UserId        NVARCHAR(128),
    @LoginProvider NVARCHAR(128),
    @ProviderKey   NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        DELETE FROM dbo.UserLogins
        WHERE  UserId = @UserId AND LoginProvider = @LoginProvider AND ProviderKey = @ProviderKey;

        SET @ResponseMessage = N'Google account unlinked.';
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not unlink that account.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/* ═════════════════════════════════════════════════════════════════════════
   Identity token store — holds the protected TOTP secret
   ═════════════════════════════════════════════════════════════════════════ */

CREATE OR ALTER PROCEDURE dbo.usp_UserToken_Set
    @UserId        NVARCHAR(128),
    @LoginProvider NVARCHAR(128),
    @Name          NVARCHAR(128),
    @Value         NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @UserId)
            SELECT @ResponseCode = 404, @ResponseMessage = N'That account no longer exists.';
        ELSE
        BEGIN
            MERGE dbo.UserTokens AS target
            USING (SELECT @UserId AS UserId, @LoginProvider AS LoginProvider, @Name AS Name) AS source
               ON target.UserId = source.UserId
              AND target.LoginProvider = source.LoginProvider
              AND target.Name = source.Name
            WHEN MATCHED THEN UPDATE SET Value = @Value
            WHEN NOT MATCHED THEN INSERT (UserId, LoginProvider, Name, Value)
                 VALUES (@UserId, @LoginProvider, @Name, @Value);
        END
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not save that token.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserToken_Get
    @UserId        NVARCHAR(128),
    @LoginProvider NVARCHAR(128),
    @Name          NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        SELECT Value FROM dbo.UserTokens
        WHERE  UserId = @UserId AND LoginProvider = @LoginProvider AND Name = @Name;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not read that token.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserToken_Remove
    @UserId        NVARCHAR(128),
    @LoginProvider NVARCHAR(128),
    @Name          NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        DELETE FROM dbo.UserTokens
        WHERE  UserId = @UserId AND LoginProvider = @LoginProvider AND Name = @Name;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not remove that token.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/* ═════════════════════════════════════════════════════════════════════════
   Refresh tokens — rotation with reuse detection

   The whole security property lives in usp_RefreshToken_Redeem. Read it
   alongside docs/security.md.
   ═════════════════════════════════════════════════════════════════════════ */

CREATE OR ALTER PROCEDURE dbo.usp_RefreshToken_Create
    @UserId      NVARCHAR(128),
    @TokenHash   BINARY(32),
    @FamilyId    UNIQUEIDENTIFIER,
    @ExpiresAt   DATETIME2(3),
    @CreatedByIp NVARCHAR(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @NewId BIGINT = NULL;

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @UserId AND IsActive = 1)
            SELECT @ResponseCode = 404, @ResponseMessage = N'That account is not active.';
        ELSE IF @TokenHash IS NULL OR @FamilyId IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'Token details are required.';
        ELSE IF @ExpiresAt IS NULL OR @ExpiresAt <= SYSUTCDATETIME()
            SELECT @ResponseCode = 400, @ResponseMessage = N'Token expiry must be in the future.';
        ELSE
        BEGIN
            INSERT INTO dbo.RefreshTokens (UserId, TokenHash, FamilyId, ExpiresAt, CreatedByIp)
            VALUES (@UserId, @TokenHash, @FamilyId, @ExpiresAt, @CreatedByIp);

            SET @NewId = SCOPE_IDENTITY();
        END
    END TRY
    BEGIN CATCH
        SET @NewId = NULL;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not start the session.';
    END CATCH

    SELECT @NewId AS RefreshTokenId;
    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/*  Redeem one refresh token and issue its replacement, atomically.

    The four outcomes:

      valid        → mark used, insert the replacement in the same family
      already used → REPLAY. Someone holds a copy of a spent token: the
                     lineage is compromised, so revoke the entire family and
                     force both the attacker and the real user to sign in again
      revoked      → the family was already burned; refuse
      expired /
      not found    → refuse

    All of it inside one transaction with the row locked, so two refreshes
    racing on the same token cannot both come out valid.                     */
CREATE OR ALTER PROCEDURE dbo.usp_RefreshToken_Redeem
    @PresentedHash  BINARY(32),
    @NewTokenHash   BINARY(32),
    @NewExpiresAt   DATETIME2(3),
    @CreatedByIp    NVARCHAR(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @TokenId BIGINT, @UserId NVARCHAR(128) = NULL, @FamilyId UNIQUEIDENTIFIER,
            @ExpiresAt DATETIME2(3), @UsedAt DATETIME2(3), @RevokedAt DATETIME2(3),
            @NewId BIGINT = NULL, @ReuseDetected BIT = 0;

    BEGIN TRY
        IF @PresentedHash IS NULL OR @NewTokenHash IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'Token details are required.';
        ELSE
        BEGIN
            BEGIN TRANSACTION;

                SELECT  @TokenId = RefreshTokenId, @UserId = UserId, @FamilyId = FamilyId,
                        @ExpiresAt = ExpiresAt, @UsedAt = UsedAt, @RevokedAt = RevokedAt
                FROM    dbo.RefreshTokens WITH (UPDLOCK, HOLDLOCK)
                WHERE   TokenHash = @PresentedHash;

                IF @TokenId IS NULL
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT @ResponseCode = 401, @ResponseMessage = N'Please sign in again.';
                END
                ELSE IF @UsedAt IS NOT NULL
                BEGIN
                    /*  Replay. Burn the whole lineage — the legitimate holder
                        is logged out too, and that is the point: they find out
                        immediately instead of sharing a session silently.     */
                    UPDATE dbo.RefreshTokens
                    SET    RevokedAt = SYSUTCDATETIME(),
                           RevokedReason = N'Reuse detected on family'
                    WHERE  FamilyId = @FamilyId AND RevokedAt IS NULL;

                    COMMIT TRANSACTION;

                    SET @ReuseDetected = 1;
                    SET @UserId = NULL;      -- nothing is issued on this path
                    SELECT @ResponseCode = 401, @ResponseMessage = N'Please sign in again.';
                END
                ELSE IF @RevokedAt IS NOT NULL
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT @ResponseCode = 401, @ResponseMessage = N'Please sign in again.';
                    SET @UserId = NULL;
                END
                ELSE IF @ExpiresAt <= SYSUTCDATETIME()
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT @ResponseCode = 401, @ResponseMessage = N'Your session has expired. Please sign in again.';
                    SET @UserId = NULL;
                END
                ELSE IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @UserId AND IsActive = 1)
                BEGIN
                    /*  Account disabled since the token was issued.          */
                    UPDATE dbo.RefreshTokens
                    SET    RevokedAt = SYSUTCDATETIME(), RevokedReason = N'Account disabled'
                    WHERE  FamilyId = @FamilyId AND RevokedAt IS NULL;

                    COMMIT TRANSACTION;
                    SELECT @ResponseCode = 401, @ResponseMessage = N'Please sign in again.';
                    SET @UserId = NULL;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.RefreshTokens (UserId, TokenHash, FamilyId, ExpiresAt, CreatedByIp)
                    VALUES (@UserId, @NewTokenHash, @FamilyId, @NewExpiresAt, @CreatedByIp);

                    SET @NewId = SCOPE_IDENTITY();

                    UPDATE dbo.RefreshTokens
                    SET    UsedAt = SYSUTCDATETIME(), ReplacedByTokenId = @NewId
                    WHERE  RefreshTokenId = @TokenId;

                    COMMIT TRANSACTION;
                    SET @ResponseMessage = N'OK';
                END
        END
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        SET @UserId = NULL; SET @NewId = NULL;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not refresh the session.';
    END CATCH

    /*  1 — who the new token belongs to (empty on every failure path), plus
        the reuse flag so the API can log the security event distinctly.     */
    SELECT  u.Id AS UserId, u.Email, u.FullName, @NewId AS RefreshTokenId, @ReuseDetected AS ReuseDetected
    FROM    dbo.Users AS u WHERE u.Id = @UserId;

    /*  2 — roles for the new access token                                   */
    SELECT  r.Name
    FROM    dbo.UserRoles AS ur JOIN dbo.Roles AS r ON r.Id = ur.RoleId
    WHERE   ur.UserId = @UserId;

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_RefreshToken_Revoke
    @TokenHash BINARY(32)       = NULL,
    @UserId    NVARCHAR(128)    = NULL,
    @FamilyId  UNIQUEIDENTIFIER = NULL,
    @Reason    NVARCHAR(128)    = N'Signed out'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @Affected INT = 0;

    BEGIN TRY
        IF @TokenHash IS NULL AND @UserId IS NULL AND @FamilyId IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'Nothing identified to revoke.';
        ELSE
        BEGIN
            /*  Revoking by token revokes its whole family: signing out on one
                device should not leave a rotated sibling alive.              */
            IF @TokenHash IS NOT NULL AND @FamilyId IS NULL
                SELECT @FamilyId = FamilyId FROM dbo.RefreshTokens WHERE TokenHash = @TokenHash;

            UPDATE dbo.RefreshTokens
            SET    RevokedAt = SYSUTCDATETIME(), RevokedReason = @Reason
            WHERE  RevokedAt IS NULL
              AND  ((@FamilyId IS NOT NULL AND FamilyId = @FamilyId)
                 OR (@FamilyId IS NULL AND @UserId IS NOT NULL AND UserId = @UserId));

            SET @Affected = @@ROWCOUNT;
        END
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not sign out.';
    END CATCH

    SELECT @Affected AS RevokedCount;
    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/*  Housekeeping: spent and expired tokens are evidence for a while, then
    they are just rows. Called by a nightly job.                             */
CREATE OR ALTER PROCEDURE dbo.usp_RefreshToken_Purge
    @OlderThanDays INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @Deleted INT = 0;

    BEGIN TRY
        IF @OlderThanDays IS NULL OR @OlderThanDays < 1 OR @OlderThanDays > 3650
            SELECT @ResponseCode = 400, @ResponseMessage = N'Retention must be between 1 and 3650 days.';
        ELSE
        BEGIN
            DELETE FROM dbo.RefreshTokens
            WHERE  ExpiresAt < DATEADD(DAY, -@OlderThanDays, SYSUTCDATETIME());
            SET @Deleted = @@ROWCOUNT;
        END
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not purge tokens.';
    END CATCH

    SELECT @Deleted AS DeletedCount;
    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/* ═════════════════════════════════════════════════════════════════════════
   2FA recovery codes
   ═════════════════════════════════════════════════════════════════════════ */

/*  Codes are added one at a time inside an application-side transaction, the
    first call passing @ClearExisting = 1. Generating a new set must invalidate
    the old one — otherwise an old printout still opens the door.

    One at a time rather than a table-valued parameter: that would mean a
    second BINARY(32) UDT to version, and ten inserts during an enrolment that
    happens once per staff member is not a hot path.                         */
CREATE OR ALTER PROCEDURE dbo.usp_RecoveryCode_Add
    @UserId   NVARCHAR(128),
    @CodeHash BINARY(32),
    @ClearExisting BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @UserId)
            SELECT @ResponseCode = 404, @ResponseMessage = N'That account no longer exists.';
        ELSE IF @CodeHash IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'A code is required.';
        ELSE
        BEGIN
            IF @ClearExisting = 1
                DELETE FROM dbo.RecoveryCodes WHERE UserId = @UserId;

            INSERT INTO dbo.RecoveryCodes (UserId, CodeHash) VALUES (@UserId, @CodeHash);
        END
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not save the recovery codes.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/*  Single use, enforced under a lock so the same code cannot be spent twice
    by two simultaneous attempts.                                            */
CREATE OR ALTER PROCEDURE dbo.usp_RecoveryCode_Redeem
    @UserId   NVARCHAR(128),
    @CodeHash BINARY(32)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @CodeId BIGINT = NULL, @Remaining INT = 0;

    BEGIN TRY
        BEGIN TRANSACTION;

            SELECT @CodeId = RecoveryCodeId
            FROM   dbo.RecoveryCodes WITH (UPDLOCK, HOLDLOCK)
            WHERE  UserId = @UserId AND CodeHash = @CodeHash AND UsedAt IS NULL;

            IF @CodeId IS NULL
            BEGIN
                ROLLBACK TRANSACTION;
                SELECT @ResponseCode = 401, @ResponseMessage = N'That code is not valid.';
            END
            ELSE
            BEGIN
                UPDATE dbo.RecoveryCodes SET UsedAt = SYSUTCDATETIME() WHERE RecoveryCodeId = @CodeId;

                SELECT @Remaining = COUNT(*) FROM dbo.RecoveryCodes
                WHERE  UserId = @UserId AND UsedAt IS NULL;

                COMMIT TRANSACTION;
                SET @ResponseMessage = N'OK';
            END
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not check that code.';
    END CATCH

    SELECT @Remaining AS RemainingCodes;
    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO
