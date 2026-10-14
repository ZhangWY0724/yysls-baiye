using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog;
using Yanyun.Api.Common;
using Yanyun.Api.Infrastructure.Authentication;
using Yanyun.Api.Infrastructure.Configuration;
using Yanyun.Api.Infrastructure.Health;
using Yanyun.Api.Infrastructure.OpenApi;
using Yanyun.Api.Infrastructure.Persistence;
using Yanyun.Api.Infrastructure.Redis;
using Yanyun.Api.Modules.Notifications;
using Yanyun.Api.Modules.Auth;
using Yanyun.Api.Infrastructure.Email;
using Yanyun.Api.Modules.Business;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddCommandLine(args);
}

// 开发环境只生成本次进程的密钥，不写入仓库或输出日志；生产必须显式配置。
var usesEphemeralSigningKey = builder.Environment.IsDevelopment() &&
    string.IsNullOrWhiteSpace(builder.Configuration["Auth:SigningKey"]);
if (usesEphemeralSigningKey)
{
    builder.Configuration["Auth:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}

builder.Services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionName)
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<RedisOptions>().BindConfiguration(RedisOptions.SectionName)
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.JsonSerializerOptions.Converters.Add(new OffsetDateTimeConverter());
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(
        ApiResponse.Failure(context.HttpContext, "VALIDATION_FAILED", "请求参数不符合要求",
            new
            {
                Fields = context.ModelState.SelectMany(entry => entry.Value?.Errors.Select(error =>
                    new { Field = entry.Key, Message = string.IsNullOrEmpty(error.ErrorMessage)
                        ? "字段格式不正确" : error.ErrorMessage }) ?? [])
            }));
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerDocumentTransformer>();
    options.AddOperationTransformer<BearerDocumentTransformer>();
});

var postgresConnection = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(postgresConnection))
{
    throw new InvalidOperationException("缺少 ConnectionStrings:Postgres 配置。");
}
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(postgresConnection));
builder.Services.AddDbContext<YanyunDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddSingleton<RedisConnection>();
builder.Services.AddPlatformAuthentication();
builder.Services.AddScoped<CsrfGuard>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<BusinessOperations>();
builder.Services.AddScoped<BusinessViews>();
builder.Services.AddScoped<CleanupService>();
builder.Services.AddScoped<NotificationPublisher>();
builder.Services.AddSingleton<NotificationDispatchQueue>();
builder.Services.AddHostedService<NotificationDispatcher>();
builder.Services.AddSingleton<AuthRedisStore>();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddHttpClient<IVerificationEmailSender, ResendEmailSender>(client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
}).RemoveAllLoggers();
if (builder.Configuration.GetValue("Scheduling:Enabled", true))
{
    builder.Services.AddQuartz(quartz =>
    {
        quartz.SchedulerName = "Yanyun.Notifications";
        quartz.UsePersistentStore(store =>
        {
            store.UseProperties = true;
            store.UsePostgres(postgresConnection);
            store.UseSystemTextJsonSerializer();
        });
        var key = new JobKey("station-notifications");
        quartz.AddJob<NotificationJob>(options => options.WithIdentity(key));
        quartz.AddTrigger(options => options.ForJob(key).WithIdentity("station-notifications-tick")
            .WithSimpleSchedule(schedule => schedule.WithIntervalInSeconds(10).RepeatForever()
                .WithMisfireHandlingInstructionNowWithExistingCount()));
    });
    builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
}
builder.Services.AddSignalR().AddJsonProtocol(options =>
    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);
builder.Services.AddSingleton<IUserIdProvider, NotificationUserIdProvider>();

var keyDirectory = builder.Configuration["DataProtection:KeysPath"] ??
    Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../../artifacts/dataprotection"));
builder.Services.AddDataProtection().SetApplicationName("Yanyun.Api")
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "yanyun.csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.Path = "/api/v1";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});

var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (origins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
        uri.Scheme is not ("http" or "https") || uri.AbsolutePath != "/" ||
        !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
        !string.IsNullOrEmpty(uri.UserInfo)))
{
    throw new InvalidOperationException("Cors:AllowedOrigins 必须为明确的 HTTP/HTTPS 来源。");
}
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var address in builder.Configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? [])
    {
        options.KnownProxies.Add(System.Net.IPAddress.Parse(address));
    }
});
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"], timeout: TimeSpan.FromSeconds(5))
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"], timeout: TimeSpan.FromSeconds(5));

var app = builder.Build();
if (usesEphemeralSigningKey)
{
    app.Logger.LogWarning("开发环境使用进程级临时 JWT 密钥，重启后旧 Token 失效；稳定联调请使用 user-secrets 配置。");
}
app.UseForwardedHeaders();
app.UseWebSockets();
app.UseRouting();
app.Use(async (context, next) =>
{
    var start = Stopwatch.GetTimestamp();
    var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "[unmatched]";
    try
    {
        await next(context);
    }
    finally
    {
        // 不把实际路径、查询字符串或请求头送入结构化日志。
        app.Logger.LogInformation("HTTP {Method} {Route} {StatusCode}，耗时 {Elapsed} ms，追踪 {TraceId}",
            context.Request.Method,
            route,
            context.Response.StatusCode, Stopwatch.GetElapsedTime(start).TotalMilliseconds,
            context.TraceIdentifier);
    }
});
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Request-Id"] = context.TraceIdentifier;
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next(context);
});
app.UseStatusCodePages(async context =>
{
    var status = context.HttpContext.Response.StatusCode;
    await context.HttpContext.Response.WriteAsJsonAsync(ApiResponse.Failure(context.HttpContext,
        status == 404 ? "RESOURCE_NOT_FOUND" : "REQUEST_FAILED",
        status == 404 ? "资源不存在或已失效" : "请求未能完成"), context.HttpContext.RequestAborted);
});
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications", options => options.CloseOnAuthenticationExpiration = true);
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
}).AllowAnonymous();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}
app.MapFallback("{**path}", (HttpContext context) => Results.Json(
    ApiResponse.Failure(context, "RESOURCE_NOT_FOUND", "资源不存在或已失效"), statusCode: 404))
    .AllowAnonymous().ExcludeFromDescription();

// 迁移由显式部署命令执行，启动只注册数据访问，不自动建表或写入演示数据。
await app.RunAsync();

public partial class Program;
