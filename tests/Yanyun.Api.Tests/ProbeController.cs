using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Yanyun.Api.Common;

namespace Yanyun.Api.Tests;

// 只加载到测试主机，验证真实中间件/默认权限策略，不进入部署程序集。
[ApiController]
[Route("api/v1/test-probe")]
public sealed class ProbeController : ControllerBase
{
    [HttpGet("protected")]
    public ActionResult<ApiResponse<object>> Protected()
        => Ok(ApiResponse.Success(new { HasAccess = true }));

    [AllowAnonymous]
    [HttpGet("exception")]
    public IActionResult Exception() => throw new InvalidOperationException("private-exception-secret");

    [AllowAnonymous]
    [HttpPost("json")]
    public ActionResult<ApiResponse<object>> JsonBody([FromBody] ProbeBody request)
        => Ok(ApiResponse.Success(request));
}

public sealed record ProbeBody(int Value);
