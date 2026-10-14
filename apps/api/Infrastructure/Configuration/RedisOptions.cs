using System.ComponentModel.DataAnnotations;

namespace Yanyun.Api.Infrastructure.Configuration;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    [Required]
    public string KeyPrefix { get; set; } = "";

    [Range(100, 30000)]
    public int ConnectTimeoutMilliseconds { get; set; } = 2000;

    [Range(100, 30000)]
    public int OperationTimeoutMilliseconds { get; set; } = 2000;
}
