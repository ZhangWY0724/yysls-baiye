"use client";
import Link from "next/link";
import {
  CalendarDays,
  Check,
  Clock3,
  Copy,
  Link2,
  LogOut,
  Plus,
  Search,
  ShieldCheck,
  UserMinus,
  UserPlus,
  Users,
  Bird,
} from "lucide-react";
import { useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { api, idempotency } from "@/lib/api";
import {
  Application,
  Guild,
  GuildCreated,
  GuildDetail,
  Invitation,
  InvitationCreated,
  InvitationPreview,
  Me,
  Member,
  Page,
  Participation,
  Player,
  PlayerSummary,
  Reminder,
} from "@/lib/contracts";
import {
  APPLICATION_STATUSES,
  canManage,
  Card,
  date,
  Form,
  QueryState,
  roleName,
  serverName,
  statusName,
  statusTone,
  TextField,
  useAction,
  useDebounced,
  useProfileOptions,
  useQuery,
  value,
  WEEKDAYS,
  formatPower,
} from "./business-ui";
import { formatTime, OnlineTime } from "./online-time";
import { TemplatesPanel } from "./raid-pages";
import {
  Avatar,
  Button,
  ButtonLink,
  EmptyState,
  Field,
  IconButton,
  Modal,
  PageActions,
  Pager,
  StatusPill,
  Switch,
  Tabs,
  useConfirm,
} from "./ui";

type Reload = () => Promise<void>;

function RecruitPill({ recruiting }: { recruiting: boolean }) {
  return <StatusPill tone={recruiting ? "ok" : "muted"}>{recruiting ? "招募中" : "暂停招募"}</StatusPill>;
}

function ServerSelect({ value: selected, onChange }: { value: string; onChange: (value: string) => void }) {
  return (
    <select aria-label="区服" value={selected} onChange={(event) => onChange(event.target.value)}>
      <option value="">全部区服</option>
      <option value="official">官服</option>
      <option value="channel">渠道服</option>
    </select>
  );
}

export function GuildList({ me, reload }: { me: Me; reload: Reload }) {
  const router = useRouter();
  const [page, setPage] = useState(1);
  const [q, setQ] = useState("");
  const search = useDebounced(q);
  const [server, setServer] = useState("");
  const [create, setCreate] = useState(false);
  const [applied, setApplied] = useState<string[]>([]);
  const query = useQuery<Page<Guild>>(
    `/guilds?page=${page}&q=${encodeURIComponent(search)}${server ? `&server=${server}` : ""}`,
  );
  const action = useAction(async () => {
    query.reload();
    await reload();
  });
  return (
    <div className="stack">
      {action.feedback}
      <PageActions>
        <Button
          variant="primary"
          icon={Plus}
          disabled={!!me.membership}
          title={me.membership ? "你已加入百业，不能再创建" : undefined}
          onClick={() => setCreate(true)}
        >
          创建百业
        </Button>
      </PageActions>
      <div className="toolbar">
        <div className="filters">
          <label className="search">
            <Search size={16} strokeWidth={1.75} aria-hidden />
            <input
              type="search"
              placeholder="搜索百业名称"
              aria-label="搜索百业"
              value={q}
              onChange={(e) => {
                setQ(e.target.value);
                setPage(1);
              }}
            />
          </label>
          <ServerSelect
            value={server}
            onChange={(next) => {
              setServer(next);
              setPage(1);
            }}
          />
        </div>
      </div>
      <QueryState {...query} retry={query.reload}>
        {!query.data?.items.length && (
          <EmptyState icon={Users} title="没有找到百业">
            试试换个关键词或区服。
          </EmptyState>
        )}
        <div className="tile-grid">
          {query.data?.items.map((guild) => {
            const canApply = !me.membership && guild.recruiting;
            return (
              <article className="tile" key={guild.id}>
                <header className="tile-head">
                  <Avatar name={guild.name} id={guild.id} size="lg" square />
                  <div>
                    <h2>{guild.name}</h2>
                    <small>
                      {serverName(guild.server)} · {guild.member_count} 位成员
                    </small>
                  </div>
                  <RecruitPill recruiting={guild.recruiting} />
                </header>
                <p className="tile-text">{guild.intro || "暂未填写百业简介"}</p>
                <p className="tile-meta">
                  <Users size={14} strokeWidth={1.75} aria-hidden />
                  招募要求：{guild.recruit_requirements || "暂未填写"}
                </p>
                <footer className="tile-actions">
                  <ButtonLink size="sm" href={`/guilds/${guild.id}`}>
                    查看百业
                  </ButtonLink>
                  {canApply && (
                    <Button
                      size="sm"
                      variant="primary"
                      icon={applied.includes(guild.id) ? Check : undefined}
                      disabled={action.busy || applied.includes(guild.id)}
                      onClick={() =>
                        action.run(async () => {
                          await api(`/guilds/${guild.id}/applications`, "POST", { message: "" });
                          setApplied((current) => [...current, guild.id]);
                        }, "申请已提交")
                      }
                    >
                      {applied.includes(guild.id) ? "已申请" : "申请加入"}
                    </Button>
                  )}
                </footer>
              </article>
            );
          })}
        </div>
        <Pager page={page} total={query.data?.total || 0} setPage={setPage} />
      </QueryState>
      {create && (
        <Modal title="创建百业" busy={action.busy} close={() => setCreate(false)}>
          <Form
            busy={action.busy}
            onSubmit={(form) =>
              action.run(async () => {
                const guild = await api<GuildCreated>(
                  "/guilds",
                  "POST",
                  {
                    name: value(form, "name"),
                    server: value(form, "server"),
                    intro: value(form, "intro"),
                    recruit_requirements: value(form, "recruit_requirements"),
                  },
                  idempotency(),
                );
                await reload();
                router.push(`/guilds/${guild.id}`);
              }, "百业已创建")
            }
          >
            <TextField name="name" label="百业名称" required />
            <Field label="区服" hint="需要已有对应区服的游戏角色">
              <select name="server">
                <option value="official">官服</option>
                <option value="channel">渠道服</option>
              </select>
            </Field>
            <Field label="简介">
              <textarea name="intro" maxLength={2000} />
            </Field>
            <Field label="招募要求">
              <textarea name="recruit_requirements" maxLength={1000} />
            </Field>
            {action.error && <p className="text-bad">{action.error}</p>}
            <div className="form-actions">
              <Button variant="ghost" onClick={() => setCreate(false)}>
                取消
              </Button>
              <Button type="submit" variant="primary">
                {action.busy ? "正在创建…" : "创建百业"}
              </Button>
            </div>
          </Form>
        </Modal>
      )}
    </div>
  );
}

export function GuildHome({ id, me, reload }: { id: string; me: Me; reload: Reload }) {
  const router = useRouter();
  const confirm = useConfirm();
  const query = useQuery<GuildDetail>(`/guilds/${id}`);
  const { styleLabel } = useProfileOptions();
  const action = useAction(async () => {
    query.reload();
    await reload();
  });
  const guild = query.data;
  const member = me.membership?.guild_id === id ? me.membership : null;
  const owner = member?.role === "owner";
  return (
    <QueryState {...query} retry={query.reload}>
      {guild && (
        <div className="stack">
          {action.feedback}
          <PageActions>
            {member && (
              <>
                <ButtonLink href={`/guilds/${id}/members`}>成员管理</ButtonLink>
                <ButtonLink variant="primary" icon={CalendarDays} href={`/guilds/${id}/raids`}>
                  本周副本
                </ButtonLink>
              </>
            )}
            {!me.membership && guild.recruiting && (
              <Button
                variant="primary"
                icon={guild.my_application ? Check : UserPlus}
                disabled={!!guild.my_application || action.busy}
                onClick={() =>
                  action.run(() => api(`/guilds/${id}/applications`, "POST", { message: "" }), "申请已提交")
                }
              >
                {guild.my_application ? "已申请" : "申请加入"}
              </Button>
            )}
          </PageActions>
          <section className="card hero">
            <Avatar name={guild.name} id={guild.id} size="xl" square />
            <div>
              <h2>
                {guild.name}
                <RecruitPill recruiting={guild.recruiting} />
              </h2>
              <p>
                {serverName(guild.server)} · {guild.member_count} 位成员
              </p>
            </div>
          </section>
          <div className={member ? "split" : undefined}>
            <div className="stack">
              <Card title="百业介绍">
                <div className="prose">
                  <p>{guild.intro || "暂未填写百业简介"}</p>
                  <h3>招募要求</h3>
                  <p>{guild.recruit_requirements || "暂未填写招募要求"}</p>
                </div>
              </Card>
              {member && (
                <Card title="近期副本">
                  {guild.recent_events.length ? (
                    <div className="list">
                      {guild.recent_events.map((event) => (
                        <Link className="list-row list-link" key={event.id} href={`/guilds/${id}/raids?event=${event.id}`}>
                          <Clock3 size={16} strokeWidth={1.75} aria-hidden />
                          <span className="list-main">
                            <strong>{event.title}</strong>
                            <small>{date(event.starts_at)}</small>
                          </span>
                          <span className="link-text">查看安排</span>
                        </Link>
                      ))}
                    </div>
                  ) : (
                    <EmptyState compact>暂时没有发布副本安排</EmptyState>
                  )}
                </Card>
              )}
            </div>
            {member && (
              <Card title="成员">
                {guild.public_members.length ? (
                  <div className="list">
                    {guild.public_members.map((item) => (
                    <div className="list-row list-row-dense" key={item.user_id}>
                      <Avatar name={item.nickname} id={item.user_id} size="sm" />
                      <span className="list-main">
                        <strong>{item.nickname}</strong>
                        <small>
                          {roleName(item.role)} · 加入于 {date(item.joined_at).split(" ")[0]}
                        </small>
                      </span>
                    </div>
                  ))}
                  </div>
                ) : (
                  <EmptyState compact>暂无成员</EmptyState>
                )}
              </Card>
            )}
          </div>
          {member && (
            <Card title={owner ? "退出并解散百业" : "退出百业"} className="card-danger">
              <div className="danger-row">
                <p>
                  {owner
                    ? "社长退出即解散整个百业，删除全体成员在本百业的业务数据，无法恢复。"
                    : "退出后将删除你在本百业的全部记录；你创建的模板、场次及关联其他人的排班也会删除。"}
                </p>
                <Button
                  variant="danger-ghost"
                  icon={LogOut}
                  disabled={action.busy}
                  onClick={async () => {
                    if (
                      !(await confirm({
                        title: owner ? "退出并解散百业" : "退出百业",
                        body: owner
                          ? "退会即解散整个百业，删除所有百业业务数据。成员账号与个人角色保留。"
                          : "退会将删除你在本百业的全部记录，你创建的模板、场次及关联排班也会删除。",
                        confirmLabel: owner ? "解散并退出" : "退出百业",
                        danger: true,
                        typeToConfirm: owner ? guild.name : undefined,
                      }))
                    )
                      return;
                    action.run(async () => {
                      await api(`/guilds/${id}/leave`, "POST", {
                        expected_version: guild.version,
                        confirm_guild_id: id,
                        ...(owner ? { confirm_dissolve: true } : {}),
                      });
                      await reload();
                      router.push("/guilds");
                    }, owner ? "百业已解散" : "已退出百业");
                  }}
                >
                  {owner ? "解散并退出" : "退出百业"}
                </Button>
              </div>
            </Card>
          )}
        </div>
      )}
    </QueryState>
  );
}

export function GuildSettings({ id, me, reload }: { id: string; me: Me; reload: Reload }) {
  const router = useRouter();
  const confirm = useConfirm();
  const [tab, setTab] = useState<"info" | "templates" | "reminders">("info");
  const guild = useQuery<Guild>(`/guilds/${id}`);
  const reminders = useQuery<Reminder>(tab === "reminders" ? `/guilds/${id}/reminder-settings` : null);
  const action = useAction(async () => {
    guild.reload();
    reminders.reload();
    await reload();
  });
  if (!canManage(me, id))
    return <EmptyState title="你没有这个操作的权限">仅社长或管理员可修改百业设置。</EmptyState>;
  return (
    <div className="stack narrow">
      {action.feedback}
      <div className="toolbar">
        <Tabs
          label="百业设置"
          items={[
            ["info", "百业资料"],
            ["templates", "副本模板"],
            ["reminders", "提醒配置"],
          ]}
          value={tab}
          onChange={setTab}
        />
      </div>
      {tab === "info" && (
        <QueryState {...guild} retry={guild.reload}>
          {guild.data && (
            <>
              <Card title="百业资料">
                <Form
                  key={guild.data.version}
                  busy={action.busy}
                  onSubmit={(form) =>
                    action.run(
                      () =>
                        api(`/guilds/${id}`, "PATCH", {
                          name: value(form, "name"),
                          intro: value(form, "intro"),
                          recruit_requirements: value(form, "recruit_requirements"),
                          recruiting: form.has("recruiting"),
                          expected_version: guild.data!.version,
                        }),
                      "资料已保存",
                    )
                  }
                >
                  <TextField name="name" label="百业名称" defaultValue={guild.data.name} required />
                  <Field label="简介">
                    <textarea name="intro" defaultValue={guild.data.intro} maxLength={2000} />
                  </Field>
                  <Field label="招募要求">
                    <textarea name="recruit_requirements" defaultValue={guild.data.recruit_requirements} maxLength={1000} />
                  </Field>
                  <Switch name="recruiting" label="开放招募" defaultChecked={guild.data.recruiting} />
                  <div className="form-actions">
                    <Button type="submit" variant="primary">
                      保存资料
                    </Button>
                  </div>
                </Form>
              </Card>
              {me.membership?.role === "owner" && (
                <Card title="解散百业" className="card-danger">
                  <div className="danger-row">
                    <p>解散会删除全体成员在本百业的全部业务数据，成员账号与个人角色保留，无法恢复。</p>
                    <Button
                      variant="danger-ghost"
                      disabled={action.busy}
                      onClick={async () => {
                        if (
                          !(await confirm({
                            title: "永久解散百业",
                            body: "解散后全体成员在本百业的申请、排班、模板和场次都会删除，无法恢复。",
                            confirmLabel: "永久解散",
                            danger: true,
                            typeToConfirm: guild.data!.name,
                          }))
                        )
                          return;
                        action.run(async () => {
                          await api(`/guilds/${id}`, "DELETE", undefined, {
                            "X-Expected-Version": String(guild.data!.version),
                            "X-Confirm-Guild-Id": id,
                          });
                          await reload();
                          router.push("/guilds");
                        }, "百业已解散");
                      }}
                    >
                      永久解散百业
                    </Button>
                  </div>
                </Card>
              )}
            </>
          )}
        </QueryState>
      )}
      {tab === "templates" && (
        <Card>
          <TemplatesPanel id={id} />
        </Card>
      )}
      {tab === "reminders" && (
        <QueryState {...reminders} retry={reminders.reload}>
          {reminders.data && (
            <Card title="提醒配置">
              <Form
                key={reminders.data.version}
                busy={action.busy}
                onSubmit={(form) =>
                  action.run(
                    () =>
                      api(`/guilds/${id}/reminder-settings`, "PUT", {
                        reminder_enabled: form.has("enabled"),
                        remind_minutes: Number(value(form, "minutes")),
                        expected_version: reminders.data!.version,
                      }),
                    "提醒配置已保存",
                  )
                }
              >
                <Switch name="enabled" label="新场次默认启用开打提醒" defaultChecked={reminders.data.reminder_enabled} />
                <TextField
                  name="minutes"
                  label="提前分钟数"
                  type="number"
                  min={1}
                  max={1440}
                  defaultValue={reminders.data.remind_minutes}
                  hint="仅影响新建场次；已有场次在场次详情中单独调整。"
                />
                <div className="form-actions">
                  <Button type="submit" variant="primary">
                    保存提醒
                  </Button>
                </div>
              </Form>
            </Card>
          )}
        </QueryState>
      )}
    </div>
  );
}

type MemberTab = "members" | "applications" | "invitations" | "participation";
export function MembersPage({ id, me, reload }: { id: string; me: Me; reload: Reload }) {
  const params = useSearchParams();
  const confirm = useConfirm();
  const manage = canManage(me, id);
  const owner = me.membership?.guild_id === id && me.membership.role === "owner";
  const wanted: MemberTab = params.get("tab") === "applications" && manage ? "applications" : "members";
  const [tab, setTab] = useState<MemberTab>(wanted);
  const [lastWanted, setLastWanted] = useState(wanted);
  const [page, setPage] = useState(1);
  if (wanted !== lastWanted) {
    setLastWanted(wanted);
    setTab(wanted);
    setPage(1);
  }
  const [search, setSearch] = useState("");
  const memberSearch = useDebounced(search);
  const [status, setStatus] = useState("pending");
  const [unassigned, setUnassigned] = useState(false);
  const [link, setLink] = useState("");
  const [copied, setCopied] = useState(false);
  const members = useQuery<Page<Member>>(
    tab === "members" ? `/guilds/${id}/members?page=${page}&q=${encodeURIComponent(memberSearch)}` : null,
  );
  const applications = useQuery<Page<Application>>(
    tab === "applications" ? `/guilds/${id}/applications?page=${page}&status=${status}` : null,
  );
  const invitations = useQuery<Page<Invitation>>(tab === "invitations" ? `/guilds/${id}/invitations?page=${page}` : null);
  const stats = useQuery<Participation>(
    tab === "participation" ? `/guilds/${id}/participation?page=${page}&unassigned_only=${unassigned}` : null,
  );
  const action = useAction(async () => {
    members.reload();
    applications.reload();
    invitations.reload();
    stats.reload();
    await reload();
  });
  const version = me.membership?.guild_version || 0;
  const tabs: [MemberTab, string][] = [
    ["members", "成员"],
    ...(manage
      ? ([
          ["applications", "审批申请"],
          ["invitations", "邀请管理"],
          ["participation", "参与情况"],
        ] as [MemberTab, string][])
      : []),
  ];
  const maxCumulative = Math.max(1, ...(stats.data?.items.map((item) => item.cumulative_count) || [0]));
  return (
    <div className="stack">
      {action.feedback}
      <div className="toolbar">
        <Tabs
          label="成员管理视图"
          items={tabs}
          value={tab}
          onChange={(next) => {
            setTab(next);
            setPage(1);
          }}
        />
        {tab === "members" && (
          <label className="search">
            <Search size={16} strokeWidth={1.75} aria-hidden />
            <input
              type="search"
              aria-label="搜索成员"
              placeholder="搜索成员名称"
              value={search}
              onChange={(event) => {
                setSearch(event.target.value);
                setPage(1);
              }}
            />
          </label>
        )}
        {tab === "applications" && (
          <select
            className="select-inline"
            aria-label="申请状态"
            value={status}
            onChange={(e) => {
              setStatus(e.target.value);
              setPage(1);
            }}
          >
            {APPLICATION_STATUSES.map((item) => (
              <option key={item} value={item}>
                {statusName(item)}
              </option>
            ))}
          </select>
        )}
        {tab === "invitations" && (
          <Button
            variant="primary"
            icon={Link2}
            disabled={action.busy}
            onClick={() =>
              action.run(async () => {
                const result = await api<InvitationCreated>(`/guilds/${id}/invitations`, "POST", {
                  type: "code",
                  max_uses: 20,
                });
                setCopied(false);
                setLink(new URL(result.invite_url!, location.origin).toString());
              }, "邀请链接已生成，请立即复制保存")
            }
          >
            生成邀请链接
          </Button>
        )}
        {tab === "participation" && (
          <Switch
            label="只看本周未排班"
            checked={unassigned}
            onChange={(checked) => {
              setUnassigned(checked);
              setPage(1);
            }}
          />
        )}
      </div>

      {tab === "members" && (
        <Card>
          <QueryState {...members} retry={members.reload}>
            {!members.data?.items.length && <EmptyState compact>没有找到成员</EmptyState>}
            <div className="list">
              {members.data?.items.map((member) => {
                const editable = manage && member.user_id !== me.user_id && member.role !== "owner";
                return (
                  <div className="list-row" key={member.membership_id}>
                    <Avatar name={member.nickname} id={member.user_id} />
                    <span className="list-main">
                      <strong>
                        {member.nickname}
                        {member.role !== "member" && (
                          <StatusPill tone={member.role === "owner" ? "accent" : "muted"}>{roleName(member.role)}</StatusPill>
                        )}
                      </strong>
                      <small>{member.profiles.map((profile) => profile.name).join(" / ") || "未设置角色"}</small>
                    </span>
                    {editable && (
                      <div className="list-actions">
                        {owner && (
                          <>
                            <Button
                              size="sm"
                              variant="ghost"
                              icon={ShieldCheck}
                              disabled={action.busy}
                              onClick={() =>
                                action.run(
                                  () =>
                                    api(`/guilds/${id}/members/${member.membership_id}/role`, "PATCH", {
                                      role: member.role === "admin" ? "member" : "admin",
                                      expected_version: version,
                                    }),
                                  member.role === "admin" ? "已取消管理员" : "已设为管理员",
                                )
                              }
                            >
                              {member.role === "admin" ? "取消管理员" : "设为管理员"}
                            </Button>
                            <Button
                              size="sm"
                              variant="ghost"
                              disabled={action.busy}
                              onClick={async () => {
                                if (
                                  await confirm({
                                    title: "转让社长",
                                    body: `将社长转让给 ${member.nickname}，你将成为普通成员。`,
                                    confirmLabel: "确认转让",
                                    danger: true,
                                  })
                                )
                                  action.run(
                                    () =>
                                      api(`/guilds/${id}/owner-transfer`, "POST", {
                                        target_membership_id: member.membership_id,
                                        previous_owner_role: "member",
                                        expected_version: version,
                                      }),
                                    "社长已转让",
                                  );
                              }}
                            >
                              转让社长
                            </Button>
                          </>
                        )}
                        {(owner || member.role === "member") && (
                          <Button
                            size="sm"
                            variant="danger-ghost"
                            icon={UserMinus}
                            disabled={action.busy}
                            onClick={async () => {
                              if (
                                await confirm({
                                  title: "移出成员",
                                  body: `移出 ${member.nickname} 将删除其在本百业的全部记录，以及其创建的模板、场次和关联排班。`,
                                  confirmLabel: "移出",
                                  danger: true,
                                })
                              )
                                action.run(
                                  () =>
                                    api(`/guilds/${id}/members/${member.membership_id}`, "DELETE", undefined, {
                                      "X-Expected-Version": String(version),
                                      "X-Confirm-Membership-Id": member.membership_id,
                                    }),
                                  "成员已移出",
                                );
                            }}
                          >
                            移出
                          </Button>
                        )}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
            <Pager page={page} total={members.data?.total || 0} setPage={setPage} />
          </QueryState>
        </Card>
      )}

      {tab === "applications" && (
        <Card>
          <QueryState {...applications} retry={applications.reload}>
            {!applications.data?.items.length && <EmptyState compact>暂无{statusName(status)}的申请</EmptyState>}
            <div className="list">
              {applications.data?.items.map((item) => (
                <div className="list-row" key={item.id}>
                  <Avatar name={item.applicant?.name || "玩家"} id={item.user_id} />
                  <span className="list-main">
                    <strong>
                      {item.applicant?.name || "玩家"}
                      <StatusPill tone={statusTone(item.status)}>{statusName(item.status)}</StatusPill>
                    </strong>
                    {item.message && <span className="list-desc">{item.message}</span>}
                    <small>
                      {item.applicant ? `鹅力 ${formatPower(item.applicant.power)} · ${serverName(item.applicant.server)} · ` : ""}
                      {date(item.created_at)}
                    </small>
                  </span>
                  {item.status === "pending" && (
                    <div className="list-actions">
                      <Button
                        size="sm"
                        variant="ghost"
                        disabled={action.busy}
                        onClick={() =>
                          action.run(
                            () =>
                              api(`/applications/${item.id}`, "PATCH", { decision: "rejected", expected_status: "pending" }),
                            "已拒绝申请",
                          )
                        }
                      >
                        拒绝
                      </Button>
                      <Button
                        size="sm"
                        variant="primary"
                        disabled={action.busy}
                        onClick={() =>
                          action.run(
                            () =>
                              api(
                                `/applications/${item.id}`,
                                "PATCH",
                                { decision: "approved", expected_status: "pending" },
                                idempotency(),
                              ),
                            "已通过申请",
                          )
                        }
                      >
                        通过
                      </Button>
                    </div>
                  )}
                </div>
              ))}
            </div>
            <Pager page={page} total={applications.data?.total || 0} setPage={setPage} />
          </QueryState>
        </Card>
      )}

      {tab === "invitations" && (
        <>
          {link && (
            <div className="copy-field">
              <input aria-label="邀请链接" readOnly value={link} onFocus={(event) => event.target.select()} />
              <Button
                icon={copied ? Check : Copy}
                onClick={() => navigator.clipboard.writeText(link).then(() => setCopied(true))}
              >
                {copied ? "已复制" : "复制"}
              </Button>
            </div>
          )}
          <Card>
            <QueryState {...invitations} retry={invitations.reload}>
              {!invitations.data?.items.length && <EmptyState compact>还没有发出邀请</EmptyState>}
              <div className="list">
                {invitations.data?.items.map((item) => (
                  <div className="list-row" key={item.id}>
                    <span className="list-main">
                      <strong>
                        {item.type === "code" ? "邀请链接" : "定向邀请"}
                        <StatusPill tone={statusTone(item.status)}>{statusName(item.status)}</StatusPill>
                      </strong>
                      <small>
                        已使用 {item.used_count}/{item.max_uses} · 有效期至 {date(item.expires_at)}
                      </small>
                    </span>
                    {item.status === "pending" && item.type === "code" && (
                      <Button
                        size="sm"
                        variant="ghost"
                        icon={Copy}
                        disabled={action.busy}
                        onClick={() =>
                          action.run(async () => {
                            const result = await api<{ invite_url: string }>(`/invitations/${item.id}/link`);
                            const url = new URL(result.invite_url, location.origin).toString();
                            setLink(url);
                            setCopied(false);
                            await navigator.clipboard.writeText(url).then(() => setCopied(true), () => {});
                          }, "邀请链接已显示在上方，可复制")
                        }
                      >
                        复制链接
                      </Button>
                    )}
                    {item.status === "pending" && (
                      <Button
                        size="sm"
                        variant="danger-ghost"
                        disabled={action.busy}
                        onClick={() => action.run(() => api(`/invitations/${item.id}/revoke`, "POST"), "邀请已撤销")}
                      >
                        撤销
                      </Button>
                    )}
                  </div>
                ))}
              </div>
              <Pager page={page} total={invitations.data?.total || 0} setPage={setPage} />
            </QueryState>
          </Card>
        </>
      )}

      {tab === "participation" && (
        <QueryState {...stats} retry={stats.reload}>
          <p className="section-desc">只统计正式排班；累计从加入时间截至本周，包含本周未开始的场次。</p>
          <div className="split split-even">
            {[true, false].map((attended) => {
              const rows = stats.data?.items.filter((item) => item.weekly_count > 0 === attended) || [];
              return (
                <Card key={String(attended)} title={attended ? "本周已排班" : "本周未排班"} actions={<span className="card-hint">{rows.length} 人</span>}>
                  {!rows.length && <EmptyState compact>没有成员</EmptyState>}
                  <div className="list">
                    {rows.map((item) => (
                      <div className="list-row list-row-dense" key={item.membership_id}>
                        <Avatar name={item.nickname} id={item.user_id} size="sm" />
                        <strong className="list-main">{item.nickname}</strong>
                        <span className="count">{item.weekly_count} 场</span>
                      </div>
                    ))}
                  </div>
                </Card>
              );
            })}
          </div>
          <Card title="累计排班排名" actions={<span className="card-hint">名次按全百业计算，此处显示当前页</span>}>
            <div className="list">
              {stats.data?.items.map((item) => (
                <div className="list-row list-row-dense ranking-row" key={item.membership_id}>
                  <span className={`rank-number${item.rank <= 3 ? " top-rank" : ""}`}>{String(item.rank).padStart(2, "0")}</span>
                  <Avatar name={item.nickname} id={item.user_id} size="sm" />
                  <strong className="list-main">{item.nickname}</strong>
                  <span className="meter" aria-hidden>
                    <i style={{ width: `${(item.cumulative_count / maxCumulative) * 100}%` }} />
                  </span>
                  <span className="count">{item.cumulative_count} 场</span>
                </div>
              ))}
            </div>
          </Card>
          <Pager page={page} total={stats.data?.total || 0} setPage={setPage} />
        </QueryState>
      )}
    </div>
  );
}

const unavailableReasons: Record<string, string> = {
  ALREADY_IN_GUILD: "你已加入百业，不能再接受邀请。",
  PROFILE_REQUIRED: "需要先创建该区服的游戏角色。",
  INVITATION_EXPIRED: "邀请已过期。",
  INVITATION_REVOKED: "邀请已被撤销。",
  INVITATION_EXHAUSTED: "邀请次数已用完。",
  INVITATION_ALREADY_HANDLED: "邀请已处理。",
};
export function InvitationPage({ path, reload }: { path: string; reload: Reload }) {
  const router = useRouter();
  const query = useQuery<InvitationPreview>(path);
  const action = useAction(async () => {
    query.reload();
    await reload();
  });
  const data = query.data;
  return (
    <QueryState {...query} retry={query.reload}>
      {data && (
        <div className="stack narrow">
          {action.feedback}
          <section className="card hero">
            <Avatar name={data.guild.name} id={data.guild.id} size="xl" square />
            <div>
              <h2>
                {data.guild.name}
                <StatusPill tone={statusTone(data.invitation.status)}>{statusName(data.invitation.status)}</StatusPill>
              </h2>
              <p>
                {serverName(data.guild.server)} · {data.guild.member_count} 位成员
                {data.inviter_nickname && ` · ${data.inviter_nickname} 邀请你加入`}
              </p>
            </div>
          </section>
          <Card title="百业介绍">
            <div className="prose">
              <p>{data.guild.intro || "暂未填写百业简介"}</p>
              <h3>招募要求</h3>
              <p>{data.guild.recruit_requirements || "暂未填写招募要求"}</p>
              <p className="section-desc">有效期至 {date(data.invitation.expires_at)}</p>
            </div>
          </Card>
          {data.unavailable_reason && (
            <p className="notice notice-warn">
              当前无法接受：{unavailableReasons[data.unavailable_reason] || data.unavailable_reason}
            </p>
          )}
          <div className="form-actions">
            {data.can_decline && (
              <Button
                variant="ghost"
                disabled={action.busy}
                onClick={() => action.run(() => api(`${path}/decline`, "POST"), "已拒绝邀请")}
              >
                拒绝邀请
              </Button>
            )}
            <Button
              variant="primary"
              disabled={action.busy || !data.can_accept}
              onClick={() =>
                action.run(async () => {
                  await api(`${path}/accept`, "POST", undefined, idempotency());
                  await reload();
                  router.push(`/guilds/${data.guild.id}`);
                }, "已加入百业")
              }
            >
              接受邀请
            </Button>
          </div>
        </div>
      )}
    </QueryState>
  );
}

function InviteButton({ me, userId, size = "md" }: { me: Me; userId: string; size?: "md" | "sm" }) {
  const action = useAction(() => {});
  if (!me.membership || me.membership.role === "member") return null;
  const guildId = me.membership.guild_id;
  return (
    <>
      {action.feedback}
      <Button
        size={size}
        variant="primary"
        icon={UserPlus}
        disabled={action.busy}
        onClick={() =>
          action.run(
            () => api(`/guilds/${guildId}/invitations`, "POST", { type: "direct", target_user_id: userId }),
            "邀请已发送",
          )
        }
      >
        邀请加入
      </Button>
    </>
  );
}

export function PlayersPage({ id, me }: { id?: string; me: Me }) {
  const [page, setPage] = useState(1);
  const [server, setServer] = useState("");
  const [style, setStyle] = useState("");
  const [weekday, setWeekday] = useState("");
  const [start, setStart] = useState(1140);
  const [end, setEnd] = useState(1440);
  const validTime = !weekday || (start >= 0 && end <= 1440 && start < end);
  const query = useQuery<Page<PlayerSummary>>(
    !id && validTime
      ? `/players?page=${page}${server ? `&server=${server}` : ""}${style ? `&style=${encodeURIComponent(style)}` : ""}${weekday ? `&weekday=${weekday}&start_minute=${start}&end_minute=${end}` : ""}`
      : null,
  );
  const player = useQuery<Player>(id ? `/players/${id}` : null);
  const options = useProfileOptions();
  const styleLabel = options.styleLabel;
  const reset = <T,>(setter: (value: T) => void) => (next: T) => {
    setter(next);
    setPage(1);
  };
  if (id)
    return (
      <QueryState {...player} retry={player.reload}>
        {player.data && (
          <div className="stack narrow">
            <PageActions>
              <InviteButton me={me} userId={id} />
            </PageActions>
            <section className="card hero">
              <Avatar name={player.data.nickname} id={player.data.user_id} size="xl" />
              <div>
                <h2>{player.data.nickname}</h2>
                <p>散人 · 正在寻找队伍</p>
              </div>
            </section>
            {player.data.profiles.map((profile) => (
              <Card
                key={profile.profile_id}
                title={profile.name}
                actions={
                  <span className="power">
                    <Bird size={14} strokeWidth={1.75} aria-hidden />
                    鹅力 {formatPower(profile.power)}
                  </span>
                }
              >
                <p className="section-desc">{serverName(profile.server)}</p>
                <div className="tag-list">
                  {profile.styles.map((code) => (
                    <span key={code}>{styleLabel(code)}</span>
                  ))}
                </div>
                {profile.bio && <p className="tile-text">{profile.bio}</p>}
                <div className="tag-list tag-list-muted">
                  {profile.online_slots.length ? (
                    profile.online_slots.map((slot, index) => (
                      <span key={index}>
                        <Clock3 size={12} strokeWidth={1.75} aria-hidden />周{WEEKDAYS[slot.weekday - 1]} {formatTime(slot.start_minute)}–
                        {formatTime(slot.end_minute)}
                      </span>
                    ))
                  ) : (
                    <span>未填写在线时段</span>
                  )}
                </div>
              </Card>
            ))}
          </div>
        )}
      </QueryState>
    );
  return (
    <div className="stack">
      <div className="toolbar">
        <div className="filters">
          <ServerSelect value={server} onChange={reset(setServer)} />
          <select aria-label="流派" value={style} onChange={(e) => reset(setStyle)(e.target.value)}>
            <option value="">全部流派</option>
            {options.data?.styles.map((item) => (
              <option key={item.code} value={item.code}>
                {item.label}
              </option>
            ))}
          </select>
          <select aria-label="在线日期" value={weekday} onChange={(e) => reset(setWeekday)(e.target.value)}>
            <option value="">不限在线时间</option>
            {WEEKDAYS.map((day, index) => (
              <option key={day} value={index + 1}>
                周{day}在线
              </option>
            ))}
          </select>
          {weekday && (
            <span className="time-range">
              <OnlineTime label="开始时间" hideLabel minutes={start} onChange={reset(setStart)} />
              <span aria-hidden>–</span>
              <OnlineTime label="结束时间" hideLabel end minutes={end} onChange={reset(setEnd)} />
            </span>
          )}
        </div>
        {weekday && <span className="toolbar-hint">北京时间；结束 00:00 表示 24:00</span>}
      </div>
      {!validTime ? (
        <p className="notice notice-warn">开始时间须早于结束时间。</p>
      ) : (
        <QueryState {...query} retry={query.reload}>
          {!query.data?.items.length && (
            <EmptyState icon={Users} title="没有找到符合条件的玩家">
              试试放宽区服、流派或在线时间。
            </EmptyState>
          )}
          <div className="tile-grid">
            {query.data?.items.map((item) => {
              const profile = item.matching_profiles[0] || item.default_profile;
              return (
                <article className="tile" key={item.user_id}>
                  <header className="tile-head">
                    <Avatar name={item.nickname} id={item.user_id} size="lg" />
                    <div>
                      <h2>{item.nickname}</h2>
                      <small>
                        {item.matching_profiles.map((p) => p.name).join(" / ") || "未设置角色"}
                      </small>
                    </div>
                    <StatusPill tone="warn">求组</StatusPill>
                  </header>
                  <p className="power">
                    <Bird size={14} strokeWidth={1.75} aria-hidden />
                    鹅力 <strong>{profile ? formatPower(profile.power) : "未设置"}</strong>
                    {profile && <span>· {serverName(profile.server)}</span>}
                  </p>
                  <div className="tag-list">
                    {profile?.styles.map((code) => (
                      <span key={code}>{styleLabel(code)}</span>
                    ))}
                  </div>
                  <footer className="tile-actions">
                    <ButtonLink size="sm" href={`/players/${item.user_id}`}>
                      查看资料
                    </ButtonLink>
                    <InviteButton me={me} userId={item.user_id} size="sm" />
                  </footer>
                </article>
              );
            })}
          </div>
          <Pager page={page} total={query.data?.total || 0} setPage={setPage} />
        </QueryState>
      )}
    </div>
  );
}
