"use client";
import Link from "next/link";
import { useState } from "react";
import { api, clearSession } from "@/lib/api";
import {
  Application,
  Invitation,
  Me,
  Option,
  Page,
  Profile,
} from "@/lib/contracts";
import {
  APPLICATION_STATUSES,
  Card,
  date,
  Empty,
  Field,
  Form,
  Pager,
  QueryState,
  serverName,
  statusName,
  useAction,
  useProfileOptions,
  useQuery,
  value,
  WEEKDAYS,
} from "./business-ui";
import { OnlineTime } from "./online-time";

export function SettingsPage({
  me,
  reload,
}: {
  me: Me;
  reload: () => Promise<void>;
}) {
  const profiles = useQuery<{ items: Profile[] }>("/profiles");
  const options = useProfileOptions();
  const [editing, setEditing] = useState<Profile | null | undefined>(undefined);
  const action = useAction(async () => {
    profiles.reload();
    await reload();
  });
  return (
    <div className="real-stack">
      {action.feedback}
      <Card title="个人资料">
        <Form
          busy={action.busy}
          onSubmit={(form) =>
            action.run(() =>
              api("/me", "PATCH", {
                nickname: value(form, "nickname"),
                default_profile_id: value(form, "default_profile_id") || null,
              }),
            )
          }
        >
          <Field
            name="nickname"
            label="昵称"
            defaultValue={me.nickname}
            required
          />
          <label>
            默认角色
            <select
              name="default_profile_id"
              defaultValue={me.default_profile?.id || ""}
            >
              <option value="">自动选择最早创建的角色</option>
              {profiles.data?.items.map((profile) => (
                <option key={profile.id} value={profile.id}>
                  {profile.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            已验证邮箱
            <input readOnly value={me.email || ""} />
          </label>
          <button className="button button-primary">保存资料</button>
        </Form>
      </Card>
      <Card
        title="角色管理"
        actions={
          <button
            className="button button-secondary"
            onClick={() => setEditing(null)}
          >
            添加角色
          </button>
        }
      >
        <QueryState {...profiles} retry={profiles.reload}>
          {!profiles.data?.items.length && (
            <Empty>还没有角色，请先添加角色。</Empty>
          )}
          {profiles.data?.items.map((profile) => (
            <div className="real-row" key={profile.id}>
              <div>
                <strong>{profile.name}</strong>
                <small>
                  {serverName(profile.server)} · 鹅力 {profile.power} ·{" "}
                  {profile.styles.map(options.styleLabel).join(" / ")}
                  {profile.is_default ? " · 默认角色" : ""}
                </small>
              </div>
              <button
                className="button button-secondary"
                onClick={() => setEditing(profile)}
              >
                编辑
              </button>
              <button
                disabled={action.busy}
                className="button button-danger"
                onClick={() => {
                  if (confirm("删除这个角色？未结束的排班引用会阻止删除。"))
                    action.run(() => api(`/profiles/${profile.id}`, "DELETE"));
                }}
              >
                删除
              </button>
            </div>
          ))}
        </QueryState>
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
                await api(
                  editing ? `/profiles/${editing.id}` : "/profiles",
                  editing ? "PATCH" : "POST",
                  body,
                );
                setEditing(undefined);
              })
            }
          />
        )}
        {options.error && <p className="real-error">{options.error}</p>}
        {options.data && !options.data.styles.length && (
          <p className="real-error">
            正式流派字典尚未初始化，暂时不能保存角色。
          </p>
        )}
      </Card>
      <Card title="我的协作记录">
        <div className="real-actions">
          <Link href="/settings/applications">我的申请</Link>
          <Link href="/settings/invitations">收到的邀请</Link>
          <Link href="/settings/account-deletion">注销账号</Link>
        </div>
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
    <Card title={profile ? "编辑角色" : "添加角色"}>
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
        <Field
          name="name"
          label="角色名"
          defaultValue={profile?.name}
          required
        />
        <Field
          name="power"
          label="鹅力"
          type="number"
          min={0}
          max={2147483647}
          defaultValue={profile?.power || 0}
          required
        />
        <label>
          区服
          <select name="server" defaultValue={profile?.server || "official"}>
            {servers.map((server) => (
              <option key={server.code} value={server.code}>
                {server.label}
              </option>
            ))}
          </select>
        </label>
        <div>
          <span>流派（至少一项）</span>
          <div className="real-checks">
            {styles.map((style) => (
              <label key={style.code}>
                <input
                  type="checkbox"
                  name="styles"
                  value={style.code}
                  defaultChecked={profile?.styles.includes(style.code)}
                />
                {style.label}
              </label>
            ))}
          </div>
        </div>
        <label>
          简介
          <textarea
            name="bio"
            maxLength={500}
            defaultValue={profile?.bio || ""}
          />
        </label>
        <div>
          <span>在线时段</span>
          {slots.map((slot, index) => (
            <div className="real-inline" key={index}>
              <select
                aria-label="星期"
                value={slot.weekday}
                onChange={(e) =>
                  update(index, { weekday: Number(e.target.value) })
                }
              >
                {WEEKDAYS.map((day, index) => (
                  <option key={day} value={index + 1}>
                    周{day}
                  </option>
                ))}
              </select>
              <OnlineTime
                label="开始时间"
                minutes={slot.start_minute}
                onChange={(minutes) => update(index, { start_minute: minutes })}
              />
              <OnlineTime
                label="结束时间"
                end
                minutes={slot.end_minute}
                onChange={(minutes) => update(index, { end_minute: minutes })}
              />
              <button
                type="button"
                onClick={() => setSlots(slots.filter((_, i) => i !== index))}
              >
                移除
              </button>
            </div>
          ))}
          <small>
            北京时间；结束时间 00:00 表示当天 24:00。跨午夜拆成两个时段。
          </small>
          <button
            type="button"
            disabled={slots.length >= 28}
            onClick={() =>
              setSlots([
                ...slots,
                { weekday: 1, start_minute: 1200, end_minute: 1440 },
              ])
            }
          >
            添加时段
          </button>
        </div>
        <div className="real-actions">
          <button disabled={!styles.length} className="button button-primary">
            保存角色
          </button>
          <button
            className="button button-secondary"
            type="button"
            onClick={close}
          >
            取消
          </button>
        </div>
      </Form>
    </Card>
  );
}
export function MyApplications() {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState("");
  const query = useQuery<Page<Application>>(
    `/me/applications?page=${page}${status ? `&status=${status}` : ""}`,
  );
  const action = useAction(query.reload);
  return (
    <Card title="我的申请">
      {action.feedback}
      <label className="real-filter-field">
        申请状态
        <select
          aria-label="申请状态"
          value={status}
          onChange={(e) => {
            setStatus(e.target.value);
            setPage(1);
          }}
        >
          <option value="">全部状态</option>
          {APPLICATION_STATUSES.map((item) => (
            <option key={item} value={item}>
              {statusName(item)}
            </option>
          ))}
        </select>
      </label>
      <QueryState {...query} retry={query.reload}>
        {!query.data?.items.length && <Empty />}
        {query.data?.items.map((item) => (
          <div className="real-row" key={item.id}>
            <div>
              <Link href={`/guilds/${item.guild_id}`}>{item.guild_name}</Link>
              <small>
                {date(item.created_at)} · {statusName(item.status)}
              </small>
              <p>{item.message}</p>
            </div>
            {item.status === "pending" && (
              <button
                disabled={action.busy}
                className="button button-secondary"
                onClick={() => {
                  if (confirm("撤回这条入会申请？"))
                    action.run(() =>
                      api(`/applications/${item.id}/withdraw`, "POST"),
                    );
                }}
              >
                撤回
              </button>
            )}
          </div>
        ))}
        <Pager page={page} total={query.data?.total || 0} setPage={setPage} />
      </QueryState>
    </Card>
  );
}
export function MyInvitations() {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState("pending");
  const query = useQuery<Page<Invitation>>(
    `/me/invitations?page=${page}${status ? `&status=${status}` : ""}`,
  );
  return (
    <Card title="收到的邀请">
      <label className="real-filter-field">
        邀请状态
        <select
          aria-label="邀请状态"
          value={status}
          onChange={(e) => {
            setStatus(e.target.value);
            setPage(1);
          }}
        >
          <option value="">全部状态</option>
          {["pending", "accepted", "declined", "revoked", "expired"].map(
            (item) => (
              <option key={item} value={item}>
                {statusName(item)}
              </option>
            ),
          )}
        </select>
      </label>
      <QueryState {...query} retry={query.reload}>
        {!query.data?.items.length && <Empty />}
        {query.data?.items.map((item) => (
          <div className="real-row" key={item.id}>
            <div>
              <strong>{item.guild_name}</strong>
              <small>
                {statusName(item.status)} · 有效期 {date(item.expires_at)}
              </small>
            </div>
            <Link href={`/invitations/${item.id}`}>查看邀请</Link>
          </div>
        ))}
        <Pager page={page} total={query.data?.total || 0} setPage={setPage} />
      </QueryState>
    </Card>
  );
}
export function AccountDeletion({ me }: { me: Me }) {
  const [confirmation, setConfirmation] = useState("");
  const action = useAction(() => {});
  return (
    <Card title="注销账号">
      {action.feedback}
      <p>注销将删除账号、邮箱、角色和全部关联业务记录，无法恢复。</p>
      {me.membership?.role === "owner" && (
        <p className="real-error">
          你是社长：注销将同时解散百业，删除全体成员在该百业的业务数据。其他成员账号和个人角色保留。
        </p>
      )}
      <label>
        输入 DELETE 确认
        <input
          value={confirmation}
          onChange={(e) => setConfirmation(e.target.value)}
        />
      </label>
      <button
        className="button button-danger"
        disabled={action.busy || confirmation !== "DELETE"}
        onClick={() => {
          if (confirm("最后确认：永久注销账号并删除上述全部数据？"))
            action.run(async () => {
              await api("/me/account-deletion", "POST", {
                confirm_user_id: me.user_id,
                confirmation,
              });
              clearSession();
            });
        }}
      >
        永久注销
      </button>
    </Card>
  );
}
