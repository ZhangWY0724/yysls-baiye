namespace Yanyun.Api.Common;

public sealed record ApiResponse<T>(T? Data, ApiError? Error);

public sealed record ApiError(string Code, string Message, object? Details, string TraceId);

public static class ApiResponse
{
    public static ApiResponse<T> Success<T>(T data) => new(data, null);

    public static ApiResponse<object> Failure(
        HttpContext context, string code, string message, object? details = null)
        => new(null, new ApiError(code, message, details, context.TraceIdentifier));
}
