using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Yanyun.Api.Infrastructure.OpenApi;

public sealed class BearerDocumentTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info.Title = "燕云百业平台 API";
        document.Info.Version = "v1";
        document.Info.Description = "一期真实业务接口：邮箱认证、角色、百业成员协作、副本排班、参与统计与站内通知。";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "已完成邮箱验证的账号 Access Token"
        };
        return Task.CompletedTask;
    }

    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        // 与默认权限策略一致：未标记公开的 Controller 操作均声明 Bearer。
        if (!context.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security = [new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
            }];
        }
        var action = (context.Description.ActionDescriptor as ControllerActionDescriptor)?.ActionName;
        var controller = (context.Description.ActionDescriptor as ControllerActionDescriptor)?.ControllerName;
        void Header(string name, bool required, string description, string? format = null)
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter { Name = name, In = ParameterLocation.Header, Required = required,
                Description = description, Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = format } });
        }
        if (controller == "Auth" && context.Description.HttpMethod == "POST" || action == "DeleteAccount")
            Header("X-CSRF-TOKEN", true, "通过 auth/csrf 获取；注销时携带本人 Bearer 获取相应凭证");
        if (controller == "Guilds" && action == "Create" || action is "CreateEvent" or "Signup" or "AcceptCode" or "AcceptDirect" or "Decide")
            Header("Idempotency-Key", action != "Decide", "UUID；同一操作重试复用。审批通过时必填", "uuid");
        else if (context.Description.HttpMethod == "POST" && controller is "Profiles" or "Raids" or "Collaboration" or "Guilds")
            Header("Idempotency-Key", false, "可选 UUID；创建请求和重试复用相同标识可避免重复写入", "uuid");
        if (context.Description.HttpMethod == "DELETE" && (controller == "Guilds" || controller == "Raids" && action != "CancelAssignment"))
            Header("X-Expected-Version", true, "当前资源版本；移出成员使用百业版本");
        if (controller == "Guilds" && action == "Delete") Header("X-Confirm-Guild-Id", true, "与路径百业 ID 一致", "uuid");
        if (controller == "Guilds" && action == "Remove") Header("X-Confirm-Membership-Id", true, "与路径成员 ID 一致", "uuid");
        var errorSchema = await context.GetOrCreateSchemaAsync(typeof(Yanyun.Api.Common.ApiResponse<object>), cancellationToken: cancellationToken);
        operation.Responses ??= new OpenApiResponses();
        foreach (var status in new[] { "400", "401", "403", "404", "409", "429", "500", "503" })
            operation.Responses.TryAdd(status, new OpenApiResponse { Description = "统一业务错误，按 error.code 处理",
                Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() { Schema = errorSchema } } });
    }
}
