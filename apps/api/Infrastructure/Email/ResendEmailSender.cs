using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Yanyun.Api.Common;

namespace Yanyun.Api.Infrastructure.Email;

public sealed class EmailOptions
{
    public string ResendApiKey { get; set; } = "";
    public string From { get; set; } = "";
}

public interface IVerificationEmailSender
{
    Task SendRegistrationAsync(string recipient, string code, Guid challengeId, CancellationToken cancellationToken);
}

public sealed class ResendEmailSender(HttpClient client, IOptions<EmailOptions> options)
    : IVerificationEmailSender
{
    public async Task SendRegistrationAsync(string recipient, string code, Guid challengeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ResendApiKey) || string.IsNullOrWhiteSpace(options.Value.From))
            throw Unavailable();

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ResendApiKey);
        request.Headers.Add("Idempotency-Key", $"registration/{challengeId:D}");
        request.Content = JsonContent.Create(new
        {
            from = options.Value.From,
            to = new[] { recipient },
            subject = "百业平台 · 邮箱注册验证码",
            text = $"你的注册验证码是：{code}，10 分钟内有效。如非本人操作，请忽略此邮件。"
        });
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw Unavailable();
            using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            if (payload.RootElement.ValueKind != JsonValueKind.Object || !payload.RootElement.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))
                throw Unavailable();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException ||
            exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw Unavailable();
        }
    }

    private static BusinessException Unavailable() =>
        new(503, "VERIFICATION_EMAIL_UNAVAILABLE", "验证邮件暂时无法发送，请稍后重试");
}
