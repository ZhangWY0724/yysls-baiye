"use client";
import Link from "next/link";
import {
  Clock3,
  Plus,
  ChevronLeft,
  ChevronRight,
  CalendarDays,
  Bell,
  X,
} from "lucide-react";
import { useEffect, useState } from "react";
import { api, idempotency } from "@/lib/api";
import { Me, Member, Page, Profile, Raid, Template } from "@/lib/contracts";
import {
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
  statusName,
  useAction,
  useQuery,
  useDebounced,
  value,
  WEEKDAYS,
} from "./business-ui";

const DAY = 86400000;
const WEEK = 7 * DAY;
const ROLES = ["tank", "healer", "dps"] as const;
// 以北京时间表示的 ISO 字符串，例如 "2026-10-14T20:00:00.000Z" 中的日期与时刻均为北京时间。
const beijingIso = (ms: number) => new Date(ms + 8 * 3600000).toISOString();
// 本周一北京时间 0 点（以 UTC 毫秒数表示的日历日期）。
function thisMonday() {
  const today = Date.parse(beijingIso(Date.now()).slice(0, 10));
  return today - ((new Date(today).getUTCDay() + 6) % 7) * DAY;
}
function weekKey(offset: number) {
  const thursday = new Date(thisMonday() + offset * WEEK + 3 * DAY);
  const year = thursday.getUTCFullYear();
  const week = Math.ceil(((thursday.getTime() - Date.UTC(year, 0, 1)) / DAY + 1) / 7);
  return `${year}-W${String(week).padStart(2, "0")}`;
}
function weekOffset(key: string) {
  const match = /^(\d{4})-W(\d{2})$/.exec(key);
  if (!match) return null;
  const jan4 = Date.UTC(Number(match[1]), 0, 4);
  const monday = jan4 - ((new Date(jan4).getUTCDay() + 6) % 7) * DAY + (Number(match[2]) - 1) * WEEK;
  return Math.round((monday - thisMonday()) / WEEK);
}
const localTime = (iso: string) => beijingIso(Date.parse(iso)).slice(0, 16);
const utcTime = (input: string) => new Date(`${input}:00+08:00`).toISOString();
type Week = {
  week_key: string;
  week_start: string;
  week_end: string;
  events: Raid[];
};
export function RaidsPage({
  id,
  me,
  eventId,
}: {
  id: string;
  me: Me;
  eventId?: string;
}) {
  const [offset, setOffset] = useState(0);
  const [weekPickerOpen, setWeekPickerOpen] = useState(false);
  const [selected, setSelected] = useState<string | null>(eventId || null);
  const [create, setCreate] = useState(false);
  const [createStart, setCreateStart] = useState<string>();
  const [templatesOpen, setTemplatesOpen] = useState(false);
  const [templatePage, setTemplatePage] = useState(1);
  const [templateSearch, setTemplateSearch] = useState("");
  const templateFilter = useDebounced(templateSearch);
  const week = useQuery<Week>(`/guilds/${id}/events?week=${weekKey(offset)}`);
  const templates = useQuery<Page<Template>>(
    create || templatesOpen
      ? `/guilds/${id}/dungeon-templates?page=${templatePage}&q=${encodeURIComponent(templateFilter)}`
      : null,
  );
  const manage = canManage(me, id);
  const action = useAction(() => {
    week.reload();
    templates.reload();
  });
  useEffect(() => {
    if (!selected) return;
    const close = (event: KeyboardEvent) => {
      if (event.key === "Escape") setSelected(null);
    };
    window.addEventListener("keydown", close);
    return () => window.removeEventListener("keydown", close);
  }, [selected]);
  useEffect(() => setSelected(eventId || null), [eventId]);
  return (
    <div className="real-stack">
      {action.feedback}
      <section>
        {manage && (
          <div className="section-heading">
            <span />
            <div className="tile-actions">
              <button
                className="button button-secondary compact"
                onClick={() => setTemplatesOpen(!templatesOpen)}
              >
                副本模板
              </button>
              <button
                className="button button-primary"
                onClick={() => {
                  setCreateStart(undefined);
                  setCreate(true);
                }}
              >
                <Plus size={17} />
                开一场
              </button>
            </div>
          </div>
        )}
        <div className="calendar-toolbar">
          <div className="week-switch">
            <button aria-label="上一周" onClick={() => setOffset(offset - 1)}>
              <ChevronLeft size={17} />
            </button>
            <strong>
              {week.data
                ? `${date(week.data.week_start).split(" ")[0]} — ${date(new Date(new Date(week.data.week_end).getTime() - 1).toISOString()).split(" ")[0]}`
                : weekKey(offset)}
            </strong>
            <div className="week-picker-wrap">
              <button
                className="week-picker-trigger"
                aria-label="快速选择周次"
                aria-expanded={weekPickerOpen}
                onClick={() => setWeekPickerOpen(!weekPickerOpen)}
              >
                <CalendarDays size={17} />
              </button>
              {weekPickerOpen && (
                <div className="week-picker-popover">
                  <label htmlFor="week-jump">跳转到指定周</label>
                  <input
                    id="week-jump"
                    type="week"
                    value={weekKey(offset)}
                    onChange={(event) => {
                      const next = weekOffset(event.target.value);
                      if (next === null) return;
                      setOffset(next);
                      setWeekPickerOpen(false);
                    }}
                  />
                </div>
              )}
            </div>
            <button onClick={() => setOffset(0)}>本周</button>
            <button aria-label="下一周" onClick={() => setOffset(offset + 1)}>
              <ChevronRight size={17} />
            </button>
          </div>
          <span>
            <Clock3 size={15} />
            北京时间 · 自然周
          </span>
        </div>
        <QueryState {...week} retry={week.reload}>
          {!week.data?.events.length && <Empty>这周还没有安排副本</Empty>}
          <div className="calendar-scroll">
            <div className="calendar-grid">
              {Array.from({ length: 7 }, (_, day) => {
                const start = week.data
                  ? Date.parse(week.data.week_start) + day * DAY
                  : 0;
                const rows =
                  week.data?.events.filter(
                    (raid) =>
                      Date.parse(raid.starts_at) >= start &&
                      Date.parse(raid.starts_at) < start + DAY,
                  ) || [];
                return (
                  <section
                    className={`calendar-day${start && beijingIso(start).slice(0, 10) === beijingIso(Date.now()).slice(0, 10) ? " today" : ""}`}
                    key={day}
                  >
                    <header>
                      <strong>周{WEEKDAYS[day]}</strong>
                      <small>{start ? beijingIso(start).slice(5, 10) : ""}</small>
                    </header>
                    <div className="day-events">
                      {rows
                        .sort((a, b) => a.starts_at.localeCompare(b.starts_at))
                        .map((raid) => (
                          <button
                            className="raid-card"
                            key={raid.id}
                            onClick={() => setSelected(raid.id)}
                          >
                            <span className="event-time">
                              <Clock3 size={13} />
                              {new Date(raid.starts_at).toLocaleTimeString(
                                "zh-CN",
                                {
                                  timeZone: "Asia/Shanghai",
                                  hour: "2-digit",
                                  minute: "2-digit",
                                },
                              )}
                              {raid.my_assignment && (
                                <em>{statusName(raid.my_assignment.status)}</em>
                              )}
                            </span>
                            <strong>{raid.title}</strong>
                            <small className="raid-name">
                              {raid.template_name}
                            </small>
                            {ROLES.map(
                              (role) => {
                                const group = raid.confirmed_assignments.filter(
                                  (item) => item.slot_role === role,
                                );
                                return (
                                  <div className="role-row" key={role}>
                                    <span>{roleName(role)}</span>
                                    <span className="avatar-row">
                                      {group.map((item) => (
                                        <i
                                          className="tiny-avatar"
                                          key={item.id}
                                          title={item.profile.name}
                                        >
                                          {item.profile.name.slice(0, 1)}
                                        </i>
                                      ))}
                                      {Array.from(
                                        {
                                          length: Math.max(
                                            0,
                                            raid.capacities[role] -
                                              group.length,
                                          ),
                                        },
                                        (_, index) => (
                                          <i
                                            className="empty-slot"
                                            key={index}
                                          />
                                        ),
                                      )}
                                    </span>
                                  </div>
                                );
                              },
                            )}
                            <span className="event-status-row">
                              <span className="status status-warn">
                                {statusName(raid.status)} ·{" "}
                                {raid.confirmed_assignments.length}/
                                {raid.capacities.tank +
                                  raid.capacities.healer +
                                  raid.capacities.dps}
                              </span>
                              {!!raid.waitlist_assignments.length && (
                                <span className="status status-muted">
                                  候补 {raid.waitlist_assignments.length} 人
                                </span>
                              )}
                            </span>
                          </button>
                        ))}
                      {!rows.length && (
                        <p className="lane-empty">这天没有安排副本</p>
                      )}
                    </div>
                    {manage && (
                      <button
                        className="lane-create"
                        onClick={() => {
                          setCreateStart(beijingIso(start).slice(0, 10) + "T20:00");
                          setCreate(true);
                        }}
                      >
                        <Plus size={14} />
                        开一场
                      </button>
                    )}
                  </section>
                );
              })}
            </div>
          </div>
        </QueryState>
      </section>
      {create && manage && (
        <Modal title="创建副本场次" busy={action.busy} close={() => setCreate(false)}>
            <QueryState {...templates} retry={templates.reload}>
              <EventForm
                key={createStart || "new"}
                defaultStart={createStart}
                templates={templates.data?.items || []}
                busy={action.busy}
                save={(body) =>
                  action.run(async () => {
                    const result = await api<Raid>(
                      `/guilds/${id}/events`,
                      "POST",
                      body,
                      idempotency(),
                    );
                    setCreate(false);
                    setSelected(result.id);
                  })
                }
              />
            </QueryState>
        </Modal>
      )}
      {templatesOpen && manage && <TemplatesPanel id={id} query={templates} />}
      {(create || templatesOpen) && manage && (
        <Card title="查找副本模板">
          <input
            aria-label="搜索副本模板"
            value={templateSearch}
            placeholder="搜索模板名称"
            onChange={(e) => {
              setTemplateSearch(e.target.value);
              setTemplatePage(1);
            }}
          />
          <Pager
            page={templatePage}
            total={templates.data?.total || 0}
            setPage={setTemplatePage}
          />
        </Card>
      )}
      {selected && (
        <div
          className="drawer-backdrop"
          onClick={() => setSelected(null)}
          role="dialog"
          aria-modal="true"
          aria-label="场次详情"
        >
          <div
            className="event-drawer"
            onClick={(event) => event.stopPropagation()}
          >
            <button
              className="icon-button drawer-close"
              aria-label="关闭场次详情"
              onClick={() => {
                setSelected(null);
                week.reload();
              }}
            >
              <X size={19} />
            </button>
            <EventDetail
              id={id}
              eventId={selected}
              me={me}
              changed={week.reload}
            />
          </div>
        </div>
      )}
    </div>
  );
}
function EventForm({
  templates,
  raid,
  defaultStart,
  busy,
  save,
}: {
  templates: Template[];
  raid?: Raid;
  defaultStart?: string;
  busy: boolean;
  save: (body: unknown) => void;
}) {
  const [templateId, setTemplateId] = useState(templates[0]?.id || "");
  const selectedTemplate =
    templates.find((template) => template.id === templateId) || templates[0];
  return (
    <Form
      busy={busy}
      onSubmit={(form) => {
        const capacity = {
          need_tank: Number(value(form, "tank")),
          need_healer: Number(value(form, "healer")),
          need_dps: Number(value(form, "dps")),
        };
        save({
          ...capacity,
          title: value(form, "title") || undefined,
          starts_at: utcTime(value(form, "starts_at")),
          duration_min: Number(value(form, "duration")),
          signup_mode: value(form, "mode"),
          note: value(form, "note"),
          ...(raid
            ? {
                reminder_enabled: form.has("reminder"),
                remind_minutes: Number(value(form, "minutes")),
              }
            : {}),
          ...(raid
            ? { expected_version: raid.version }
            : {
                template_id: value(form, "template"),
                status: value(form, "status"),
              }),
        });
      }}
    >
      {!raid && (
        <label>
          副本模板
          <select
            name="template"
            value={selectedTemplate?.id || ""}
            onChange={(event) => setTemplateId(event.target.value)}
            required
          >
            {templates.map((template) => (
              <option key={template.id} value={template.id}>
                {template.name}
              </option>
            ))}
          </select>
        </label>
      )}
      <Field
        name="title"
        label="场次名称"
        defaultValue={raid?.title}
        required={!!raid}
      />
      <Field
        name="starts_at"
        label="开始时间（北京时间）"
        type="datetime-local"
        defaultValue={raid ? localTime(raid.starts_at) : defaultStart}
        required
      />
      <Field
        key={`duration:${selectedTemplate?.id}`}
        name="duration"
        label="时长（分钟）"
        type="number"
        min={1}
        max={1440}
        defaultValue={
          raid?.duration_min ?? selectedTemplate?.default_duration_min ?? 60
        }
        required
      />
      <div className="real-inline" key={`capacity:${selectedTemplate?.id}`}>
        <Field
          name="tank"
          label="承伤"
          type="number"
          min={0}
          max={10}
          defaultValue={
            raid?.capacities.tank ?? selectedTemplate?.need_tank ?? 0
          }
        />
        <Field
          name="healer"
          label="奶妈"
          type="number"
          min={0}
          max={10}
          defaultValue={
            raid?.capacities.healer ?? selectedTemplate?.need_healer ?? 0
          }
        />
        <Field
          name="dps"
          label="输出"
          type="number"
          min={0}
          max={10}
          defaultValue={raid?.capacities.dps ?? selectedTemplate?.need_dps ?? 0}
        />
      </div>
      <label>
        报名模式
        <select name="mode" defaultValue={raid?.signup_mode || "open"}>
          <option value="open">开放报名</option>
          <option value="assign">仅管理指派</option>
        </select>
      </label>
      {!raid && (
        <label>
          发布状态
          <select name="status">
            <option value="published">立即发布</option>
            <option value="draft">保存草稿</option>
          </select>
        </label>
      )}
      <label>
        备注
        <textarea
          name="note"
          maxLength={2000}
          defaultValue={raid?.note || ""}
        />
      </label>
      {raid ? (
        <>
          <label className="real-checkbox">
            <input
              type="checkbox"
              name="reminder"
              defaultChecked={raid.reminder_enabled}
            />
            启用站内开打提醒
          </label>
          <Field
            name="minutes"
            label="提前分钟"
            type="number"
            min={1}
            max={1440}
            defaultValue={raid.remind_minutes}
          />
        </>
      ) : (
        <p>开打提醒采用百业当前默认设置，创建后可在场次详情调整。</p>
      )}
      {!raid && !templates.length && (
        <p className="real-error">
          暂无可用模板，请先创建百业模板或初始化系统模板。
        </p>
      )}
      <button
        className="button button-primary"
        disabled={!raid && !templates.length}
      >
        {raid ? "保存修改" : "创建场次"}
      </button>
    </Form>
  );
}
export function TemplatesPanel({
  id,
  query,
}: {
  id: string;
  query: ReturnType<typeof useQuery<Page<Template>>>;
}) {
  const [editing, setEditing] = useState<Template | null | undefined>(
    undefined,
  );
  const action = useAction(query.reload);
  return (
    <Card
      title="副本模板"
      actions={<button onClick={() => setEditing(null)}>新增模板</button>}
    >
      {action.feedback}
      <QueryState {...query} retry={query.reload}>
        {query.data?.items.map((template) => (
          <div key={template.id} className="real-row">
            <div>
              <strong>{template.name}</strong>
              <small>
                {template.guild_id ? "百业模板" : "系统模板"} ·{" "}
                {template.need_tank}/{template.need_healer}/{template.need_dps}{" "}
                · {template.default_duration_min} 分钟
              </small>
            </div>
            {template.guild_id && (
              <>
                <button onClick={() => setEditing(template)}>编辑</button>
                <button
                  disabled={action.busy}
                  onClick={() => {
                    if (confirm("归档模板？已有场次不受影响。"))
                      action.run(() =>
                        api(
                          `/guilds/${id}/dungeon-templates/${template.id}`,
                          "DELETE",
                          undefined,
                          { "X-Expected-Version": String(template.version) },
                        ),
                      );
                  }}
                >
                  归档
                </button>
              </>
            )}
          </div>
        ))}
      </QueryState>
      {editing !== undefined && (
        <Form
          key={editing?.id || "new"}
          busy={action.busy}
          onSubmit={(form) =>
            action.run(async () => {
              await api(
                `/guilds/${id}/dungeon-templates${editing ? `/${editing.id}` : ""}`,
                editing ? "PATCH" : "POST",
                {
                  name: value(form, "name"),
                  need_tank: Number(value(form, "tank")),
                  need_healer: Number(value(form, "healer")),
                  need_dps: Number(value(form, "dps")),
                  default_duration_min: Number(value(form, "duration")),
                  ...(editing ? { expected_version: editing.version } : {}),
                },
              );
              setEditing(undefined);
            })
          }
        >
          <Field
            name="name"
            label="模板名称"
            defaultValue={editing?.name}
            required
          />
          <div className="real-inline">
            <Field
              name="tank"
              label="承伤人数"
              type="number"
              min={0}
              max={10}
              defaultValue={editing?.need_tank ?? 1}
            />
            <Field
              name="healer"
              label="奶妈人数"
              type="number"
              min={0}
              max={10}
              defaultValue={editing?.need_healer ?? 2}
            />
            <Field
              name="dps"
              label="输出人数"
              type="number"
              min={0}
              max={10}
              defaultValue={editing?.need_dps ?? 7}
            />
          </div>
          <Field
            name="duration"
            label="默认时长（分钟）"
            type="number"
            min={1}
            max={1440}
            defaultValue={editing?.default_duration_min || 60}
          />
          <div className="real-actions">
            <button className="button button-primary">保存模板</button>
            <button type="button" onClick={() => setEditing(undefined)}>
              取消
            </button>
          </div>
        </Form>
      )}
    </Card>
  );
}
function RoleSelect({ label, value, onChange }: { label: string; value: string; onChange: (role: string) => void }) {
  return (
    <select aria-label={label} value={value} onChange={(e) => onChange(e.target.value)}>
      {ROLES.map((r) => (
        <option key={r} value={r}>
          {roleName(r)}
        </option>
      ))}
    </select>
  );
}
function EventDetail({
  id,
  eventId,
  me,
  changed,
}: {
  id: string;
  eventId: string;
  me: Me;
  changed: () => void;
}) {
  const query = useQuery<Raid>(`/guilds/${id}/events/${eventId}`);
  const profiles = useQuery<{ items: Profile[] }>("/profiles");
  const [memberPage, setMemberPage] = useState(1);
  const [memberSearch, setMemberSearch] = useState("");
  const memberFilter = useDebounced(memberSearch);
  const members = useQuery<Page<Member>>(
    canManage(me, id)
      ? `/guilds/${id}/members?page=${memberPage}&q=${encodeURIComponent(memberFilter)}`
      : null,
  );
  const [profile, setProfile] = useState(me.default_profile?.id || "");
  const [role, setRole] = useState("dps");
  const [assignProfile, setAssignProfile] = useState("");
  const [editing, setEditing] = useState(false);
  const action = useAction(() => {
    query.reload();
    changed();
  });
  const raid = query.data;
  const confirmedIn = (role: string) =>
    raid!.confirmed_assignments.filter((item) => item.slot_role === role);
  const cancelButton = (assignment: Raid["confirmed_assignments"][number], label: string) =>
    !raid!.is_locked &&
    (raid!.can_manage || assignment.user_id === me.user_id) && (
      <button
        disabled={action.busy}
        onClick={() =>
          action.run(() => api(`/events/${eventId}/assignments/${assignment.id}`, "DELETE"))
        }
      >
        {label}
      </button>
    );
  return (
    <QueryState {...query} retry={query.reload}>
      {raid && (
        <section className="event-detail">
          <header>
            <div>
              <span className="kicker">
                {date(raid.starts_at)} · 北京时间
              </span>
              <h2>{raid.title}</h2>
              <p className="drawer-raid-name">
                副本：{raid.template_name}
              </p>
            </div>
          </header>
          {action.feedback}
          <div className="drawer-meta">
            <span className="status status-warn">
              {statusName(raid.status)}
            </span>
            <span>
              <Clock3 size={14} />
              预计 {raid.duration_min} 分钟
            </span>
          </div>
          {raid.note && <p className="muted-copy">{raid.note}</p>}
          <section className="drawer-section">
            <h3>队伍需求</h3>
            <div className="demand-grid">
              {ROLES.map((position) => {
                const used = confirmedIn(position).length;
                const capacity = raid.capacities[position];
                return (
                  <div key={position}>
                    <span>{roleName(position)}</span>
                    <strong>
                      {used}/{capacity}
                    </strong>
                    <small>
                      {used >= capacity ? "已满" : `还缺 ${capacity - used} 人`}
                    </small>
                  </div>
                );
              })}
            </div>
          </section>
          <div>
            {ROLES.map((position) => (
              <section className="drawer-section" key={position}>
                <h3>
                  {roleName(position)} {confirmedIn(position).length}/
                  {raid.capacities[position]}
                </h3>
                <div className="drawer-members">
                  {confirmedIn(position).map((assignment) => (
                      <article className="drawer-member" key={assignment.id}>
                        <span className="app-avatar">
                          {assignment.profile.name.slice(0, 1)}
                        </span>
                        <div>
                          <strong>
                            {assignment.profile.name}
                            <em>{roleName(assignment.slot_role)}</em>
                          </strong>
                          <small>鹅力 {assignment.profile.power}</small>
                          <small>{assignment.profile.styles.join("、")}</small>
                        </div>
                        {cancelButton(assignment, "取消")}
                      </article>
                    ))}
                </div>
              </section>
            ))}
          </div>
          {!!raid.waitlist_assignments.length && (
            <section className="drawer-section">
              <h3>候补队列</h3>
              <div className="waitlist-list">
                {raid.waitlist_assignments.map((assignment) => (
                  <div className="waitlist-person" key={assignment.id}>
                    <span>{assignment.waitlist_position}</span>
                    <span className="app-avatar">
                      {assignment.profile.name.slice(0, 1)}
                    </span>
                    <strong>{assignment.profile.name}</strong>
                    <small>{roleName(assignment.slot_role)}</small>
                    {cancelButton(assignment, "取消候补")}
                  </div>
                ))}
              </div>
            </section>
          )}
          {raid.my_assignment && (
            <p>
              我的安排：{roleName(raid.my_assignment.slot_role)} ·{" "}
              {statusName(raid.my_assignment.status)}
              。更换角色或定位需要先取消再报名。
            </p>
          )}
          {raid.can_signup && (
            <section className="drawer-section">
              <h3>选择角色后报名</h3>
              <label>
                本场角色
                <select
                  aria-label="我的报名角色"
                  value={profile}
                  onChange={(e) => setProfile(e.target.value)}
                >
                  <option value="">选择角色</option>
                  {profiles.data?.items.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                本场定位
                <RoleSelect label="报名定位" value={role} onChange={setRole} />
              </label>
              <p className="muted-copy">
                报名即确认占位；账号同一时间只能参加一场，满员后进入候补。
              </p>
              <button
                disabled={action.busy || !profile}
                className="button button-primary full-button"
                onClick={() =>
                  action.run(() =>
                    api(
                      `/events/${eventId}/assignments`,
                      "POST",
                      {
                        action: "signup",
                        profile_id: profile,
                        slot_role: role,
                      },
                      idempotency(),
                    ),
                  )
                }
              >
                报名（满员则候补）
              </button>
            </section>
          )}
          {raid.can_manage && (
            <>
              <h3>指派成员</h3>
              {raid.status === "draft" && <p>先发布草稿，再指派成员。</p>}
              <input
                aria-label="搜索成员昵称"
                placeholder="搜索成员昵称"
                value={memberSearch}
                onChange={(e) => {
                  setMemberSearch(e.target.value);
                  setMemberPage(1);
                  setAssignProfile("");
                }}
              />
              <QueryState {...members} retry={members.reload}>
                <div className="real-inline">
                  <select
                    aria-label="指派角色"
                    value={assignProfile}
                    onChange={(e) => setAssignProfile(e.target.value)}
                  >
                    <option value="">选择成员角色</option>
                    {members.data?.items.flatMap((member) =>
                      member.profiles.map((p) => (
                        <option key={p.profile_id} value={p.profile_id}>
                          {member.nickname} · {p.name}
                        </option>
                      )),
                    )}
                  </select>
                  <RoleSelect label="指派定位" value={role} onChange={setRole} />
                  <button
                    disabled={
                      action.busy ||
                      !assignProfile ||
                      raid.status !== "published"
                    }
                    onClick={() =>
                      action.run(() =>
                        api(
                          `/events/${eventId}/assignments`,
                          "POST",
                          {
                            action: "assign",
                            profile_id: assignProfile,
                            slot_role: role,
                          },
                          idempotency(),
                        ),
                      )
                    }
                  >
                    指派
                  </button>
                </div>
                <Pager
                  page={memberPage}
                  total={members.data?.total || 0}
                  setPage={(page) => {
                    setMemberPage(page);
                    setAssignProfile("");
                  }}
                />
              </QueryState>
              <div className="real-actions">
                <button onClick={() => setEditing(!editing)}>编辑场次</button>
                {raid.status === "draft" && (
                  <button
                    disabled={action.busy}
                    onClick={() =>
                      action.run(() =>
                        api(`/guilds/${id}/events/${eventId}`, "PATCH", {
                          status: "published",
                          expected_version: raid.version,
                        }),
                      )
                    }
                  >
                    发布草稿
                  </button>
                )}
                <button
                  className="button button-danger"
                  disabled={action.busy}
                  onClick={() => {
                    if (confirm("取消场次？现有排班将保留为只读历史。"))
                      action.run(() =>
                        api(
                          `/guilds/${id}/events/${eventId}`,
                          "DELETE",
                          undefined,
                          { "X-Expected-Version": String(raid.version) },
                        ),
                      );
                  }}
                >
                  取消场次
                </button>
              </div>
              {editing && (
                <EventForm
                  raid={raid}
                  templates={[]}
                  busy={action.busy}
                  save={(body) =>
                    action.run(async () => {
                      await api(
                        `/guilds/${id}/events/${eventId}`,
                        "PATCH",
                        body,
                      );
                      setEditing(false);
                    })
                  }
                />
              )}
            </>
          )}
          {raid.is_locked && <p>场次已开始或已取消，排班与场次只读。</p>}
        </section>
      )}
    </QueryState>
  );
}
export function MyWeekPage({ me, unread }: { me: Me; unread: number }) {
  type MyWeek = {
    week_key: string;
    items: {
      event: Pick<
        Raid,
        "id" | "guild_id" | "title" | "starts_at" | "status" | "version"
      > & { can_cancel: boolean };
      assignment: {
        id: string;
        status: string;
        slot_role: string;
        profile: { name: string };
      };
    }[];
  };
  const query = useQuery<MyWeek>("/me/week");
  const action = useAction(query.reload);
  const raidsHref = me.membership ? `/guilds/${me.membership.guild_id}/raids` : "/guilds";
  return (
    <section>
      <div className="welcome-line">
        <div>
          <h2>{me.nickname}，本周安排</h2>
          <p>本周安排一目了然，报名即确认占位。</p>
        </div>
        <div className="week-switch week-static">
          <strong>{query.data?.week_key}</strong>
          <span>北京时间 · 本周</span>
        </div>
      </div>
      <div className="overview-stats">
        <div>
          <span>本周已报名及候补</span>
          <strong>{query.data?.items.length || 0}</strong>
          <small>场次</small>
        </div>
        <Link
          href={raidsHref}
        >
          <CalendarDays size={20} />
          <strong>查看百业副本</strong>
          <ChevronRight size={16} />
        </Link>
        <Link href="/notifications">
          <Bell size={20} />
          <strong>未读通知</strong>
          <b>{unread} 条</b>
        </Link>
      </div>
      <div className="section-heading">
        <div>
          <h2>我的副本</h2>
          <p>仅显示已报名或候补的场次</p>
        </div>
      </div>
      {action.feedback}
      <QueryState {...query} retry={query.reload}>
        {!query.data?.items.length && (
          <div className="empty-state">
            <CalendarDays size={25} />
            <strong>这周还没有安排</strong>
            <span>去看看有哪些副本在招人</span>
            <Link
              className="button button-secondary"
              href={raidsHref}
            >
              浏览副本
            </Link>
          </div>
        )}
        <div className="my-event-list">
          {query.data?.items.map((item) => (
            <div className="my-event-entry" key={item.assignment.id}>
              <Link
                className="my-event"
                href={`/guilds/${item.event.guild_id}/raids?event=${item.event.id}`}
              >
                <span className="date-box">
                  <strong>
                    {new Date(item.event.starts_at).toLocaleDateString(
                      "zh-CN",
                      {
                        timeZone: "Asia/Shanghai",
                        month: "numeric",
                        day: "numeric",
                      },
                    )}
                  </strong>
                  <small>
                    {new Date(item.event.starts_at).toLocaleDateString(
                      "zh-CN",
                      { timeZone: "Asia/Shanghai", weekday: "short" },
                    )}
                  </small>
                </span>
                <span>
                  <strong>{item.event.title}</strong>
                  <small>
                    {date(item.event.starts_at)} ·{" "}
                    {item.assignment.profile.name} ·{" "}
                    {roleName(item.assignment.slot_role)} ·{" "}
                    {statusName(item.assignment.status)} ·{" "}
                    {statusName(item.event.status)}
                  </small>
                </span>
                <span
                  className={`status status-${item.assignment.status === "waitlist" ? "warn" : "ok"}`}
                >
                  {item.assignment.status === "waitlist" ? "候补中" : "已报名"}
                </span>
                <ChevronRight size={17} />
              </Link>
              {item.event.can_cancel && (
                <button
                  className="text-button danger-text my-event-cancel"
                  disabled={action.busy}
                  onClick={() =>
                    action.run(() =>
                      api(
                        `/events/${item.event.id}/assignments/${item.assignment.id}`,
                        "DELETE",
                      ),
                    )
                  }
                >
                  取消
                </button>
              )}
            </div>
          ))}
        </div>
      </QueryState>
    </section>
  );
}
