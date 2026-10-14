"use client";
import Link from "next/link";
import { ChevronRight, Inbox, Mail, Pencil, Plus, Star, Trash2, UserX, X, Bird } from "lucide-react";
import { useState } from "react";
import { api, clearSession } from "@/lib/api";
import { Application, Invitation, Me, Option, Page, Profile } from "@/lib/contracts";
import {
  APPLICATION_STATUSES,
  Card,
  date,
  Form,
  QueryState,
  serverName,
  statusName,
  statusTone,
  TextField,
  useAction,
  useProfileOptions,
  useQuery,
  value,
  WEEKDAYS,
  formatPower,
} from "./business-ui";
import { OnlineTime } from "./online-time";
import {
  Button,
  ButtonLink,
  EmptyState,
  Field,
  IconButton,
  Pager,
  StatusPill,
  useConfirm,
} from "./ui";

export function SettingsPage({ me, reload }: { me: Me; reload: () => Promise<void> }) {
  const profiles = useQuery<{ items: Profile[] }>("/profiles");
  const options = useProfileOptions();
  const confirm = useConfirm();
  const [editing, setEditing] = useState<Profile | null | undefined>(undefined);
  const action = useAction(async () => {
    profiles.reload();
    await reload();
  });
  const dictionaryMissing = !!options.data && !options.data.styles.length;
  return (
    <div className="stack narrow">
      {action.feedback}
      <Card title="个人资料">
        <Form
          busy={action.busy}
          onSubmit={(form) =>
            action.run(
              () =>
                api("/me", "PATCH", {
                  nickname: value(form, "nickname"),
                  default_profile_id: value(form, "default_profile_id") || null,
                }),
              "资料已保存",
            )
          }
        >
          <TextField name="nickname" label="昵称" defaultValue={me.nickname} required />
          <Field label="默认角色" hint="未设置时自动使用最早创建的角色">
            <select name="default_profile_id" defaultValue={me.default_profile?.id || ""}>
              <option value="">自动选择</option>
              {profiles.data?.items.map((profile) => (
                <option key={profile.id} value={profile.id}>
                  {profile.name}
                </option>
              ))}
            </select>
          </Field>
          <Field label="已验证邮箱">
            <input readOnly value={me.email || ""} />
          </Field>
          <div className="form-actions">
            <Button type="submit" variant="primary">
              保存资料
            </Button>
          </div>
        </Form>
      </Card>
      <Card
        title="角色管理"
        actions={
          <Button size="sm" icon={Plus} disabled={dictionaryMissing} onClick={() => setEditing(null)}>
            添加角色
          </Button>
        }
      >
        {options.error && <p className="notice notice-bad">{options.error}</p>}
        {dictionaryMissing && <p className="notice notice-warn">正式流派字典尚未初始化，暂时不能保存角色。</p>}
        {editing !== undefined && (
          <ProfileForm
            key={editing?.id || "new"}
            profile={editing}
            servers={options.data?.servers || []}
            styles={options.data?.styles || []}
            busy={action.busy}
            close={() => setEditing(undefined)}
            save={(body) =>
              action.run(async () => {
                await api(editing ? `/profiles/${editing.id}` : "/profiles", editing ? "PATCH" : "POST", body);
                setEditing(undefined);
              }, "角色已保存")
            }
          />
        )}
        <QueryState {...profiles} retry={profiles.reload}>
          {!profiles.data?.items.length && (
            <EmptyState compact>还没有角色，添加角色后才能报名副本和加入百业。</EmptyState>
          )}
          <div className="list">
            {profiles.data?.items.map((profile) => (
              <div className="list-row" key={profile.id}>
                <span className="list-main">
                  <strong>
                    {profile.name}
                    {profile.is_default && (
                      <StatusPill tone="accent">
                        <Star size={11} strokeWidth={2} fill="currentColor" aria-hidden />
                        默认
                      </StatusPill>
                    )}
                  </strong>
                  <small>
                    {serverName(profile.server)} · <Bird size={12} strokeWidth={1.75} aria-hidden /> {formatPower(profile.power)} ·{" "}
                    {profile.styles.map(options.styleLabel).join(" / ")}
                  </small>
                </span>
                <div className="list-actions">
                  {!profile.is_default && (
                    <Button
                      size="sm"
                      variant="ghost"
                      disabled={action.busy}
                      onClick={() =>
                        action.run(() => api("/me", "PATCH", { default_profile_id: profile.id }), "已设为默认角色")
                      }
                    >
                      设为默认
                    </Button>
                  )}
                  <Button size="sm" variant="ghost" icon={Pencil} onClick={() => setEditing(profile)}>
                    编辑
                  </Button>
                  <Button
                    size="sm"
                    variant="danger-ghost"
                    icon={Trash2}
                    disabled={action.busy}
                    onClick={async () => {
                      if (
                        await confirm({
                          title: "删除角色",
                          body: `删除「${profile.name}」？还有未结束排班的角色不能删除。`,
                          confirmLabel: "删除",
                          danger: true,
                        })
                      )
                        action.run(() => api(`/profiles/${profile.id}`, "DELETE"), "角色已删除");
                    }}
                  >
                    删除
                  </Button>
                </div>
              </div>
            ))}
          </div>
        </QueryState>
      </Card>
      <Card title="我的协作记录">
        <nav className="nav-list">
          <Link href="/settings/applications">
            <Inbox size={18} strokeWidth={1.75} aria-hidden />
            我的申请
            <ChevronRight size={16} strokeWidth={1.75} aria-hidden />
          </Link>
          <Link href="/settings/invitations">
            <Mail size={18} strokeWidth={1.75} aria-hidden />
            收到的邀请
            <ChevronRight size={16} strokeWidth={1.75} aria-hidden />
          </Link>
          <Link href="/settings/account-deletion" className="nav-list-danger">
            <UserX size={18} strokeWidth={1.75} aria-hidden />
            注销账号
            <ChevronRight size={16} strokeWidth={1.75} aria-hidden />
          </Link>
        </nav>
      </Card>
    </div>
  );
}

function ProfileForm({
  profile,
  servers,
  styles,
  busy,
  save,
  close,
}: {
  profile: Profile | null;
  servers: Option[];
  styles: Option[];
  busy: boolean;
  save: (body: unknown) => void;
  close: () => void;
}) {
  const [slots, setSlots] = useState(profile?.online_slots || []);
  const update = (index: number, patch: Partial<(typeof slots)[number]>) =>
    setSlots(slots.map((item, i) => (i === index ? { ...item, ...patch } : item)));
  return (
    <Card title={profile ? `编辑角色 · ${profile.name}` : "添加角色"} className="card-inset">
      <Form
        busy={busy}
        onSubmit={(form) =>
          save({
            name: value(form, "name"),
            server: value(form, "server"),
            power: Number(value(form, "power")),
            styles: form.getAll("styles"),
            bio: value(form, "bio"),
            online_slots: slots,
          })
        }
      >
        <div className="form-row">
          <TextField name="name" label="角色名" defaultValue={profile?.name} required />
          <Field label="区服">
            <select name="server" defaultValue={profile?.server || "official"}>
              {servers.map((server) => (
                <option key={server.code} value={server.code}>
                  {server.label}
                </option>
              ))}
            </select>
          </Field>
          <TextField name="power" label="鹅力" type="number" min={0} max={99999999.9999} step="0.0001" defaultValue={profile?.power ?? 0} required />
        </div>
        <fieldset className="field-group">
          <legend className="field-label">流派（至少一项）</legend>
          <div className="check-grid">
            {styles.map((style) => (
              <label className="check" key={style.code}>
                <input type="checkbox" name="styles" value={style.code} defaultChecked={profile?.styles.includes(style.code)} />
                {style.label}
              </label>
            ))}
          </div>
        </fieldset>
        <Field label="简介">
          <textarea name="bio" maxLength={500} defaultValue={profile?.bio || ""} />
        </Field>
        <fieldset className="field-group">
          <legend className="field-label">在线时段</legend>
          {slots.map((slot, index) => (
            <div className="slot-editor" key={index}>
              <select aria-label="星期" value={slot.weekday} onChange={(e) => update(index, { weekday: Number(e.target.value) })}>
                {WEEKDAYS.map((day, dayIndex) => (
                  <option key={day} value={dayIndex + 1}>
                    周{day}
                  </option>
                ))}
              </select>
              <OnlineTime label="开始时间" hideLabel minutes={slot.start_minute} onChange={(minutes) => update(index, { start_minute: minutes })} />
              <span aria-hidden>–</span>
              <OnlineTime label="结束时间" hideLabel end minutes={slot.end_minute} onChange={(minutes) => update(index, { end_minute: minutes })} />
              <IconButton label="移除时段" icon={X} size="sm" tone="danger" onClick={() => setSlots(slots.filter((_, i) => i !== index))} />
            </div>
          ))}
          <small className="field-hint">北京时间；结束时间 00:00 表示当天 24:00，跨午夜请拆成两个时段。</small>
          <div>
            <Button
              size="sm"
              variant="ghost"
              icon={Plus}
              disabled={slots.length >= 28}
              onClick={() => setSlots([...slots, { weekday: 1, start_minute: 1200, end_minute: 1440 }])}
            >
              添加时段
            </Button>
          </div>
        </fieldset>
        <div className="form-actions">
          <Button variant="ghost" onClick={close}>
            取消
          </Button>
          <Button type="submit" variant="primary" disabled={!styles.length}>
            保存角色
          </Button>
        </div>
      </Form>
    </Card>
  );
}

function StatusFilter({
  label,
  value: selected,
  statuses,
  onChange,
}: {
  label: string;
  value: string;
  statuses: string[];
  onChange: (value: string) => void;
}) {
  return (
    <div className="toolbar">
      <select className="select-inline" aria-label={label} value={selected} onChange={(e) => onChange(e.target.value)}>
        <option value="">全部状态</option>
        {statuses.map((item) => (
          <option key={item} value={item}>
            {statusName(item)}
          </option>
        ))}
      </select>
    </div>
  );
}

export function MyApplications() {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState("");
  const confirm = useConfirm();
  const query = useQuery<Page<Application>>(`/me/applications?page=${page}${status ? `&status=${status}` : ""}`);
  const action = useAction(query.reload);
  return (
    <div className="stack narrow">
      {action.feedback}
      <StatusFilter
        label="申请状态"
        value={status}
        statuses={APPLICATION_STATUSES}
        onChange={(next) => {
          setStatus(next);
          setPage(1);
        }}
      />
      <Card>
        <QueryState {...query} retry={query.reload}>
          {!query.data?.items.length && (
            <EmptyState icon={Inbox} title="暂无申请记录" action={<ButtonLink href="/guilds">去看看百业</ButtonLink>} />
          )}
          <div className="list">
            {query.data?.items.map((item) => (
              <div className="list-row" key={item.id}>
                <span className="list-main">
                  <strong>
                    <Link href={`/guilds/${item.guild_id}`}>{item.guild_name}</Link>
                    <StatusPill tone={statusTone(item.status)}>{statusName(item.status)}</StatusPill>
                  </strong>
                  {item.message && <span className="list-desc">{item.message}</span>}
                  <small>{date(item.created_at)}</small>
                </span>
                {item.status === "pending" && (
                  <Button
                    size="sm"
                    variant="ghost"
                    disabled={action.busy}
                    onClick={async () => {
                      if (
                        await confirm({
                          title: "撤回申请",
                          body: `撤回加入「${item.guild_name}」的申请？`,
                          confirmLabel: "撤回",
                        })
                      )
                        action.run(() => api(`/applications/${item.id}/withdraw`, "POST"), "申请已撤回");
                    }}
                  >
                    撤回
                  </Button>
                )}
              </div>
            ))}
          </div>
          <Pager page={page} total={query.data?.total || 0} setPage={setPage} />
        </QueryState>
      </Card>
    </div>
  );
}

export function MyInvitations() {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState("pending");
  const query = useQuery<Page<Invitation>>(`/me/invitations?page=${page}${status ? `&status=${status}` : ""}`);
  return (
    <div className="stack narrow">
      <StatusFilter
        label="邀请状态"
        value={status}
        statuses={["pending", "accepted", "declined", "revoked", "expired"]}
        onChange={(next) => {
          setStatus(next);
          setPage(1);
        }}
      />
      <Card>
        <QueryState {...query} retry={query.reload}>
          {!query.data?.items.length && <EmptyState icon={Mail} title="暂无邀请" />}
          <div className="list">
            {query.data?.items.map((item) => (
              <Link className="list-row list-link" key={item.id} href={`/invitations/${item.id}`}>
                <span className="list-main">
                  <strong>
                    {item.guild_name}
                    <StatusPill tone={statusTone(item.status)}>{statusName(item.status)}</StatusPill>
                  </strong>
                  <small>有效期至 {date(item.expires_at)}</small>
                </span>
                <ChevronRight size={16} strokeWidth={1.75} aria-hidden />
              </Link>
            ))}
          </div>
          <Pager page={page} total={query.data?.total || 0} setPage={setPage} />
        </QueryState>
      </Card>
    </div>
  );
}

export function AccountDeletion({ me }: { me: Me }) {
  const [confirmation, setConfirmation] = useState("");
  const confirm = useConfirm();
  const action = useAction(() => {});
  return (
    <div className="stack narrow">
      {action.feedback}
      <Card title="永久注销账号" className="card-danger">
        <div className="prose">
          <p>注销将删除账号、邮箱、角色和全部关联业务记录，无法恢复。</p>
          {me.membership?.role === "owner" && (
            <p className="notice notice-bad">
              你是社长：注销将同时解散百业，删除全体成员在该百业的业务数据。其他成员的账号和个人角色保留。
            </p>
          )}
        </div>
        <Field label="输入 DELETE 确认">
          <input value={confirmation} autoComplete="off" onChange={(e) => setConfirmation(e.target.value)} />
        </Field>
        <div className="form-actions">
          <Button
            variant="danger"
            disabled={action.busy || confirmation !== "DELETE"}
            onClick={async () => {
              if (
                await confirm({
                  title: "最后确认",
                  body: "永久注销账号并删除上述全部数据？此操作无法撤销。",
                  confirmLabel: "永久注销",
                  danger: true,
                })
              )
                action.run(async () => {
                  await api("/me/account-deletion", "POST", { confirm_user_id: me.user_id, confirmation });
                  clearSession();
                });
            }}
          >
            永久注销
          </Button>
        </div>
      </Card>
    </div>
  );
}
