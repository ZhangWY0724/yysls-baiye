"use client";
import Link from "next/link";
import {
  Bell,
  CalendarDays,
  ChevronLeft,
  ChevronRight,
  Clock3,
  Layers,
  Pencil,
  Plus,
  Send,
  UserPlus,
  Leaf,
  Shield,
  Sword,
  Trash2,
} from "lucide-react";
import { useState } from "react";
import { api, idempotency } from "@/lib/api";
import { Assignment, Me, Member, MyWeek, Page, Profile, Raid, RaidWeek, Template } from "@/lib/contracts";
import {
  canManage,
  Card,
  date,
  Form,
  QueryState,
  roleName,
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
import {
  Avatar,
  Button,
  ButtonLink,
  Drawer,
  EmptyState,
  Field,
  IconButton,
  Modal,
  PageActions,
  Pager,
  StatusPill,
  Switch,
  useConfirm,
  type Tone,
} from "./ui";

const DAY = 86400000;
const WEEK = 7 * DAY;
const ROLES = ["tank", "healer", "dps"] as const;
// 十个固定座位：前几个为承伤，随后为奶妈，其余为输出；展示顺序保证左列 1–5 号、右列 6–10 号。
const SEAT_ORDER = [1, 6, 2, 7, 3, 8, 4, 9, 5, 10];
const ROLE_ICON = { tank: Shield, healer: Leaf, dps: Sword };
const ROLE_COLOR = { tank: "var(--accent)", healer: "var(--ok)", dps: "var(--bad)" };
const seatTotal = (raid: Raid) => raid.capacities.tank + raid.capacities.healer + raid.capacities.dps;
const seatRole = (raid: Raid, index: number): (typeof ROLES)[number] =>
  index <= raid.capacities.tank ? "tank" : index <= raid.capacities.tank + raid.capacities.healer ? "healer" : "dps";
const seatsOf = (raid: Raid) => SEAT_ORDER.filter((index) => index <= seatTotal(raid));
function RoleIcon({ role, size = 14 }: { role: (typeof ROLES)[number]; size?: number }) {
  const Icon = ROLE_ICON[role];
  return <Icon size={size} strokeWidth={1.75} color={ROLE_COLOR[role]} style={{ flexShrink: 0 }} aria-label={roleName(role)} />;
}
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
const timeOf = (iso: string) =>
  new Date(iso).toLocaleTimeString("zh-CN", { timeZone: "Asia/Shanghai", hour: "2-digit", minute: "2-digit" });

// 场次状态胶囊：未发布/已结束/已取消显示状态，进行中的场次按缺口显示满员或缺人（缺承伤、奶妈优先提示）。
function raidPill(raid: Raid): [Tone, string] {
  if (raid.status !== "published") return [statusTone(raid.status), statusName(raid.status)];
  const missing = (role: (typeof ROLES)[number]) =>
    raid.capacities[role] - raid.confirmed_assignments.filter((item) => item.slot_role === role).length;
  if (missing("tank") > 0) return ["bad", "缺承伤"];
  if (missing("healer") > 0) return ["bad", "缺奶妈"];
  const total = ROLES.reduce((sum, role) => sum + Math.max(0, missing(role)), 0);
  return total > 0 ? ["warn", `缺 ${total} 人`] : ["ok", "满员"];
}

function WeekSwitcher({ offset, setOffset, range }: { offset: number; setOffset: (offset: number) => void; range: string }) {
  const [open, setOpen] = useState(false);
  return (
    <div className="week-switch">
      <IconButton label="上一周" icon={ChevronLeft} size="sm" onClick={() => setOffset(offset - 1)} />
      <strong>{range}</strong>
      <IconButton label="下一周" icon={ChevronRight} size="sm" onClick={() => setOffset(offset + 1)} />
      <span className="week-switch-divider" aria-hidden />
      <Button size="sm" variant="ghost" disabled={offset === 0} onClick={() => setOffset(0)}>
        本周
      </Button>
      <div className="week-picker">
        <IconButton label="跳转到指定周" icon={CalendarDays} size="sm" aria-expanded={open} onClick={() => setOpen(!open)} />
        {open && (
          <div className="popover">
            <Field label="跳转到指定周">
              <input
                type="week"
                value={weekKey(offset)}
                onChange={(event) => {
                  const next = weekOffset(event.target.value);
                  if (next === null) return;
                  setOffset(next);
                  setOpen(false);
                }}
              />
            </Field>
          </div>
        )}
      </div>
    </div>
  );
}

function RaidCard({ raid, open }: { raid: Raid; open: () => void }) {
  const [tone, label] = raidPill(raid);
  const capacity = raid.capacities.tank + raid.capacities.healer + raid.capacities.dps;
  return (
    <button type="button" className="raid-card" onClick={open}>
      <span className="raid-card-time">
        <Clock3 size={14} strokeWidth={1.75} aria-hidden />
        {timeOf(raid.starts_at)}
        {raid.my_assignment && (
          <StatusPill tone={statusTone(raid.my_assignment.status)}>
            {raid.my_assignment.status === "waitlist" ? "已候补" : "已报名"}
          </StatusPill>
        )}
      </span>
      <strong>{raid.title}</strong>
      <small>{raid.template_name}</small>
      <span className="member-grid">
        {seatsOf(raid).map((index) => {
          const holder = raid.confirmed_assignments.find((item) => item.slot_index === index);
          return (
            <span className="raid-card-time" key={index}>
              <RoleIcon role={seatRole(raid, index)} />
              {holder ? <span style={{ color: "var(--text)" }}>{holder.profile.name}</span> : <i className="slot-empty" />}
            </span>
          );
        })}
      </span>
      <span className="raid-card-foot">
        <StatusPill tone={tone}>{label}</StatusPill>
        <span>
          {raid.confirmed_assignments.length}/{capacity}
          {!!raid.waitlist_assignments.length && ` · 候补 ${raid.waitlist_assignments.length}`}
        </span>
      </span>
    </button>
  );
}

export function RaidsPage({ id, me, eventId }: { id: string; me: Me; eventId?: string }) {
  const [offset, setOffset] = useState(0);
  const [selected, setSelected] = useState<string | null>(eventId || null);
  const [create, setCreate] = useState(false);
  const [createStart, setCreateStart] = useState<string>();
  const [templatesOpen, setTemplatesOpen] = useState(false);
  const [lastEventId, setLastEventId] = useState(eventId);
  if (eventId !== lastEventId) {
    setLastEventId(eventId);
    setSelected(eventId || null);
  }
  const week = useQuery<RaidWeek>(`/guilds/${id}/events?week=${weekKey(offset)}`);
  const templates = useQuery<Page<Template>>(create ? `/guilds/${id}/dungeon-templates?page=1&page_size=100` : null);
  const manage = canManage(me, id);
  const action = useAction(week.reload);
  const today = beijingIso(Date.now()).slice(0, 10);
  const canCreateThisWeek = week.data ? Date.parse(week.data.week_end) > Date.now() : offset >= 0;
  const range = week.data
    ? `${date(week.data.week_start).split(" ")[0]} – ${date(new Date(Date.parse(week.data.week_end) - 1).toISOString()).split(" ")[0].slice(5)}`
    : weekKey(offset);
  const openCreate = (start?: string) => {
    setCreateStart(start);
    setCreate(true);
  };
  return (
    <div className="stack">
      {action.feedback}
      {manage && (
        <PageActions>
          <Button icon={Layers} onClick={() => setTemplatesOpen(true)}>
            副本模板
          </Button>
          <Button variant="primary" icon={Plus} disabled={!canCreateThisWeek} onClick={() => openCreate()}>
            开一场
          </Button>
        </PageActions>
      )}
      <div className="toolbar">
        <WeekSwitcher offset={offset} setOffset={setOffset} range={range} />
        <span className="toolbar-hint">
          <Clock3 size={14} strokeWidth={1.75} aria-hidden />
          北京时间 · 自然周
        </span>
      </div>
      <QueryState {...week} retry={week.reload}>
        {!week.data?.events.length && (
          <p className="notice">
            {manage ? "这周还没有安排副本，来开一场吧。" : "这周还没有安排副本，管理员排好后会通知你。"}
          </p>
        )}
        <div className="calendar-scroll">
          <div className="calendar-grid">
            {Array.from({ length: 7 }, (_, day) => {
              const start = week.data ? Date.parse(week.data.week_start) + day * DAY : 0;
              const rows = (week.data?.events || [])
                .filter((raid) => Date.parse(raid.starts_at) >= start && Date.parse(raid.starts_at) < start + DAY)
                .sort((a, b) => a.starts_at.localeCompare(b.starts_at));
              return (
                <section
                  className={`calendar-day${start && beijingIso(start).slice(0, 10) === today ? " today" : ""}`}
                  key={day}
                >
                  <header>
                    <strong>周{WEEKDAYS[day]}</strong>
                    <small>{start ? beijingIso(start).slice(5, 10) : ""}</small>
                  </header>
                  <div className="day-events">
                    {rows.map((raid) => (
                      <RaidCard key={raid.id} raid={raid} open={() => setSelected(raid.id)} />
                    ))}
                    {!rows.length && <p className="lane-empty">这天没有安排副本</p>}
                  </div>
                  {manage && start + DAY > Date.now() && (
                    <Button
                      size="sm"
                      variant="ghost"
                      icon={Plus}
                      block
                      onClick={() => {
                        const preset = beijingIso(start).slice(0, 10) + "T20:00";
                        const now = localTime(new Date(Math.ceil(Date.now() / 60000) * 60000).toISOString());
                        openCreate(preset < now ? now : preset);
                      }}
                    >
                      开一场
                    </Button>
                  )}
                </section>
              );
            })}
          </div>
        </div>
      </QueryState>
      {create && manage && (
        <Modal title="创建副本场次" busy={action.busy} close={() => setCreate(false)}>
          <QueryState {...templates} retry={templates.reload}>
            <EventForm
              key={createStart || "new"}
              defaultStart={createStart}
              templates={templates.data?.items || []}
              busy={action.busy}
              cancel={() => setCreate(false)}
              save={(body) =>
                action.run(async () => {
                  const result = await api<Raid>(`/guilds/${id}/events`, "POST", body, idempotency());
                  setCreate(false);
                  setSelected(result.id);
                }, "场次已创建")
              }
            />
          </QueryState>
        </Modal>
      )}
      {templatesOpen && manage && (
        <Drawer label="副本模板" close={() => setTemplatesOpen(false)}>
          <TemplatesPanel id={id} />
        </Drawer>
      )}
      {selected && (
        <Drawer
          label="场次详情"
          close={() => {
            setSelected(null);
            week.reload();
          }}
        >
          <EventDetail
            id={id}
            eventId={selected}
            me={me}
            changed={week.reload}
            onDeleted={() => {
              setSelected(null);
              week.reload();
            }}
          />
        </Drawer>
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
  cancel,
}: {
  templates: Template[];
  raid?: Raid;
  defaultStart?: string;
  busy: boolean;
  save: (body: unknown) => void;
  cancel: () => void;
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
        <Field label="副本模板">
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
        </Field>
      )}
      <TextField
        name="title"
        label="场次名称"
        defaultValue={raid?.title}
        required={!!raid}
      />
      <TextField
        name="starts_at"
        label="开始时间（北京时间）"
        type="datetime-local"
        defaultValue={raid ? localTime(raid.starts_at) : defaultStart}
        min={localTime(new Date().toISOString())}
        required
      />
      <TextField
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
      <div className="form-row" key={`capacity:${selectedTemplate?.id}`}>
        <TextField
          name="tank"
          label="承伤"
          type="number"
          min={0}
          max={10}
          defaultValue={
            raid?.capacities.tank ?? selectedTemplate?.need_tank ?? 0
          }
        />
        <TextField
          name="healer"
          label="奶妈"
          type="number"
          min={0}
          max={10}
          defaultValue={
            raid?.capacities.healer ?? selectedTemplate?.need_healer ?? 0
          }
        />
        <TextField
          name="dps"
          label="输出"
          type="number"
          min={0}
          max={10}
          defaultValue={raid?.capacities.dps ?? selectedTemplate?.need_dps ?? 0}
        />
      </div>
      <div className="form-row">
        <Field label="报名模式">
          <select name="mode" defaultValue={raid?.signup_mode || "open"}>
            <option value="open">开放报名</option>
            <option value="assign">仅管理指派</option>
          </select>
        </Field>
        {!raid && (
          <Field label="发布状态">
            <select name="status">
              <option value="published">立即发布</option>
              <option value="draft">保存草稿</option>
            </select>
          </Field>
        )}
      </div>
      <Field label="备注">
        <textarea name="note" maxLength={2000} defaultValue={raid?.note || ""} />
      </Field>
      {raid ? (
        <>
          <Switch name="reminder" label="启用站内开打提醒" defaultChecked={raid.reminder_enabled} />
          <TextField
            name="minutes"
            label="提前分钟"
            type="number"
            min={1}
            max={1440}
            defaultValue={raid.remind_minutes}
          />
        </>
      ) : (
        <p className="form-note">开打提醒采用百业当前默认设置，创建后可在场次详情调整。</p>
      )}
      {!raid && !templates.length && (
        <p className="text-bad">暂无可用模板，请先在“副本模板”中创建。</p>
      )}
      <div className="form-actions">
        <Button variant="ghost" onClick={cancel}>
          取消
        </Button>
        <Button type="submit" variant="primary" disabled={!raid && !templates.length}>
          {raid ? "保存修改" : "创建场次"}
        </Button>
      </div>
    </Form>
  );
}
export function TemplatesPanel({ id }: { id: string }) {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const filter = useDebounced(search);
  const query = useQuery<Page<Template>>(
    `/guilds/${id}/dungeon-templates?page=${page}&q=${encodeURIComponent(filter)}`,
  );
  const [editing, setEditing] = useState<Template | null | undefined>(undefined);
  const action = useAction(query.reload);
  const confirm = useConfirm();
  return (
    <section className="panel-section">
      {action.feedback}
      <header className="section-head">
        <h2>副本模板</h2>
        <Button size="sm" icon={Plus} onClick={() => setEditing(null)}>
          新增模板
        </Button>
      </header>
      {editing !== undefined && (
        <Card title={editing ? "编辑模板" : "新增模板"} className="card-inset">
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
              }, "模板已保存")
            }
          >
            <TextField name="name" label="模板名称" defaultValue={editing?.name} required />
            <div className="form-row">
              <TextField name="tank" label="承伤" type="number" min={0} max={10} defaultValue={editing?.need_tank ?? 1} />
              <TextField name="healer" label="奶妈" type="number" min={0} max={10} defaultValue={editing?.need_healer ?? 2} />
              <TextField name="dps" label="输出" type="number" min={0} max={10} defaultValue={editing?.need_dps ?? 7} />
            </div>
            <TextField
              name="duration"
              label="默认时长（分钟）"
              type="number"
              min={1}
              max={1440}
              defaultValue={editing?.default_duration_min || 60}
            />
            <div className="form-actions">
              <Button variant="ghost" onClick={() => setEditing(undefined)}>
                取消
              </Button>
              <Button type="submit" variant="primary">
                保存模板
              </Button>
            </div>
          </Form>
        </Card>
      )}
      <input
        type="search"
        aria-label="搜索副本模板"
        placeholder="搜索模板名称"
        value={search}
        onChange={(event) => {
          setSearch(event.target.value);
          setPage(1);
        }}
      />
      <QueryState {...query} retry={query.reload}>
        {!query.data?.items.length && <EmptyState compact>没有找到模板</EmptyState>}
        <div className="list">
          {query.data?.items.map((template) => (
            <div key={template.id} className="list-row">
              <div className="list-main">
                <strong>{template.name}</strong>
                <small>
                  {template.guild_id ? "百业模板" : "系统模板"} · 承伤 {template.need_tank} / 奶妈 {template.need_healer} / 输出{" "}
                  {template.need_dps} · {template.default_duration_min} 分钟
                </small>
              </div>
              {template.guild_id && (
                <div className="list-actions">
                  <Button size="sm" variant="ghost" icon={Pencil} onClick={() => setEditing(template)}>
                    编辑
                  </Button>
                  <Button
                    size="sm"
                    variant="danger-ghost"
                    disabled={action.busy}
                    onClick={async () => {
                      if (
                        await confirm({
                          title: "归档模板",
                          body: `归档「${template.name}」后不能再用它开新场次，已有场次不受影响。`,
                          confirmLabel: "归档",
                          danger: true,
                        })
                      )
                        action.run(
                          () =>
                            api(`/guilds/${id}/dungeon-templates/${template.id}`, "DELETE", undefined, {
                              "X-Expected-Version": String(template.version),
                            }),
                          "模板已归档",
                        );
                    }}
                  >
                    归档
                  </Button>
                </div>
              )}
            </div>
          ))}
        </div>
        <Pager page={page} total={query.data?.total || 0} setPage={setPage} />
      </QueryState>
    </section>
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
function EventDetail({ id, eventId, me, changed, onDeleted }: { id: string; eventId: string; me: Me; changed: () => void; onDeleted: () => void }) {
  const query = useQuery<Raid>(`/guilds/${id}/events/${eventId}`);
  const profiles = useQuery<{ items: Profile[] }>("/profiles");
  const { styleLabel } = useProfileOptions();
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
  const [seat, setSeat] = useState<number | null>(null);
  const [assignProfile, setAssignProfile] = useState("");
  const [editing, setEditing] = useState(false);
  const confirm = useConfirm();
  const action = useAction(() => {
    setSeat(null);
    query.reload();
    changed();
  });
  const raid = query.data;
  const cancelButton = (assignment: Assignment, label: string) =>
    !raid!.is_locked &&
    (raid!.can_manage || assignment.user_id === me.user_id) && (
      <Button
        size="sm"
        variant="danger-ghost"
        disabled={action.busy}
        onClick={() =>
          action.run(() => api(`/events/${eventId}/assignments/${assignment.id}`, "DELETE"), "已取消排班")
        }
      >
        {label}
      </Button>
    );
  return (
    <QueryState {...query} retry={query.reload}>
      {raid && (
        <div className="event-detail">
          {action.feedback}
          <header className="drawer-head">
            <span className="kicker">{date(raid.starts_at)} · 北京时间</span>
            <h2>{raid.title}</h2>
            <div className="drawer-meta">
              <StatusPill tone={raidPill(raid)[0]}>{raidPill(raid)[1]}</StatusPill>
              <span>副本：{raid.template_name}</span>
              <span>
                <Clock3 size={14} strokeWidth={1.75} aria-hidden />
                预计 {raid.duration_min} 分钟
              </span>
            </div>
            {raid.note && <p className="drawer-note">{raid.note}</p>}
          </header>
          {raid.is_locked && <p className="notice">场次已开始或已取消，排班与场次只读。</p>}
          <section className="drawer-section">
            <h3>
              队伍占位
              <small>
                {raid.confirmed_assignments.length}/{seatTotal(raid)}
              </small>
            </h3>
            <div className="member-grid">
              {seatsOf(raid).map((index) => {
                const role = seatRole(raid, index);
                const holder = raid.confirmed_assignments.find((item) => item.slot_index === index);
                if (holder)
                  return (
                    <article className="member-chip" key={index}>
                      <RoleIcon role={role} size={16} />
                      <div>
                        <strong title={holder.profile.name}>{holder.profile.name}</strong>
                        <small>鹅力 {formatPower(holder.profile.power)}</small>
                      </div>
                      {cancelButton(holder, "取消")}
                    </article>
                  );
                const pickable = !raid.is_locked && (raid.can_signup || (raid.can_manage && raid.status === "published"));
                return (
                  <button
                    type="button"
                    className="member-chip"
                    key={index}
                    disabled={!pickable}
                    aria-pressed={seat === index}
                    style={seat === index ? { borderColor: "var(--accent)", background: "var(--accent-soft)", textAlign: "left" } : { textAlign: "left" }}
                    onClick={() => setSeat(seat === index ? null : index)}
                  >
                    <RoleIcon role={role} size={16} />
                    <div>
                      <strong style={{ color: "var(--muted)" }}>点击占位</strong>
                      <small>{roleName(role)}</small>
                    </div>
                  </button>
                );
              })}
            </div>
          </section>
          {!!raid.waitlist_assignments.length && (
            <section className="drawer-section">
              <h3>
                候补队列<small>{raid.waitlist_assignments.length} 人</small>
              </h3>
              <div className="list">
                {raid.waitlist_assignments.map((assignment) => (
                  <div className="list-row list-row-dense" key={assignment.id}>
                    <span className="rank-number">{assignment.waitlist_position}</span>
                    <Avatar name={assignment.profile.name} id={assignment.user_id} size="sm" />
                    <div className="list-main">
                      <strong>{assignment.profile.name}</strong>
                      <small>{roleName(assignment.slot_role)}</small>
                    </div>
                    {cancelButton(assignment, "取消候补")}
                  </div>
                ))}
              </div>
            </section>
          )}
          {raid.my_assignment && (
            <p className="notice notice-accent">
              我的安排：{roleName(raid.my_assignment.slot_role)} · {statusName(raid.my_assignment.status)}。更换座位需要先取消再占位。
            </p>
          )}
          {raid.can_signup && (
            <section className="drawer-section">
              <h3>报名这场</h3>
              <div className="form-row">
                <Field label="本场角色">
                  <select value={profile} onChange={(e) => setProfile(e.target.value)}>
                    <option value="">选择角色</option>
                    {profiles.data?.items.map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.name}
                      </option>
                    ))}
                  </select>
                </Field>
                {raid.confirmed_assignments.length >= seatTotal(raid) && (
                  <Field label="候补定位">
                    <RoleSelect label="候补定位" value={role} onChange={setRole} />
                  </Field>
                )}
              </div>
              <p className="form-note">
                {raid.confirmed_assignments.length >= seatTotal(raid)
                  ? "座位已满，报名将进入候补；"
                  : seat
                    ? `已选 ${seat} 号 · ${roleName(seatRole(raid, seat))}；`
                    : "请先点选上方空座位；"}
                账号同一时间只能参加一场。
              </p>
              <Button
                variant="primary"
                block
                disabled={action.busy || !profile || (raid.confirmed_assignments.length < seatTotal(raid) && !seat)}
                onClick={() =>
                  action.run(
                    () =>
                      api(
                        `/events/${eventId}/assignments`,
                        "POST",
                        raid.confirmed_assignments.length >= seatTotal(raid)
                          ? { action: "signup", profile_id: profile, slot_role: role }
                          : { action: "signup", profile_id: profile, slot_role: seatRole(raid, seat!), slot_index: seat },
                        idempotency(),
                      ),
                    "已报名",
                  )
                }
              >
                报名这场
              </Button>
            </section>
          )}
          {raid.can_manage && (
            <section className="drawer-section">
              <h3>指派成员</h3>
              {raid.status === "draft" && <p className="form-note">先发布草稿，再指派成员。</p>}
              <input
                type="search"
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
                <div className="form-row form-row-actions">
                  <select aria-label="指派角色" value={assignProfile} onChange={(e) => setAssignProfile(e.target.value)}>
                    <option value="">选择成员角色</option>
                    {members.data?.items.flatMap((member) =>
                      member.profiles.map((p) => (
                        <option key={p.profile_id} value={p.profile_id}>
                          {member.nickname} · {p.name}
                        </option>
                      )),
                    )}
                  </select>
                  <Button
                    variant="primary"
                    icon={UserPlus}
                    disabled={action.busy || !assignProfile || !seat || raid.status !== "published"}
                    onClick={() =>
                      action.run(
                        () =>
                          api(
                            `/events/${eventId}/assignments`,
                            "POST",
                            { action: "assign", profile_id: assignProfile, slot_role: seatRole(raid, seat!), slot_index: seat },
                            idempotency(),
                          ),
                        "已指派",
                      )
                    }
                  >
                    指派
                  </Button>
                </div>
                <p className="form-note">{seat ? `已选 ${seat} 号 · ${roleName(seatRole(raid, seat))}` : "请先点选上方空座位，再指派成员"}</p>
                <Pager
                  page={memberPage}
                  total={members.data?.total || 0}
                  setPage={(page) => {
                    setMemberPage(page);
                    setAssignProfile("");
                  }}
                />
              </QueryState>
            </section>
          )}
          {raid.can_manage && editing && (
            <section className="drawer-section">
              <h3>编辑场次</h3>
              <EventForm
                raid={raid}
                templates={[]}
                busy={action.busy}
                cancel={() => setEditing(false)}
                save={(body) =>
                  action.run(async () => {
                    await api(`/guilds/${id}/events/${eventId}`, "PATCH", body);
                    setEditing(false);
                  }, "场次已更新")
                }
              />
            </section>
          )}
          {raid.can_manage && !editing && (
            <footer className="drawer-actions">
              {!raid.is_locked && (
                <Button
                  variant="danger-ghost"
                  disabled={action.busy}
                  onClick={async () => {
                    if (
                      await confirm({
                        title: "取消场次",
                        body: `取消「${raid.title}」后，已报名的成员会收到通知，现有排班保留为只读记录。`,
                        confirmLabel: "取消场次",
                        danger: true,
                      })
                    )
                      action.run(
                        () =>
                          api(`/guilds/${id}/events/${eventId}`, "DELETE", undefined, {
                            "X-Expected-Version": String(raid.version),
                          }),
                        "场次已取消",
                      );
                  }}
                >
                  取消场次
                </Button>
              )}
              {(raid.status === "cancelled" || Date.parse(raid.starts_at) > Date.now()) && (
                <Button
                  variant="danger-ghost"
                  icon={Trash2}
                  disabled={action.busy}
                  onClick={async () => {
                    const notify = raid.status === "published";
                    if (
                      await confirm({
                        title: notify ? "取消并删除场次" : "删除场次",
                        body: notify
                          ? `删除「${raid.title}」后无法恢复，已报名的成员会收到取消通知。`
                          : `删除「${raid.title}」后无法恢复，场次及其排班记录会被清除。`,
                        confirmLabel: notify ? "取消并删除" : "删除",
                        danger: true,
                      })
                    )
                      action.run(async () => {
                        await api(`/guilds/${id}/events/${eventId}/permanent`, "DELETE");
                        onDeleted();
                      }, "场次已删除");
                  }}
                >
                  {raid.status === "published" ? "取消并删除" : "删除场次"}
                </Button>
              )}
              <span className="spacer" />
              {!raid.is_locked && (
                <Button icon={Pencil} onClick={() => setEditing(true)}>
                  编辑场次
                </Button>
              )}
              {raid.status === "draft" && (
                <Button
                  variant="primary"
                  icon={Send}
                  disabled={action.busy}
                  onClick={() =>
                    action.run(
                      () =>
                        api(`/guilds/${id}/events/${eventId}`, "PATCH", {
                          status: "published",
                          expected_version: raid.version,
                        }),
                      "已发布",
                    )
                  }
                >
                  发布草稿
                </Button>
              )}
            </footer>
          )}
        </div>
      )}
    </QueryState>
  );
}
export function MyWeekPage({ me, unread }: { me: Me; unread: number }) {
  const query = useQuery<MyWeek>("/me/week");
  const action = useAction(query.reload);
  const raidsHref = me.membership ? `/guilds/${me.membership.guild_id}/raids` : "/guilds";
  const day = (iso: string, options: Intl.DateTimeFormatOptions) =>
    new Date(iso).toLocaleDateString("zh-CN", { timeZone: "Asia/Shanghai", ...options });
  return (
    <div className="stack">
      {action.feedback}
      <div className="toolbar">
        <div>
          <h2 className="section-title">{me.nickname}，这是你本周的安排</h2>
          <p className="section-desc">报名即确认占位，候补满足条件后自动递补。</p>
        </div>
        <span className="toolbar-hint">
          <Clock3 size={14} strokeWidth={1.75} aria-hidden />
          {query.data?.week_key} · 北京时间
        </span>
      </div>
      <div className="stat-grid">
        <div className="stat">
          <span>本周已报名及候补</span>
          <strong>
            {query.data?.items.length || 0}
            <small> 场</small>
          </strong>
        </div>
        <Link className="stat stat-link" href={raidsHref}>
          <span>百业副本</span>
          <strong>
            <CalendarDays size={20} strokeWidth={1.75} aria-hidden />
            查看周历
          </strong>
          <ChevronRight className="stat-arrow" size={18} strokeWidth={1.75} aria-hidden />
        </Link>
        <Link className="stat stat-link" href="/notifications">
          <span>未读通知</span>
          <strong className={unread ? "text-bad" : undefined}>
            <Bell size={20} strokeWidth={1.75} aria-hidden />
            {unread}
            <small> 条</small>
          </strong>
          <ChevronRight className="stat-arrow" size={18} strokeWidth={1.75} aria-hidden />
        </Link>
      </div>
      <Card title="我的副本" actions={<span className="card-hint">仅显示已报名或候补的场次</span>}>
        <QueryState {...query} retry={query.reload}>
          {!query.data?.items.length && (
            <EmptyState
              icon={CalendarDays}
              title="这周还没有安排"
              action={<ButtonLink href={raidsHref}>浏览副本</ButtonLink>}
            >
              去看看有哪些副本在招人。
            </EmptyState>
          )}
          <div className="list">
            {query.data?.items.map((item) => (
              <div className="list-row" key={item.assignment.id}>
                <Link className="list-link" href={`/guilds/${item.event.guild_id}/raids?event=${item.event.id}`}>
                  <span className="date-box">
                    <strong>{day(item.event.starts_at, { month: "numeric", day: "numeric" })}</strong>
                    <small>{day(item.event.starts_at, { weekday: "short" })}</small>
                  </span>
                  <span className="list-main">
                    <strong>{item.event.title}</strong>
                    <small>
                      {timeOf(item.event.starts_at)} · {item.assignment.profile.name} · {roleName(item.assignment.slot_role)}
                    </small>
                  </span>
                  <StatusPill tone={statusTone(item.assignment.status)}>
                    {item.assignment.status === "waitlist" ? "候补中" : "已报名"}
                  </StatusPill>
                  {item.event.status !== "published" && (
                    <StatusPill tone={statusTone(item.event.status)}>{statusName(item.event.status)}</StatusPill>
                  )}
                </Link>
                {item.event.can_cancel && (
                  <Button
                    size="sm"
                    variant="danger-ghost"
                    disabled={action.busy}
                    onClick={() =>
                      action.run(
                        () => api(`/events/${item.event.id}/assignments/${item.assignment.id}`, "DELETE"),
                        "已取消报名",
                      )
                    }
                  >
                    取消报名
                  </Button>
                )}
              </div>
            ))}
          </div>
        </QueryState>
      </Card>
    </div>
  );
}
