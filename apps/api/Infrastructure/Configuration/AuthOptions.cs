using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Yanyun.Api.Infrastructure.Configuration;

public sealed class AuthOptions : IValidatableObject
{
    public const string SectionName = "Auth";

    [Required]
    public string Issuer { get; set; } = "";

    [Required]
    public string Audience { get; set; } = "";

    [Required]
    public string SigningKey { get; set; } = "";

    [Range(1, 60)]
    public int AccessTokenMinutes { get; set; } = 15;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Encoding.UTF8.GetByteCount(SigningKey) < 32 ||
            SigningKey.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult("Auth:SigningKey 必须配置至少 32 字节的随机密钥。",
                [nameof(SigningKey)]);
        }
    }
}
