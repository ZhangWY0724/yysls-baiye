"use client";
import Link from "next/link";
import { CalendarDays, Search, Users } from "lucide-react";
import { useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { api, idempotency } from "@/lib/api";
import {
  Application,
  Guild,
  Invitation,
  InvitationPreview,
  Me,
  Member,
  Page,
  ProfileSummary,
  Template,
} from "@/lib/contracts";
import {
  APPLICATION_STATUSES,
  canManage,
  Card,
  date,
  Empty,
  Field,
  Form,
  Modal,
  Pager,
  QueryState,
  roleName,
  serverName,
  statusName,
  useAction,
  useQuery,
  useDebounced,
  useProfileOptions,
  value,
  WEEKDAYS,
} from "./business-ui";
import { formatTime, OnlineTime } from "./online-time";
import { TemplatesPanel } from "./raid-pages";

export function GuildList({
  me,
  reload,
}: {
  me: Me;
  reload: () => Promise<void>;
}) {
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
    <div className="real-stack">
      {action.feedback}
      <section>
        <div className="section-heading">
          <span>招募中的百业优先展示</span>
          <button
            className="button button-primary"
            disabled={!!me.membership}
            onClick={() => setCreate(true)}
          >
            创建百业
          </button>
        </div>
        <div className="filter-row">
          <label className="search-box">
            <Search size={17} />
            <input
              placeholder="搜索百业"
              aria-label="搜索百业"
              value={q}
              onChange={(e) => {
                setQ(e.target.value);
                setPage(1);
              }}
            />
          </label>
          <select
            aria-label="区服"
            value={server}
            onChange={(e) => {
              setServer(e.target.value);
              setPage(1);
            }}
          >
            <option value="">全部区服</option>
            <option value="official">官服</option>
            <option value="channel">渠道服</option>
          </select>
        </div>
        <QueryState {...query} retry={query.reload}>
          {!query.data?.items.length && <Empty>暂无百业</Empty>}
          <div className="tile-grid">
            {query.data?.items.map((guild) => (
              <article className="info-tile guild-tile" key={guild.id}>
                <div className="guild-heading">
                  <span className="guild-emblem">{guild.name.slice(0, 1)}</span>
                  <div>
                    <h2>{guild.name}</h2>
                    <small>
                      {serverName(guild.server)} ·{" "}
                      {guild.member_count} 位成员
                    </small>
                  </div>
                  <span
                    className={`status status-${guild.recruiting ? "ok" : "muted"}`}
                  >
                    {guild.recruiting ? "招募中" : "暂停招募"}
                  </span>
                </div>
                <p>{guild.intro || "暂未填写百业简介"}</p>
                <p className="muted-line">
                  <Users size={14} />
                  招募要求：{guild.recruit_requirements || "暂未填写招募要求"}
                </p>
                <div className="tile-actions">
                  <Link
                    className="button button-secondary compact"
                    href={`/guilds/${guild.id}`}
                  >
                    查看百业
                  </Link>
                  {!me.membership && guild.recruiting && (
                    <button
                      className="button button-primary compact"
                      disabled={action.busy || applied.includes(guild.id)}
                      onClick={() =>
                        action.run(async () => {
                          await api(
                            `/guilds/${guild.id}/applications`,
                            "POST",
                            { message: "" },
                          );
                          setApplied((current) => [...current, guild.id]);
                        })
                      }
                    >
                      {applied.includes(guild.id) ? "已申请" : "申请加入"}
                    </button>
                  )}
                </div>
              </article>
            ))}
          </div>
          <Pager page={page} total={query.data?.total || 0} setPage={setPage} />
        </QueryState>
      </section>
      {create && (
        <Modal title="创建百业" busy={action.busy} close={() => setCreate(false)}>
            <Form
              busy={action.busy}
              onSubmit={(form) =>
                action.run(async () => {
                  const guild = await api<Guild>(
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
                })
              }
            >
              <Field name="name" label="百业名称" required />
              <label>
                区服
                <select name="server">
                  <option value="official">官服</option>
                  <option value="channel">渠道服</option>
                </select>
              </label>
              <label>
                简介
                <textarea name="intro" maxLength={2000} />
              </label>
              <label>
                招募要求
                <textarea name="recruit_requirements" maxLength={1000} />
              </label>
              {action.error && (
                <p className="real-error form-submit-feedback">
                  {action.error}
                </p>
              )}
              <button className="button button-primary">
                {action.busy ? "正在创建…" : "创建"}
              </button>
            </Form>
        </Modal>
      )}
    </div>
  );
}
export function GuildHome({
  id,
  me,
  reload,
}: {
  id: string;
  me: Me;
  reload: () => Promise<void>;
}) {
  const router = useRouter();
  const query = useQuery<Guild>(`/guilds/${id}`);
  const action = useAction(async () => {
    query.reload();
    await reload();
  });
  return (
    <QueryState {...query} retry={query.reload}>
      {query.data && (
        <div>
          {action.feedback}
          <section className="guild-hero">
            <span className="guild-emblem big" aria-hidden="true">
              {query.data.name.slice(0, 1)}
            </span>
            <div>
              <h2>
                {query.data.name}　
                <span
                  className={`status status-${query.data.recruiting ? "ok" : "muted"}`}
                >
                  {query.data.recruiting ? "招募中" : "暂停招募"}
                </span>
              </h2>
              <p>
                {serverName(query.data.server)} ·{" "}
                {query.data.member_count} 位成员
              </p>
              <div className="tile-actions">
                <Link
                  className="button button-primary"
                  href={`/guilds/${id}/raids`}
                >
                  <CalendarDays size={16} />
                  本周副本
                </Link>
                {me.membership?.guild_id === id && (
                  <Link
                    className="button button-secondary"
                    href={`/guilds/${id}/members`}
                  >
                    成员管理
                  </Link>
                )}
                {!me.membership && query.data.recruiting && (
                  <button
                    className="button button-secondary"
                    disabled={!!query.data.my_application || action.busy}
                    onClick={() =>
                      action.run(() =>
                        api(`/guilds/${id}/applications`, "POST", {
                          message: "",
                        }),
                      )
                    }
                  >
                    {query.data.my_application ? "已申请" : "申请加入"}
                  </button>
                )}
              </div>
            </div>
          </section>
          <div className="split-panels">
            <section className="detail-panel">
              <h3>百业介绍</h3>
              <p>{query.data.intro || "暂未填写百业简介"}</p>
              <h3>招募要求</h3>
              <p>{query.data.recruit_requirements || "暂未填写招募要求"}</p>
              <h3>近期副本</h3>
              {query.data.recent_events?.length ? (
                query.data.recent_events.map((event) => (
                  <Link
                    className="simple-row"
                    key={event.id}
                    href={`/guilds/${id}/raids?event=${event.id}`}
                  >
                    {date(event.starts_at)}
                    <strong>{event.title}</strong>
                    <span>查看安排</span>
                  </Link>
                ))
              ) : (
                <Empty>暂时没有发布副本安排</Empty>
              )}
            </section>
            <section className="detail-panel">
              <h3>成员资料公开展示</h3>
              {query.data.public_members?.length ? (
                query.data.public_members.map((profile) => (
                  <div className="member-line" key={profile.profile_id}>
                    <span className="app-avatar">
                      {profile.name.slice(0, 1)}
                    </span>
                    <span>
                      <strong>{profile.name}</strong>
                      <small>{profile.styles.join("、")}</small>
                    </span>
                    <b>鹅力 {profile.power}</b>
                  </div>
                ))
              ) : (
                <Empty>暂无成员角色</Empty>
              )}
            </section>
          </div>
          {me.membership?.guild_id === id && (
            <section className="guild-leave-panel">
              <div>
                <h3>
                  {me.membership.role === "owner"
                    ? "退出并解散百业"
                    : "退出百业"}
                </h3>
                <p>
                  {me.membership.role === "owner"
                    ? "社长退出会解散百业，并删除全部关联业务数据。"
                    : "退出后将删除你在本百业中的关联记录。"}
                </p>
              </div>
              <button
                disabled={action.busy}
                className="button button-danger"
                onClick={() => {
                  const owner = me.membership?.role === "owner";
                  if (
                    confirm(
                      owner
                        ? "社长退会即解散整个百业，删除所有百业业务数据。是否继续？"
                        : "退会将删除你在本百业的全部记录；你创建的模板、场次及关联其他人的排班也将删除。是否继续？",
                    ) &&
                    (!owner || confirm("再次确认：解散整个百业，无法恢复？"))
                  )
                    action.run(async () => {
                      await api(`/guilds/${id}/leave`, "POST", {
                        expected_version: query.data!.version,
                        confirm_guild_id: id,
                        ...(owner ? { confirm_dissolve: true } : {}),
                      });
                      await reload();
                      router.push("/guilds");
                    });
                }}
              >
                退出百业
              </button>
            </section>
          )}
        </div>
      )}
    </QueryState>
  );
}
export function GuildSettings({
  id,
  me,
  reload,
}: {
  id: string;
  me: Me;
  reload: () => Promise<void>;
}) {
  const router = useRouter();
  const [tab, setTab] = useState("info");
  const [templatePage, setTemplatePage] = useState(1);
  const templates = useQuery<Page<Template>>(
    tab === "templates"
      ? `/guilds/${id}/dungeon-templates?page=${templatePage}`
      : null,
  );
  const guild = useQuery<Guild>(`/guilds/${id}`);
  const reminders = useQuery<{
    reminder_enabled: boolean;
    remind_minutes: number;
    version: number;
  }>(tab === "reminders" ? `/guilds/${id}/reminder-settings` : null);
  const action = useAction(async () => {
    guild.reload();
    reminders.reload();
    await reload();
  });
  if (!canManage(me, id)) return <Empty>仅社长或管理员可修改百业设置。</Empty>;
  return (
    <div className="narrow-area">
      <div className="settings-tabs" role="tablist" aria-label="百业设置">
        {[
          ["info", "百业资料"],
          ["templates", "副本模板"],
          ["reminders", "提醒配置"],
        ].map(([key, label]) => (
          <button
            key={key}
            role="tab"
            aria-selected={tab === key}
            className={tab === key ? "selected" : ""}
            onClick={() => setTab(key)}
          >
            {label}
          </button>
        ))}
      </div>
      {action.feedback}
      {tab === "info" && (
        <QueryState {...guild} retry={guild.reload}>
          {guild.data && (
            <section className="detail-panel settings-form">
              <h2>百业资料</h2>
              <Form
                key={guild.data.version}
                busy={action.busy}
                onSubmit={(form) =>
                  action.run(() =>
                    api(`/guilds/${id}`, "PATCH", {
                      name: value(form, "name"),
                      intro: value(form, "intro"),
                      recruit_requirements: value(form, "recruit_requirements"),
                      recruiting: form.has("recruiting"),
                      expected_version: guild.data!.version,
                    }),
                  )
                }
              >
                <Field
                  name="name"
                  label="百业名称"
                  defaultValue={guild.data.name}
                  required
                />
                <label>
                  简介
                  <textarea
                    name="intro"
                    defaultValue={guild.data.intro}
                    maxLength={2000}
                  />
                </label>
                <label>
                  招募要求
                  <textarea
                    name="recruit_requirements"
                    defaultValue={guild.data.recruit_requirements}
                    maxLength={1000}
                  />
                </label>
                <label className="real-checkbox">
                  <input
                    type="checkbox"
                    name="recruiting"
                    defaultChecked={guild.data.recruiting}
                  />
                  开放招募
                </label>
                <button className="button button-primary">保存资料</button>
              </Form>
            </section>
          )}
        </QueryState>
      )}
      {tab === "templates" && (
        <>
          <TemplatesPanel id={id} query={templates} />
          <Pager
            page={templatePage}
            total={templates.data?.total || 0}
            setPage={setTemplatePage}
          />
        </>
      )}
      {tab === "reminders" && (
        <QueryState {...reminders} retry={reminders.reload}>
          {reminders.data && (
            <section className="detail-panel settings-form">
              <h2>提醒配置</h2>
              <Form
                key={reminders.data.version}
                busy={action.busy}
                onSubmit={(form) =>
                  action.run(() =>
                    api(`/guilds/${id}/reminder-settings`, "PUT", {
                      reminder_enabled: form.has("enabled"),
                      remind_minutes: Number(value(form, "minutes")),
                      expected_version: reminders.data!.version,
                    }),
                  )
                }
              >
                <label className="real-checkbox">
                  <input
                    type="checkbox"
                    name="enabled"
                    defaultChecked={reminders.data.reminder_enabled}
                  />
                  新场次默认启用开打提醒
                </label>
                <Field
                  name="minutes"
                  label="提前分钟数"
                  type="number"
                  min={1}
                  max={1440}
                  defaultValue={reminders.data.remind_minutes}
                />
                <small>仅影响新建场次；已有场次在详情中独立调整。</small>
                <button className="button button-primary">保存提醒</button>
              </Form>
            </section>
          )}
        </QueryState>
      )}
      {tab === "info" && me.membership?.role === "owner" && (
        <Card title="解散百业">
          <p className="real-error">
            解散删除全体成员在本百业的全部业务数据，账号与个人角色保留。
          </p>
          <button
            disabled={action.busy || !guild.data}
            className="button button-danger"
            onClick={() => {
              if (
                confirm("确定解散百业并永久删除全部关联业务数据？") &&
                confirm("最后确认：此操作无法恢复。")
              )
                action.run(async () => {
                  await api(`/guilds/${id}`, "DELETE", undefined, {
                    "X-Expected-Version": String(guild.data!.version),
                    "X-Confirm-Guild-Id": id,
                  });
                  await reload();
                  router.push("/guilds");
                });
            }}
          >
            永久解散百业
          </button>
        </Card>
      )}
    </div>
  );
}
type Participation = Page<{
  membership_id: string;
  user_id: string;
  nickname: string;
  weekly_count: number;
  cumulative_count: number;
  rank: number;
}> & { week_key: string };
export function MembersPage({
  id,
  me,
  reload,
}: {
  id: string;
  me: Me;
  reload: () => Promise<void>;
}) {
  const params = useSearchParams();
  const [tab, setTab] = useState(
    params.get("tab") === "applications" && me.membership?.role !== "member"
      ? "applications"
      : "members",
  );
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const memberSearch = useDebounced(search);
  const [status, setStatus] = useState("pending");
  const [unassigned, setUnassigned] = useState(false);
  const members = useQuery<Page<Member>>(
    tab === "members"
      ? `/guilds/${id}/members?page=${page}&q=${encodeURIComponent(memberSearch)}`
      : null,
  );
  const applications = useQuery<Page<Application>>(
    tab === "applications"
      ? `/guilds/${id}/applications?page=${page}&status=${status}`
      : null,
  );
  const invitations = useQuery<Page<Invitation>>(
    tab === "invitations" ? `/guilds/${id}/invitations?page=${page}` : null,
  );
  const stats = useQuery<Participation>(
    tab === "participation"
      ? `/guilds/${id}/participation?page=${page}&unassigned_only=${unassigned}`
      : null,
  );
  const [link, setLink] = useState("");
  const action = useAction(async () => {
    members.reload();
    applications.reload();
    invitations.reload();
    stats.reload();
    await reload();
  });
  const manage = canManage(me, id);
  const version = me.membership?.guild_version || 0;
  useEffect(() => {
    if (params.get("tab") === "applications" && manage) {
      setTab("applications");
      setPage(1);
    }
  }, [params, manage]);
  return (
    <section>
      <div className="section-heading">
        <div>
          <h2>成员管理</h2>
          <p>审批入会申请、管理成员资料并查看周本参与情况</p>
        </div>
      </div>
      {action.feedback}
      <div
        className="settings-tabs member-tabs"
        role="tablist"
        aria-label="成员管理视图"
      >
        {[
          "members",
          ...(manage ? ["applications", "invitations", "participation"] : []),
        ].map((key) => (
          <button
            role="tab"
            aria-selected={tab === key}
            className={tab === key ? "selected" : ""}
            key={key}
            onClick={() => {
              setTab(key);
              setPage(1);
            }}
          >
            {
              {
                members: "成员",
                applications: "审批申请",
                invitations: "邀请管理",
                participation: "参与情况",
              }[key]
            }
          </button>
        ))}
      </div>
      {tab === "members" && (
        <div className="member-tools">
          <input
            aria-label="搜索成员"
            placeholder="搜索成员名称"
            value={search}
            onChange={(event) => {
              setSearch(event.target.value);
              setPage(1);
            }}
          />
        </div>
      )}
      {tab === "members" && (
        <QueryState {...members} retry={members.reload}>
          {members.data?.items.map((member) => (
            <div className="member-line" key={member.membership_id}>
              <span className="app-avatar">{member.nickname.slice(0, 1)}</span>
              <span>
                <strong>{member.nickname}</strong>
                <small>
                  {roleName(member.role)} ·{" "}
                  {member.profiles.map((profile) => profile.name).join(" / ") ||
                    "未设置角色"}
                </small>
              </span>
              {manage &&
                member.user_id !== me.user_id &&
                member.role !== "owner" && (
                  <div className="real-actions">
                    {me.membership?.role === "owner" && (
                      <>
                        <button
                          disabled={action.busy}
                          onClick={() =>
                            action.run(() =>
                              api(
                                `/guilds/${id}/members/${member.membership_id}/role`,
                                "PATCH",
                                {
                                  role:
                                    member.role === "admin"
                                      ? "member"
                                      : "admin",
                                  expected_version: version,
                                },
                              ),
                            )
                          }
                        >
                          {member.role === "admin"
                            ? "取消管理员"
                            : "设为管理员"}
                        </button>
                        <button
                          disabled={action.busy}
                          onClick={() => {
                            if (
                              confirm(
                                `将社长转让给 ${member.nickname}？你将成为普通成员。`,
                              )
                            )
                              action.run(() =>
                                api(`/guilds/${id}/owner-transfer`, "POST", {
                                  target_membership_id: member.membership_id,
                                  previous_owner_role: "member",
                                  expected_version: version,
                                }),
                              );
                          }}
                        >
                          转让社长
                        </button>
                      </>
                    )}
                    {(me.membership?.role === "owner" ||
                      member.role === "member") && (
                      <button
                        disabled={action.busy}
                        onClick={() => {
                          if (
                            confirm(
                              "移出成员将删除其在百业中的全部记录，以及其创建的模板/场次和关联排班，确定继续？",
                            )
                          )
                            action.run(() =>
                              api(
                                `/guilds/${id}/members/${member.membership_id}`,
                                "DELETE",
                                undefined,
                                {
                                  "X-Expected-Version": String(version),
                                  "X-Confirm-Membership-Id":
                                    member.membership_id,
                                },
                              ),
                            );
                        }}
                      >
                        移出
                      </button>
                    )}
                  </div>
                )}
            </div>
          ))}
          <Pager
            page={page}
            total={members.data?.total || 0}
            setPage={setPage}
          />
        </QueryState>
      )}
      {tab === "applications" && (
        <>
          <select
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
          <QueryState {...applications} retry={applications.reload}>
            {!applications.data?.items.length && <Empty />}
            {applications.data?.items.map((item) => (
              <div className="member-line request-line" key={item.id}>
                <span className="app-avatar">
                  {(item.applicant?.name || "玩家").slice(0, 1)}
                </span>
                <span>
                  <strong>{item.applicant?.name || "玩家"}</strong>
                  <p>{item.message}</p>
                  <small>{statusName(item.status)}</small>
                </span>
                {item.status === "pending" && (
                  <div className="real-actions">
                    {["approved", "rejected"].map((decision) => (
                      <button
                        disabled={action.busy}
                        key={decision}
                        onClick={() =>
                          action.run(() =>
                            api(
                              `/applications/${item.id}`,
                              "PATCH",
                              { decision, expected_status: "pending" },
                              decision === "approved"
                                ? idempotency()
                                : undefined,
                            ),
                          )
                        }
                      >
                        {decision === "approved" ? "通过" : "拒绝"}
                      </button>
                    ))}
                  </div>
                )}
              </div>
            ))}
            <Pager
              page={page}
              total={applications.data?.total || 0}
              setPage={setPage}
            />
          </QueryState>
        </>
      )}
      {tab === "invitations" && (
        <>
          <button
            disabled={action.busy}
            className="button button-primary"
            onClick={() =>
              action.run(async () => {
                const result = await api<Invitation>(
                  `/guilds/${id}/invitations`,
                  "POST",
                  { type: "code", max_uses: 20 },
                );
                setLink(
                  new URL(result.invite_url!, location.origin).toString(),
                );
              }, "邀请链接已生成，请立即复制保存")
            }
          >
            生成邀请链接
          </button>
          {link && (
            <div className="real-inline">
              <input aria-label="邀请链接" readOnly value={link} />
              <button onClick={() => navigator.clipboard.writeText(link)}>
                复制
              </button>
            </div>
          )}
          <QueryState {...invitations} retry={invitations.reload}>
            {!invitations.data?.items.length && <Empty />}
            {invitations.data?.items.map((item) => (
              <div className="real-row" key={item.id}>
                <div>
                  <strong>
                    {item.type === "code" ? "邀请码" : "定向邀请"}
                  </strong>
                  <small>
                    {statusName(item.status)} · {item.used_count}/
                    {item.max_uses} · {date(item.expires_at)}
                  </small>
                </div>
                {item.status === "pending" && (
                  <button
                    disabled={action.busy}
                    onClick={() =>
                      action.run(() =>
                        api(`/invitations/${item.id}/revoke`, "POST"),
                      )
                    }
                  >
                    撤销
                  </button>
                )}
              </div>
            ))}
            <Pager
              page={page}
              total={invitations.data?.total || 0}
              setPage={setPage}
            />
          </QueryState>
        </>
      )}
      {tab === "participation" && (
        <>
          <p>仅统计正式排班，累计截至当前周，包含本周未开始场次。</p>
          <label className="real-checkbox">
            <input
              type="checkbox"
              checked={unassigned}
              onChange={(e) => {
                setUnassigned(e.target.checked);
                setPage(1);
              }}
            />
            只看本周未排班
          </label>
          <QueryState {...stats} retry={stats.reload}>
            <div className="attendance-columns">
              {[true, false].map((attended) => (
                <section
                  className="detail-panel attendance-panel"
                  key={String(attended)}
                >
                  <div className="attendance-heading">
                    <h3>{attended ? "本周已参加" : "本周未参加"}</h3>
                  </div>
                  {stats.data?.items
                    .filter((item) => item.weekly_count > 0 === attended)
                    .map((item) => (
                      <div className="attendance-row" key={item.membership_id}>
                        <span className="app-avatar">
                          {item.nickname.slice(0, 1)}
                        </span>
                        <strong>{item.nickname}</strong>
                        <span>{item.weekly_count} 场</span>
                      </div>
                    ))}
                </section>
              ))}
            </div>
            <section className="detail-panel ranking-panel">
              <div className="attendance-heading">
                <div>
                  <h3>累计参与排名</h3>
                  <p>累计截至当前周，包含本周已排班场次；展示当前分页成员。</p>
                </div>
              </div>
              <div className="ranking-list">
                {stats.data?.items.map((item) => (
                  <div className="ranking-row" key={item.membership_id}>
                    <span
                      className={`rank-number ${item.rank <= 3 ? "top-rank" : ""}`}
                    >
                      {String(item.rank).padStart(2, "0")}
                    </span>
                    <span className="app-avatar">
                      {item.nickname.slice(0, 1)}
                    </span>
                    <strong>{item.nickname}</strong>
                    <div className="rank-meter">
                      <i
                        style={{
                          width: `${Math.max(0, (item.cumulative_count / Math.max(1, ...stats.data!.items.map((member) => member.cumulative_count))) * 100)}%`,
                        }}
                      />
                    </div>
                    <b>
                      {item.cumulative_count}
                      <small> 场</small>
                    </b>
                  </div>
                ))}
              </div>
            </section>
            <Pager
              page={page}
              total={stats.data?.total || 0}
              setPage={setPage}
            />
          </QueryState>
        </>
      )}
    </section>
  );
}
export function InvitationPage({
  path,
  reload,
}: {
  path: string;
  reload: () => Promise<void>;
}) {
  const router = useRouter();
  const query = useQuery<InvitationPreview>(path);
  const action = useAction(async () => {
    query.reload();
    await reload();
  });
  return (
    <QueryState {...query} retry={query.reload}>
      {query.data && (
        <Card title="百业邀请">
          {action.feedback}
          <h3>{query.data.guild.name}</h3>
          <p>{query.data.guild.intro}</p>
          <p>{query.data.guild.recruit_requirements}</p>
          <small>
            {statusName(query.data.invitation.status)} ·{" "}
            {date(query.data.invitation.expires_at)}
          </small>
          {query.data.unavailable_reason && (
            <p>当前无法接受：{query.data.unavailable_reason}</p>
          )}
          <div className="real-actions">
            <button
              disabled={action.busy || !query.data.can_accept}
              className="button button-primary"
              onClick={() =>
                action.run(async () => {
                  await api(`${path}/accept`, "POST", undefined, idempotency());
                  await reload();
                  router.push(`/guilds/${query.data!.guild.id}`);
                })
              }
            >
              接受邀请
            </button>
            {query.data.can_decline && (
              <button
                disabled={action.busy}
                onClick={() => action.run(() => api(`${path}/decline`, "POST"))}
              >
                拒绝邀请
              </button>
            )}
          </div>
        </Card>
      )}
    </QueryState>
  );
}
type Player = {
  user_id: string;
  nickname: string;
  default_profile: ProfileSummary | null;
  matching_profiles: ProfileSummary[];
  profiles?: (ProfileSummary & {
    bio: string;
    online_slots: {
      weekday: number;
      start_minute: number;
      end_minute: number;
    }[];
  })[];
};
function InviteButton({ me, userId, compact }: { me: Me; userId: string; compact?: boolean }) {
  const action = useAction(() => {});
  if (!me.membership || me.membership.role === "member") return null;
  const guildId = me.membership.guild_id;
  return (
    <>
      {action.feedback}
      <button
        className={`button button-primary${compact ? " compact" : ""}`}
        disabled={action.busy}
        onClick={() =>
          action.run(() =>
            api(`/guilds/${guildId}/invitations`, "POST", { type: "direct", target_user_id: userId }),
          )
        }
      >
        邀请加入百业
      </button>
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
  const query = useQuery<Page<Player>>(
    !id && validTime
      ? `/players?page=${page}${server ? `&server=${server}` : ""}${style ? `&style=${encodeURIComponent(style)}` : ""}${weekday ? `&weekday=${weekday}&start_minute=${start}&end_minute=${end}` : ""}`
      : null,
  );
  const player = useQuery<Player>(id ? `/players/${id}` : null);
  const options = useProfileOptions();
  const styleLabel = options.styleLabel;
  if (id)
    return (
      <QueryState {...player} retry={player.reload}>
        {player.data && (
          <Card title={player.data.nickname}>
            {player.data.profiles?.map((profile) => (
              <section key={profile.profile_id}>
                <div className="real-row">
                  <strong>{profile.name}</strong>
                  <span>
                    {serverName(profile.server)} · 鹅力 {profile.power} ·{" "}
                    {profile.styles.map(styleLabel).join(" / ")}
                  </span>
                </div>
                <p>{profile.bio}</p>
                <small>
                  {profile.online_slots.length
                    ? profile.online_slots
                        .map(
                          (slot) =>
                            `周${WEEKDAYS[slot.weekday - 1]} ${formatTime(slot.start_minute)}–${formatTime(slot.end_minute)}`,
                        )
                        .join("；")
                    : "未填写在线时段"}
                </small>
              </section>
            ))}
            <InviteButton me={me} userId={id} />
          </Card>
        )}
      </QueryState>
    );
  return (
    <Card title="散人玩家">
      <div className="real-inline">
        <select
          aria-label="区服"
          value={server}
          onChange={(e) => {
            setServer(e.target.value);
            setPage(1);
          }}
        >
          <option value="">全部区服</option>
          <option value="official">官服</option>
          <option value="channel">渠道服</option>
        </select>
        <select
          aria-label="流派"
          value={style}
          onChange={(e) => {
            setStyle(e.target.value);
            setPage(1);
          }}
        >
          <option value="">全部流派</option>
          {options.data?.styles.map((item) => (
            <option key={item.code} value={item.code}>
              {item.label}
            </option>
          ))}
        </select>
      </div>
      <div className="real-inline">
        <label>
          在线日期
          <select
            value={weekday}
            onChange={(e) => {
              setWeekday(e.target.value);
              setPage(1);
            }}
          >
            <option value="">不限在线时间</option>
            {WEEKDAYS.map((day, index) => (
              <option key={day} value={index + 1}>
                周{day}
              </option>
            ))}
          </select>
        </label>
        {weekday && (
          <>
            <OnlineTime
              label="开始时间"
              minutes={start}
              onChange={(minutes) => {
                setStart(minutes);
                setPage(1);
              }}
            />
            <OnlineTime
              label="结束时间"
              end
              minutes={end}
              onChange={(minutes) => {
                setEnd(minutes);
                setPage(1);
              }}
            />
            <small>
              {formatTime(start)}–{formatTime(end)}（北京时间；结束 00:00 表示 24:00）
            </small>
          </>
        )}
      </div>
      {!validTime ? (
        <p className="real-error">
          开始时间须早于结束时间，分钟范围为 0–1440。
        </p>
      ) : (
        <QueryState {...query} retry={query.reload}>
          {!query.data?.items.length && <Empty>暂无符合条件的散人玩家</Empty>}
          <div className="tile-grid">
            {query.data?.items.map((item) => {
              const profile = item.matching_profiles[0] || item.default_profile;
              return (
                <article className="info-tile" key={item.user_id}>
                  <div className="person-head">
                    <span className="app-avatar">
                      {item.nickname.slice(0, 1)}
                    </span>
                    <div>
                      <strong>{item.nickname}</strong>
                      <small>散人 · 正在寻找队伍</small>
                    </div>
                    <span className="status status-warn">求组</span>
                  </div>
                  <p className="power-line">
                    鹅力 <strong>{profile?.power ?? "未设置"}</strong>
                  </p>
                  <div className="tag-list">
                    {profile?.styles.map((code) => (
                      <span key={code}>{styleLabel(code)}</span>
                    ))}
                  </div>
                  <p className="muted-line">
                    角色：
                    {item.matching_profiles
                      .map((profile) => profile.name)
                      .join(" / ") || "未设置角色"}
                  </p>
                  <div className="tile-actions">
                    <Link
                      className="button button-secondary compact"
                      href={`/players/${item.user_id}`}
                    >
                      查看资料
                    </Link>
                    <InviteButton me={me} userId={item.user_id} compact />
                  </div>
                </article>
              );
            })}
          </div>
          <Pager page={page} total={query.data?.total || 0} setPage={setPage} />
        </QueryState>
      )}
    </Card>
  );
}
