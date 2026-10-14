namespace Yanyun.Api.Common;

public sealed class BusinessException(int status, string code, string message, object? details = null)
    : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public object? Details { get; } = details;
}

public sealed record RetryDelay(long RetryAfter);
