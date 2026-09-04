using System.ComponentModel.DataAnnotations;

namespace SaadsShop.Api.DTOs.Request;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "That does not look like an email address.")]
    [StringLength(256)]
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// No [StringLength] minimum here on purpose. Sign-in must not tell an
    /// attacker anything about the password policy, and a short guess should
    /// fail the same way a long wrong one does.
    /// </summary>
    [Required(ErrorMessage = "Password is required.")]
    public string Password { get; init; } = string.Empty;
}

public sealed class TwoFactorRequest
{
    /// <summary>
    /// The short-lived token issued by /auth/login. It proves the password step
    /// passed and nothing more — it cannot be used against any other endpoint.
    /// </summary>
    [Required] public string MfaToken { get; init; } = string.Empty;

    /// <summary>A six-digit TOTP code, or one of the recovery codes.</summary>
    [Required(ErrorMessage = "Enter the code from your authenticator app.")]
    [StringLength(32, MinimumLength = 6, ErrorMessage = "That code does not look right.")]
    public string Code { get; init; } = string.Empty;

    /// <summary>Set when the code is a recovery code rather than a TOTP.</summary>
    public bool IsRecoveryCode { get; init; }
}

public sealed class ConfirmTwoFactorRequest
{
    [Required(ErrorMessage = "Enter the six-digit code to confirm.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "The code is six digits.")]
    public string Code { get; init; } = string.Empty;
}

public sealed class CreateStaffRequest
{
    [Required(ErrorMessage = "A name is required.")]
    [StringLength(128, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Length is the rule that matters most; the rest is enforced by Identity's
    /// password options so there is a single source of truth.
    /// </summary>
    [Required(ErrorMessage = "A password is required.")]
    [StringLength(128, MinimumLength = 12,
        ErrorMessage = "The password must be at least 12 characters.")]
    public string Password { get; init; } = string.Empty;

    [RegularExpression(@"^(\+92|92|0)?[\s-]?3\d{2}[\s-]?\d{3}[\s-]?\d{4}$",
        ErrorMessage = "That phone number does not look right.")]
    public string? PhoneNumber { get; init; }

    [RegularExpression("^(Owner|Staff)$", ErrorMessage = "Role must be Owner or Staff.")]
    public string Role { get; init; } = "Staff";
}

public sealed class SetRoleRequest
{
    [Required] public string UserId { get; init; } = string.Empty;

    [Required]
    [RegularExpression("^(Owner|Staff)$", ErrorMessage = "Role must be Owner or Staff.")]
    public string Role { get; init; } = string.Empty;

    public bool Attach { get; init; } = true;
}
