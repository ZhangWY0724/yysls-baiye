using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Yanyun.Api.Common;
using Yanyun.Api.Infrastructure.Configuration;

namespace Yanyun.Api.Infrastructure.Authentication;

public static class AuthenticationRegistration
{
    public static IServiceCollection AddPlatformAuthentication(this IServiceCollection services)
    {
        services.AddScoped<IUserAccountStateReader, UserAccountStateReader>();
        services.AddSingleton<IAuthorizationHandler, VerifiedEmailHandler>();
        services.AddAuthorization(options =>
        {
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().AddRequirements(new VerifiedEmailRequirement()).Build();
            options.AddPolicy(VerifiedEmailRequirement.PolicyName, policy);
            options.FallbackPolicy = policy;
        });
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>>((options, authOptions) =>
            {
                var auth = authOptions.Value;
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = auth.Issuer,
                    ValidateAudience = true,
                    ValidAudience = auth.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(auth.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = "sub"
                };
                options.Events = CreateEvents();
            });
        return services;
    }

    private static JwtBearerEvents CreateEvents() => new()
    {
        OnMessageReceived = context =>
        {
            if (context.Request.Path.StartsWithSegments("/hubs/notifications") &&
                !context.Request.Headers.ContainsKey("Authorization"))
            {
                context.Token = context.Request.Query["access_token"];
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var userId))
            {
                context.Fail("无效账号标识");
                return;
            }
            var reader = context.HttpContext.RequestServices.GetRequiredService<IUserAccountStateReader>();
            var state = await reader.FindAsync(userId, context.HttpContext.RequestAborted);
            if (state is null || !state.IsActive)
            {
                context.HttpContext.Items[VerifiedEmailRequirement.ErrorItemKey] =
                    state is null ? "ACCOUNT_NOT_FOUND" : "ACCOUNT_DISABLED";
                context.Fail("账号不可用");
                return;
            }
            context.HttpContext.Items[VerifiedEmailRequirement.StateItemKey] = state;
        },
        OnAuthenticationFailed = context =>
        {
            if (context.Exception is NpgsqlException)
            {
                context.HttpContext.Items[VerifiedEmailRequirement.ErrorItemKey] = "AUTH_STATE_UNAVAILABLE";
                context.Fail("认证状态服务暂不可用");
            }
            else if (context.Exception is SecurityTokenExpiredException)
            {
                context.HttpContext.Items[VerifiedEmailRequirement.ErrorItemKey] = "TOKEN_EXPIRED";
            }
            return Task.CompletedTask;
        },
        OnChallenge = async context =>
        {
            context.HandleResponse();
            var code = context.HttpContext.Items[VerifiedEmailRequirement.ErrorItemKey] as string
                ?? "AUTHENTICATION_REQUIRED";
            context.Response.StatusCode = code switch
            {
                "AUTH_STATE_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
                "ACCOUNT_DISABLED" => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status401Unauthorized
            };
            if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
            {
                context.Response.Headers.WWWAuthenticate = "Bearer";
            }
            await context.Response.WriteAsJsonAsync(
                ApiResponse.Failure(context.HttpContext, code,
                    code switch
                    {
                        "AUTH_STATE_UNAVAILABLE" => "认证状态服务暂不可用，请稍后重试",
                        "ACCOUNT_DISABLED" => "账号不可用",
                        _ => "请登录后继续"
                    }),
                context.HttpContext.RequestAborted);
        },
        OnForbidden = async context =>
        {
            var needsBinding = context.HttpContext.Items[VerifiedEmailRequirement.StateItemKey]
                is UserAccountState { HasVerifiedEmail: false };
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(ApiResponse.Failure(context.HttpContext,
                needsBinding ? "EMAIL_BINDING_REQUIRED" : "PERMISSION_DENIED",
                needsBinding ? "请先完成邮箱绑定" : "你没有此操作的权限"),
                context.HttpContext.RequestAborted);
        }
    };
}
