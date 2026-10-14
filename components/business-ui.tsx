"use client";
import {
  createContext,
  FormEvent,
  ReactNode,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from "react";
import { createPortal } from "react-dom";
import { X } from "lucide-react";
import { api, ApiError } from "@/lib/api";
import type { Me, ProfileOptions } from "@/lib/contracts";
import { Button, Field, IconButton, Skeleton, type Tone } from "./ui";

/* 数据查询、写操作反馈和业务字典。展示组件见 ui.tsx。 */

function Toast({ error, message, dismiss }: { error: string; message: string; dismiss: () => void }) {
  if ((!error && !message) || typeof document === "undefined") return null;
  return createPortal(
    <div className={`toast${error ? " toast-error" : ""}`}>
      <div role={error ? "alert" : "status"}>
        <strong>{error ? "操作未完成" : "操作成功"}</strong>
        <p>{error || message}</p>
      </div>
      <IconButton label="关闭提示" icon={X} size="sm" onClick={dismiss} />
    </div>,
    document.body,
  );
}

// 平台层在收到通知推送或窗口重新获得焦点时递增该值，页面内所有查询随之后台刷新。
const RefreshContext = createContext(0);
export const RefreshProvider = RefreshContext.Provider;

export function useQuery<T>(path: string | null) {
  const refreshKey = useContext(RefreshContext);
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [backgroundError, setBackgroundError] = useState("");
  const hasData = useRef(false);
  const [revision, setRevision] = useState(0);
  const loadedPath = useRef<string | null | undefined>(undefined);
  const reload = useCallback(() => setRevision((value) => value + 1), []);
  useEffect(() => {
    let active = true;
    const controller = new AbortController();
    setError("");
    setBackgroundError("");
    if (loadedPath.current !== path) {
      loadedPath.current = path;
      hasData.current = false;
      setData(null);
      setLoading(!!path);
    }
    if (!path) return;
    api<T>(path, "GET", undefined, undefined, controller.signal)
      .then((result) => {
        if (active) {
          setData(result);
          hasData.current = true;
        }
      })
      .catch((e) => {
        if (active) {
          if (hasData.current) setBackgroundError(e.message);
          else setError(e.message);
        }
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
      controller.abort();
    };
  }, [path, revision, refreshKey]);
  return { data, error, backgroundError, loading, reload };
}
export function useDebounced<T>(value: T, delay = 300) {
  const [settled, setSettled] = useState(value);
  useEffect(() => { const timer = setTimeout(() => setSettled(value), delay); return () => clearTimeout(timer); }, [value, delay]);
  return settled;
}
export function QueryState({
  loading,
  error,
  backgroundError,
  retry,
  children,
}: {
  loading: boolean;
  error: string;
  backgroundError?: string;
  retry: () => void;
  children: ReactNode;
}) {
  if (loading) return <Skeleton />;
  if (error)
    return (
      <div className="empty" role="alert">
        <p className="text-bad">加载失败：{error}</p>
        <Button onClick={retry}>重试</Button>
      </div>
    );
  return (
    <>
      {backgroundError && (
        <div className="notice notice-warn" role="status">
          <span>刷新失败：{backgroundError}</span>
          <Button size="sm" onClick={retry}>
            重试
          </Button>
        </div>
      )}
      {children}
    </>
  );
}
export function Card({
  title,
  children,
  actions,
  className,
}: {
  title?: ReactNode;
  children: ReactNode;
  actions?: ReactNode;
  className?: string;
}) {
  return (
    <section className={`card${className ? ` ${className}` : ""}`}>
      {(title || actions) && (
        <header className="card-head">
          {title && <h2>{title}</h2>}
          {actions && <div className="card-actions">{actions}</div>}
        </header>
      )}
      {children}
    </section>
  );
}
export const date = (value: string) =>
  new Date(value).toLocaleString("zh-CN", {
    timeZone: "Asia/Shanghai",
    hour12: false,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
export const roleName = (role: string) =>
  ({
    owner: "社长",
    admin: "管理员",
    member: "成员",
    tank: "承伤",
    healer: "奶妈",
    dps: "输出",
  })[role] || role;
export const statusName = (status: string) =>
  ({
    pending: "待处理",
    approved: "已通过",
    rejected: "已拒绝",
    withdrawn: "已撤回",
    accepted: "已接受",
    declined: "已拒绝",
    revoked: "已撤销",
    expired: "已过期",
    confirmed: "正式",
    waitlist: "候补",
    draft: "草稿",
    published: "已发布",
    cancelled: "已取消",
    finished: "已结束",
  })[status] || status;
// 状态胶囊颜色：进行中/待处理用提醒色，完成用成功色，失效类用弱化色。
export const statusTone = (status: string): Tone =>
  (({
    pending: "warn",
    waitlist: "warn",
    draft: "muted",
    approved: "ok",
    accepted: "ok",
    confirmed: "ok",
    published: "accent",
    rejected: "bad",
    declined: "bad",
    cancelled: "muted",
    finished: "muted",
    withdrawn: "muted",
    revoked: "muted",
    expired: "muted",
  }) as Record<string, Tone>)[status] || "muted";
export function useAction(refresh: () => void | Promise<void>) {
  const running = useRef(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  async function run(action: () => Promise<unknown>, success = "操作成功") {
    if (running.current) return;
    running.current = true;
    setBusy(true);
    setError("");
    setMessage("");
    try {
      await action();
      setMessage(success);
      await refresh();
    } catch (e) {
      setError((e as Error).message);
      if (e instanceof ApiError && [404, 409].includes(e.status))
        await Promise.resolve(refresh()).catch(() => {});
    } finally {
      running.current = false;
      setBusy(false);
    }
  }
  return {
    busy,
    error,
    message,
    run,
    feedback: <Toast error={error} message={message} dismiss={() => { setError(""); setMessage(""); }} />,
  };
}
export function Form({
  children,
  onSubmit,
  busy,
  className,
}: {
  children: ReactNode;
  onSubmit: (values: FormData) => void;
  busy: boolean;
  className?: string;
}) {
  return (
    <form
      className={`form${className ? ` ${className}` : ""}`}
      onSubmit={(event: FormEvent<HTMLFormElement>) => {
        event.preventDefault();
        onSubmit(new FormData(event.currentTarget));
      }}
    >
      <fieldset disabled={busy}>{children}</fieldset>
    </form>
  );
}
export function TextField({
  name,
  label,
  defaultValue,
  type = "text",
  required = false,
  min,
  max,
  hint,
}: {
  name: string;
  label: string;
  defaultValue?: string | number;
  type?: string;
  required?: boolean;
  min?: number;
  max?: number;
  hint?: ReactNode;
}) {
  return (
    <Field label={label} hint={hint}>
      <input name={name} type={type} defaultValue={defaultValue} required={required} min={min} max={max} />
    </Field>
  );
}
export const value = (form: FormData, key: string) => String(form.get(key) || "");

export const serverName = (server: string) =>
  server === "official" ? "官服" : "渠道服";
export const WEEKDAYS = ["一", "二", "三", "四", "五", "六", "日"];
export const APPLICATION_STATUSES = ["pending", "approved", "rejected", "withdrawn"];
export const canManage = (me: Me, guildId: string) =>
  me.membership?.guild_id === guildId && me.membership.role !== "member";
export function useProfileOptions() {
  const query = useQuery<ProfileOptions>(
    "/dictionaries/profile-options",
  );
  const styleLabel = (code: string) =>
    query.data?.styles.find((option) => option.code === code)?.label || code;
  return { ...query, styleLabel };
}
