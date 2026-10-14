namespace Yanyun.Api.Modules.Users;

public sealed class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Nickname { get; set; } = "";
    public string? Email { get; set; }
    public string? NormalizedEmail { get; set; }
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public string? PasswordHash { get; set; }
    public string Status { get; set; } = "active";
    public Guid? DefaultProfileId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
