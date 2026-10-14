using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yanyun.Api.Common;
using Yanyun.Api.Infrastructure.Persistence;

namespace Yanyun.Api.Controllers;

[ApiController]
[Route("api/v1/dictionaries/profile-options")]
public sealed class ProfileOptionsController(YanyunDbContext dbContext) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<ProfileOptionsResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ProfileOptionsResponse>>> Get(CancellationToken cancellationToken)
    {
        var rows = await dbContext.ProfileStyleOptions.AsNoTracking()
            .OrderBy(option => option.SortOrder).ThenBy(option => option.Code)
            .ToListAsync(cancellationToken);
        var version = rows.Count == 0 ? "empty"
            : rows.Max(option => option.UpdatedAt).UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Ok(ApiResponse.Success(new ProfileOptionsResponse(version,
            [new("official", "官服"), new("channel", "渠道服")],
            rows.Where(option => option.Enabled).Select(option => new DictionaryOption(option.Code, option.Label)).ToArray())));
    }
}

public sealed record DictionaryOption(string Code, string Label);
public sealed record ProfileOptionsResponse(
    string DictionaryVersion, DictionaryOption[] Servers, DictionaryOption[] Styles);
