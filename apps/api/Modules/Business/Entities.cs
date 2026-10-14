namespace Yanyun.Api.Modules.Business;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class GameProfile : Entity
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string Server { get; set; } = "official";
    public int Power { get; set; }
    public string[] Styles { get; set; } = [];
    public string OnlineSlotsJson { get; set; } = "[]";
    public string Bio { get; set; } = "";
    public DateTimeOffset? DeletedAt { get; set; }
}
public sealed class Guild : Entity
{
    public Guid OwnerUserId { get; set; }
    public string Name { get; set; } = "";
    public string Server { get; set; } = "official";
    public string Intro { get; set; } = "";
    public string RecruitRequirements { get; set; } = "";
    public bool Recruiting { get; set; } = true;
    public bool ReminderEnabled { get; set; } = true;
    public int RemindMinutes { get; set; } = 30;
    public long Version { get; set; } = 1;
}
public sealed class Membership : Entity
{
    public Guid GuildId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "member";
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class GuildApplication : Entity
{
    public Guid GuildId { get; set; }
    public Guid UserId { get; set; }
    public string Message { get; set; } = "";
    public string Status { get; set; } = "pending";
    public Guid? HandledBy { get; set; }
    public DateTimeOffset? HandledAt { get; set; }
}
public sealed class Invitation : Entity
{
    public Guid GuildId { get; set; }
    public Guid CreatedBy { get; set; }
    public string Type { get; set; } = "direct";
    public string? CodeHash { get; set; }
    public Guid? TargetUserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int MaxUses { get; set; } = 1;
    public int UsedCount { get; set; }
    public string Status { get; set; } = "pending";
}
public sealed class InvitationAcceptance : Entity
{
    public Guid InvitationId { get; set; }
    public Guid UserId { get; set; }
    public Guid MembershipId { get; set; }
}
public sealed class DungeonTemplate : Entity
{
    public Guid? GuildId { get; set; }
    public Guid? CreatedBy { get; set; }
    public string Name { get; set; } = "";
    public int NeedTank { get; set; }
    public int NeedHealer { get; set; }
    public int NeedDps { get; set; }
    public int DefaultDurationMin { get; set; } = 60;
    public DateTimeOffset? ArchivedAt { get; set; }
    public long Version { get; set; } = 1;
}
public sealed class RaidEvent : Entity
{
    public Guid GuildId { get; set; }
    public Guid TemplateId { get; set; }
    public Guid CreatedBy { get; set; }
    public string Title { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public int DurationMin { get; set; }
    public string SignupMode { get; set; } = "open";
    public string Status { get; set; } = "published";
    public string Note { get; set; } = "";
    public int NeedTank { get; set; }
    public int NeedHealer { get; set; }
    public int NeedDps { get; set; }
    public bool ReminderEnabled { get; set; }
    public int RemindMinutes { get; set; }
    public long Version { get; set; } = 1;
}
public sealed class Assignment : Entity
{
    public Guid EventId { get; set; }
    public Guid UserId { get; set; }
    public Guid ProfileId { get; set; }
    public Guid MembershipId { get; set; }
    public Guid AssignedBy { get; set; }
    public string SlotRole { get; set; } = "dps";
    public string Status { get; set; } = "confirmed";
}
public sealed class Notification : Entity
{
    public Guid UserId { get; set; }
    public Guid? GuildId { get; set; }
    public Guid? EventId { get; set; }
    public Guid[] RelatedUserIds { get; set; } = [];
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string TargetJson { get; set; } = "{}";
    public string DedupeKey { get; set; } = "";
    public DateTimeOffset? ReadAt { get; set; }
}
public sealed class IdempotencyRecord : Entity
{
    public Guid UserId { get; set; }
    public Guid? GuildId { get; set; }
    public Guid? EventId { get; set; }
    public Guid[] RelatedUserIds { get; set; } = [];
    public string Operation { get; set; } = "";
    public Guid Key { get; set; }
    public string RequestHash { get; set; } = "";
    public string ResponseJson { get; set; } = "{}";
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(24);
}
public sealed class AuditLog : Entity
{
    public Guid ActorId { get; set; }
    public Guid? GuildId { get; set; }
    public Guid? EventId { get; set; }
    public Guid[] RelatedUserIds { get; set; } = [];
    public string Action { get; set; } = "";
    public Guid TargetId { get; set; }
    public string TraceId { get; set; } = "";
}
