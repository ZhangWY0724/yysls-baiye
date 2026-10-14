import type { components } from "./api.generated";
type Schema = components["schemas"];

export type Option = Schema["DictionaryOption"];
export type Profile = Schema["ProfileResponse"];
export type ProfileSummary = Schema["ProfileSummaryResponse"];
export type Membership = Schema["MembershipResponse"];
export type Me = Schema["MeResponse"];
export type Guild = Schema["GuildResponse"] &
  Partial<
    Pick<
      Schema["GuildDetailResponse"],
      "public_members" | "recent_events" | "my_application"
    >
  >;
export type Member = Schema["MemberResponse"];
export type Application = Schema["ApplicationResponse"];
export type Invitation = Schema["InvitationResponse"] &
  Partial<Pick<Schema["InvitationCreatedResponse"], "code" | "invite_url">>;
export type InvitationPreview = Schema["InvitationPreviewResponse"];
export type Template = Schema["TemplateResponse"];
export type Assignment = Schema["AssignmentResponse"];
export type Raid = Schema["EventResponse"];
export type Notice = Schema["NotificationResponse"];
export type Page<T> = Omit<Schema["PagedResponseOfGuildResponse"], "items"> & {
  items: T[];
};
