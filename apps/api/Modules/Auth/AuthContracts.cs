using System.ComponentModel.DataAnnotations;

namespace Yanyun.Api.Modules.Auth;

public sealed record EmailCodeRequest(
    [Required, EmailAddress, StringLength(254)] string Email);
public sealed record RegisterRequest(
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 8)] string Password,
    Guid ChallengeId,
    [Required, RegularExpression("^[0-9]{6}$")] string VerificationCode,
    [StringLength(32, MinimumLength = 1)] string? Nickname);
public sealed record LoginRequest(
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 1)] string Password);
public sealed record AuthResult(string AccessToken, string TokenType, int ExpiresIn);
public sealed record RegistrationResult(string AccessToken, string TokenType, int ExpiresIn, Guid UserId);
public sealed record CodeChallenge(Guid ChallengeId, int ExpiresIn, int ResendAfter);
public sealed record SessionResult(AuthResult Auth, string RefreshToken, DateTimeOffset ExpiresAt);

