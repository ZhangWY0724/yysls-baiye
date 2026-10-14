using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Yanyun.Api.Infrastructure.Authentication;

namespace Yanyun.Api.Tests;

public sealed class FrameworkTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public FrameworkTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task LivenessDoesNotDependOnStorage()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("healthy", json.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("error").ValueKind);
    }

    [Fact]
    public async Task ReadinessReturns503AndDoesNotExposeConnectionDetails()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("private-connection-secret", body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("unhealthy", json.RootElement.GetProperty("data").GetProperty("checks")
            .GetProperty("redis").GetString());
    }

    [Fact]
    public async Task BusinessEndpointsRequireAuthenticationByDefault()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/test-probe/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertError(response, "AUTHENTICATION_REQUIRED");
    }

    [Fact]
    public async Task VerifiedActiveAccountCanAccessBusinessEndpoint()
    {
        var userId = Guid.NewGuid();
        _factory.Accounts[userId] = new(true, true);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(userId));
        var response = await client.GetAsync("/api/v1/test-probe/protected");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("has_access", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task JwtClaimCannotBypassCurrentEmailBindingState()
    {
        var userId = Guid.NewGuid();
        _factory.Accounts[userId] = new(true, false);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(userId));
        var response = await client.GetAsync("/api/v1/test-probe/protected");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertError(response, "EMAIL_BINDING_REQUIRED");
    }

    [Fact]
    public async Task DeletedAccountCannotContinueUsingSignedJwt()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(Guid.NewGuid()));
        var response = await client.GetAsync("/api/v1/test-probe/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertError(response, "ACCOUNT_NOT_FOUND");
    }

    [Fact]
    public async Task DisabledAccountIsForbidden()
    {
        var userId = Guid.NewGuid();
        _factory.Accounts[userId] = new(false, true);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(userId));
        var response = await client.GetAsync("/api/v1/test-probe/protected");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertError(response, "ACCOUNT_DISABLED");
    }

    [Fact]
    public async Task AuthenticationStorageFailureIs503RatherThanInvalidLogin()
    {
        var userId = Guid.NewGuid();
        _factory.AccountFailures[userId] = new NpgsqlException("private-auth-storage-secret");
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(userId));
        var response = await client.GetAsync("/api/v1/test-probe/protected");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertError(response, "AUTH_STATE_UNAVAILABLE");
        Assert.DoesNotContain("private-auth-storage-secret", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExpiredAndIncorrectlySignedTokensAreRejected()
    {
        var userId = Guid.NewGuid();
        _factory.Accounts[userId] = new(true, true);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(userId, expired: true));
        var expiredResponse = await client.GetAsync("/api/v1/test-probe/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, expiredResponse.StatusCode);
        await AssertError(expiredResponse, "TOKEN_EXPIRED");
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            CreateToken(userId, signingKey: "another-test-signing-key-123456789012345678901234567890"));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/v1/test-probe/protected")).StatusCode);
    }

    [Fact]
    public async Task QueryTokenIsAcceptedOnlyForNotificationHub()
    {
        var userId = Guid.NewGuid();
        _factory.Accounts[userId] = new(true, true);
        using var client = _factory.CreateClient();
        var token = Uri.EscapeDataString(CreateToken(userId));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/v1/test-probe/protected?access_token={token}")).StatusCode);
        var hubResponse = await client.PostAsync(
            $"/hubs/notifications/negotiate?negotiateVersion=1&access_token={token}", null);
        Assert.Equal(HttpStatusCode.OK, hubResponse.StatusCode);
    }

    [Fact]
    public async Task HubRejectsAccountThatStillNeedsEmailBinding()
    {
        var userId = Guid.NewGuid();
        _factory.Accounts[userId] = new(true, false);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(userId));
        var response = await client.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertError(response, "EMAIL_BINDING_REQUIRED");
    }

    [Fact]
    public async Task ErrorsAndMalformedJsonUseContractWithoutLeakingSecrets()
    {
        using var client = _factory.CreateClient();
        var exceptionResponse = await client.GetAsync("/api/v1/test-probe/exception");
        Assert.Equal(HttpStatusCode.InternalServerError, exceptionResponse.StatusCode);
        await AssertError(exceptionResponse, "INTERNAL_ERROR");
        Assert.DoesNotContain("private-exception-secret", await exceptionResponse.Content.ReadAsStringAsync());
        var malformedResponse = await client.PostAsync("/api/v1/test-probe/json",
            new StringContent("{broken-json", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        await AssertError(malformedResponse, "VALIDATION_FAILED");
        var unknownFieldResponse = await client.PostAsync("/api/v1/test-probe/json",
            new StringContent("{\"value\":1,\"user_id\":\"forged\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, unknownFieldResponse.StatusCode);
        await AssertError(unknownFieldResponse, "VALIDATION_FAILED");
        var missingResponse = await client.GetAsync("/api/v1/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        await AssertError(missingResponse, "RESOURCE_NOT_FOUND");
    }

    [Fact]
    public async Task DevelopmentOpenApiDescribesSnakeCaseAndBearerSecurity()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/v1/dictionaries/profile-options", body);
        Assert.Contains("dictionary_version", body);
        Assert.Contains("Bearer", body);
        Assert.Contains("/api/v1/auth/login", body);
        using var document = JsonDocument.Parse(body);
        var paths = document.RootElement.GetProperty("paths");
        Assert.Equal(60, paths.EnumerateObject().Where(path => path.Name.StartsWith("/api/v1/", StringComparison.Ordinal) &&
            !path.Name.Contains("test-probe", StringComparison.Ordinal)).Sum(path => path.Value.EnumerateObject().Count(method =>
                method.Name is "get" or "post" or "patch" or "put" or "delete")));
        Assert.Equal("#/components/schemas/ApiResponseOfEventResponse", paths.GetProperty("/api/v1/guilds/{guildId}/events/{eventId}")
            .GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Contains(paths.GetProperty("/api/v1/events/{eventId}/assignments").GetProperty("post").GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "Idempotency-Key" && parameter.GetProperty("required").GetBoolean());
    }

    [Fact]
    public async Task CsrfEndpointReturnsRequestTokenAndScopedHttpOnlyCookie()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("request_token", await response.Content.ReadAsStringAsync());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductionDoesNotPublishOpenApi()
    {
        await using var production = new ApiFactory("Production");
        using var client = production.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }

    [Fact]
    public void ProductionRequiresExplicitSigningKey()
    {
        using var production = new ApiFactory("Production", signingKey: "");
        Assert.Throws<OptionsValidationException>(() => production.CreateClient());
    }

    private static async Task AssertError(HttpResponseMessage response, string code)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("data").ValueKind);
        var error = json.RootElement.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(error.GetProperty("trace_id").GetString()));
    }

    private static string CreateToken(Guid userId, bool expired = false, string? signingKey = null)
    {
        var token = new JwtSecurityToken("test-issuer", "test-audience",
            [new Claim("sub", userId.ToString()), new Claim("email_verified", "true")],
            notBefore: DateTime.UtcNow.AddMinutes(-5),
            expires: expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? ApiFactory.TestSigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
