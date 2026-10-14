using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Yanyun.Api.Common;
using Yanyun.Api.Infrastructure.Email;

namespace Yanyun.Api.Tests;

public sealed class ResendTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
    private static ResendEmailSender Sender(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.com/") },
        Options.Create(new EmailOptions { From = "百业平台 <noreply@example.test>", ResendApiKey = "test-key" }));

    [Fact]
    public async Task RegistrationUsesDocumentedPayloadAndStableProviderIdempotency()
    {
        var challenge = Guid.NewGuid();
        var sender = Sender(new Handler(async request =>
        {
            Assert.Equal("https://api.resend.com/emails", request.RequestUri!.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal($"registration/{challenge:D}", Assert.Single(request.Headers.GetValues("Idempotency-Key")));
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("recipient@example.test", json.RootElement.GetProperty("to")[0].GetString());
            Assert.Contains("123456", json.RootElement.GetProperty("text").GetString());
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"accepted-id\"}") };
        }));
        await sender.SendRegistrationAsync("recipient@example.test", "123456", challenge, CancellationToken.None);
    }

    [Theory]
    [InlineData(429, "{\"message\":\"sensitive provider details\"}")]
    [InlineData(200, "{}")]
    [InlineData(200, "{\"id\":1}")]
    [InlineData(200, "invalid-json")]
    public async Task RejectedOrMalformedResponsesNeverReportSuccess(int status, string body)
    {
        var sender = Sender(new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) })));
        var error = await Assert.ThrowsAsync<BusinessException>(() => sender.SendRegistrationAsync("recipient@example.test", "123456", Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(503, error.Status); Assert.Equal("VERIFICATION_EMAIL_UNAVAILABLE", error.Code);
        Assert.DoesNotContain("sensitive", error.Message);
    }
}
