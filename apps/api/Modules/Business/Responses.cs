namespace Yanyun.Api.Modules.Business;

public sealed record PagedResponse<T>(T[] Items, int Page, int PageSize, int Total);
public sealed record ItemsResponse<T>(T[] Items);
public sealed record CursorResponse<T>(T[] Items, string? NextCursor);
public sealed record ProfileResponse(Guid Id, Guid UserId, string Name, string Server, decimal Power, string[] Styles,
    OnlineSlot[] OnlineSlots, string Bio, bool IsDefault, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public record ProfileSummaryResponse(Guid ProfileId, Guid UserId, string Name, string Server, decimal Power, string[] Styles);
public sealed record PublicProfileResponse(Guid ProfileId, Guid UserId, string Name, string Server, decimal Power, string[] Styles,
    OnlineSlot[] OnlineSlots, string Bio) : ProfileSummaryResponse(ProfileId, UserId, Name, Server, Power, Styles);
public sealed record MembershipResponse(Guid MembershipId, Guid GuildId, string GuildName, string Role,
    DateTimeOffset JoinedAt, long GuildVersion);
public sealed record MeResponse(Guid UserId, string Nickname, string? Email, DateTimeOffset? EmailVerifiedAt,
    bool RequiresEmailBinding, ProfileResponse? DefaultProfile, MembershipResponse? Membership);
public record GuildResponse(Guid Id, string Name, string Server, string Intro, string RecruitRequirements,
    bool Recruiting, int MemberCount, long Version);
public sealed record GuildCreatedResponse(Guid Id, string Name, string Server, string Intro, string RecruitRequirements,
    bool Recruiting, int MemberCount, long Version, MembershipResponse OwnerMembership)
    : GuildResponse(Id, Name, Server, Intro, RecruitRequirements, Recruiting, MemberCount, Version);
public sealed record RecentEventResponse(Guid Id, string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record GuildDetailResponse(Guid Id, string Name, string Server, string Intro, string RecruitRequirements,
    bool Recruiting, int MemberCount, long Version, ProfileSummaryResponse[] PublicMembers,
    RecentEventResponse[] RecentEvents, ApplicationResponse? MyApplication)
    : GuildResponse(Id, Name, Server, Intro, RecruitRequirements, Recruiting, MemberCount, Version);
public sealed record MemberResponse(Guid MembershipId, Guid UserId, string Nickname, string Role, DateTimeOffset JoinedAt,
    ProfileSummaryResponse? DefaultProfile, ProfileSummaryResponse[] Profiles);
public record ApplicationResponse(Guid Id, Guid GuildId, string GuildName, Guid UserId, string Message,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset? HandledAt, ProfileSummaryResponse? Applicant);
public sealed record ApplicationDecisionResponse(Guid Id, Guid GuildId, string GuildName, Guid UserId, string Message,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset? HandledAt, ProfileSummaryResponse? Applicant, Guid? MembershipId)
    : ApplicationResponse(Id, GuildId, GuildName, UserId, Message, Status, CreatedAt, HandledAt, Applicant);
public record InvitationResponse(Guid Id, Guid GuildId, string GuildName, string Type, Guid? TargetUserId,
    DateTimeOffset ExpiresAt, int MaxUses, int UsedCount, string Status, DateTimeOffset CreatedAt);
public sealed record InvitationCreatedResponse(Guid Id, Guid GuildId, string GuildName, string Type, Guid? TargetUserId,
    DateTimeOffset ExpiresAt, int MaxUses, int UsedCount, string Status, DateTimeOffset CreatedAt,
    string? Code, string? InviteUrl) : InvitationResponse(Id, GuildId, GuildName, Type, TargetUserId, ExpiresAt, MaxUses, UsedCount, Status, CreatedAt);
public sealed record InvitationLinkResponse(string InviteUrl);
public sealed record InvitationPreviewResponse(InvitationResponse Invitation, GuildResponse Guild,
    string? InviterNickname, bool CanAccept, bool CanDecline, string? UnavailableReason);
public sealed record TemplateResponse(Guid Id, Guid? GuildId, string Name, int NeedTank, int NeedHealer, int NeedDps,
    int DefaultDurationMin, long Version, DateTimeOffset? ArchivedAt);
public sealed record CapacityResponse(int Tank, int Healer, int Dps);
public sealed record AssignmentResponse(Guid Id, Guid UserId, Guid MembershipId, ProfileSummaryResponse Profile,
    string SlotRole, int? SlotIndex, string Status, DateTimeOffset CreatedAt, int? WaitlistPosition);
public sealed record EventResponse(Guid Id, Guid GuildId, Guid TemplateId, string TemplateName, string Title,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, int DurationMin, string SignupMode, string Status, string Note,
    long Version, bool ReminderEnabled, int RemindMinutes, bool IsLocked, CapacityResponse Capacities,
    AssignmentResponse[] ConfirmedAssignments, AssignmentResponse[] WaitlistAssignments, AssignmentResponse? MyAssignment,
    bool CanSignup, bool CanCancel, bool CanManage, string? DisabledReason);
public sealed record EventsWeekResponse(string WeekKey, DateTimeOffset WeekStart, DateTimeOffset WeekEnd, string Timezone,
    DateTimeOffset ServerTime, EventResponse[] Events);
public sealed record AssignmentCreatedResponse(Guid AssignmentId, string Status, int? WaitlistPosition, long EventVersion);
public sealed record MyEventResponse(Guid Id, Guid GuildId, string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    string Status, long Version, bool CanCancel);
public sealed record MyAssignmentResponse(Guid Id, string Status, string SlotRole, ProfileSummaryResponse Profile);
public sealed record MyWeekItemResponse(MyEventResponse Event, MyAssignmentResponse Assignment);
public sealed record MyWeekResponse(string WeekKey, DateTimeOffset WeekStart, DateTimeOffset WeekEnd, string Timezone,
    DateTimeOffset ServerTime, MyWeekItemResponse[] Items);
public sealed record PlayerSummaryResponse(Guid UserId, string Nickname, ProfileSummaryResponse? DefaultProfile,
    ProfileSummaryResponse[] MatchingProfiles);
public sealed record PlayerResponse(Guid UserId, string Nickname, PublicProfileResponse[] Profiles);
public sealed record ParticipationItemResponse(Guid MembershipId, Guid UserId, string Nickname, int WeeklyCount, int CumulativeCount, int Rank);
public sealed record ParticipationResponse(string Basis, string WeekKey, DateTimeOffset WeekStart, DateTimeOffset WeekEnd,
    string Timezone, DateTimeOffset ServerTime, DateTimeOffset CumulativeCutoff, bool IncludesFutureInCurrentWeek,
    ParticipationItemResponse[] Items, int Page, int PageSize, int Total, Guid[] UnassignedUserIds);
public sealed record NotificationTargetResponse(string Kind, Guid? GuildId, Guid? EventId, Guid? InvitationId, Guid? ApplicationId);
public sealed record NotificationResponse(Guid Id, string Type, string Title, string Body, DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt, NotificationTargetResponse Target);
public sealed record UnreadResponse(int UnreadCount);
public sealed record ReadResponse(int UpdatedCount, int UnreadCount);
public sealed record DeletedResponse(bool Deleted);
public sealed record MemberDeletedResponse(bool Deleted, long GuildVersion);
public sealed record LeaveResponse(bool Deleted, bool GuildDissolved);
public sealed record ArchivedResponse(bool Archived, long Version);
public sealed record CancelledResponse(bool Cancelled, long Version);
public sealed record AssignmentDeletedResponse(bool Deleted, long EventVersion);
public sealed record RoleChangedResponse(MemberResponse Member, long GuildVersion);
public sealed record MemberRoleResponse(Guid MembershipId, string Role);
public sealed record OwnerTransferResponse(Guid OwnerUserId, string PreviousOwnerRole, long Version, MemberRoleResponse[] Members);
public sealed record ReminderResponse(bool ReminderEnabled, int RemindMinutes, long Version);
