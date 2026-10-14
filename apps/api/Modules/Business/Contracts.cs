using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yanyun.Api.Modules.Business;

public sealed record OnlineSlot([Range(1, 7)] int Weekday, [Range(0, 1439)] int StartMinute, [Range(1, 1440)] int EndMinute);
public sealed class ProfileWrite
{
    [StringLength(32, MinimumLength = 1)] public string? Name { get; set; }
    [RegularExpression("^(official|channel)$")] public string? Server { get; set; }
    [Range(typeof(decimal), "0", "99999999.9999")] public decimal? Power { get; set; }
    public string[]? Styles { get; set; }
    public OnlineSlot[]? OnlineSlots { get; set; }
    [StringLength(500)] public string? Bio { get; set; }
}
public sealed class MeWrite
{
    [StringLength(32, MinimumLength = 1)] public string? Nickname { get; set; }
    public Guid? DefaultProfileId { get; set; }
}
public sealed record AccountDeletionRequest([property: JsonRequired] Guid ConfirmUserId, [Required] string Confirmation);
public sealed class GuildWrite
{
    [StringLength(40, MinimumLength = 1)] public string? Name { get; set; }
    [RegularExpression("^(official|channel)$")] public string? Server { get; set; }
    [StringLength(2000)] public string? Intro { get; set; }
    [StringLength(1000)] public string? RecruitRequirements { get; set; }
    public bool? Recruiting { get; set; }
    public long? ExpectedVersion { get; set; }
}
public sealed record ReminderWrite([property: JsonRequired] bool ReminderEnabled, [Range(1, 1440)] int RemindMinutes,
    [Range(typeof(long), "1", "9007199254740991")] long ExpectedVersion);
public sealed record ApplicationWrite([StringLength(500)] string? Message);
public sealed record ApplicationDecision([Required, RegularExpression("^(approved|rejected)$")] string Decision,
    [Required, RegularExpression("^pending$")] string ExpectedStatus);
public sealed record MemberRoleWrite([Required, RegularExpression("^(member|admin)$")] string Role,
    [Range(typeof(long), "1", "9007199254740991")] long ExpectedVersion);
public sealed record OwnerTransferWrite([property: JsonRequired] Guid TargetMembershipId,
    [Required, RegularExpression("^(member|admin)$")] string PreviousOwnerRole, [Range(typeof(long), "1", "9007199254740991")] long ExpectedVersion);
public sealed record LeaveWrite([Range(typeof(long), "1", "9007199254740991")] long ExpectedVersion,
    [property: JsonRequired] Guid ConfirmGuildId, bool? ConfirmDissolve);
public sealed record InvitationWrite([Required, RegularExpression("^(code|direct)$")] string Type,
    Guid? TargetUserId, DateTimeOffset? ExpiresAt, [Range(1, 1000)] int? MaxUses);
public sealed class TemplateWrite
{
    [StringLength(80, MinimumLength = 1)] public string? Name { get; set; }
    [Range(0, 10)] public int? NeedTank { get; set; }
    [Range(0, 10)] public int? NeedHealer { get; set; }
    [Range(0, 10)] public int? NeedDps { get; set; }
    [Range(1, 1440)] public int? DefaultDurationMin { get; set; }
    public long? ExpectedVersion { get; set; }
}
public sealed class EventWrite
{
    public Guid? TemplateId { get; set; }
    [StringLength(80, MinimumLength = 1)] public string? Title { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    [Range(1, 1440)] public int? DurationMin { get; set; }
    [RegularExpression("^(open|assign)$")] public string? SignupMode { get; set; }
    [RegularExpression("^(draft|published)$")] public string? Status { get; set; }
    [Range(0, 10)] public int? NeedTank { get; set; }
    [Range(0, 10)] public int? NeedHealer { get; set; }
    [Range(0, 10)] public int? NeedDps { get; set; }
    [StringLength(2000)] public string? Note { get; set; }
    public bool? ReminderEnabled { get; set; }
    [Range(1, 1440)] public int? RemindMinutes { get; set; }
    public long? ExpectedVersion { get; set; }
}
public sealed record AssignmentWrite([Required, RegularExpression("^(signup|assign)$")] string Action,
    [property: JsonRequired] Guid ProfileId, [Required, RegularExpression("^(tank|healer|dps)$")] string SlotRole, [Range(1, 10)] int? SlotIndex = null);
public sealed record ReadNotificationsWrite(Guid[]? NotificationIds, bool? All);

public static class BusinessJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        Converters = { new Yanyun.Api.Common.OffsetDateTimeConverter() } };
    public static string Serialize(object value) => JsonSerializer.Serialize(value, Options);
    public static JsonElement Parse(string value) => JsonSerializer.Deserialize<JsonElement>(value, Options);
}
