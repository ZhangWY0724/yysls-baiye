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
import { api, ApiError } from "@/lib/api";
import type { Me, Option } from "@/lib/contracts";
import { createPortal } from "react-dom";

function ActionFeedback({ error, message, dismiss }: { error: string; message: string; dismiss: () => void }) {
  if ((!error && !message) || typeof document === "undefined") return null;
  return createPortal(
    <div className={`action-feedback ${error ? "action-feedback-error" : "action-feedback-success"}`}>
      <div role={error ? "alert" : "status"}>
        <strong>{error ? "操作未完成" : "操作成功"}</strong>
        <p>{error || message}</p>
      </div>
      <button type="button" className="action-feedback-close" aria-label="关闭操作提示" onClick={dismiss}>×</button>
    </div>, document.body,
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
  if (loading)
    return (
      <p className="real-empty" role="status">
        正在加载…
      </p>
    );
  if (error)
    return (
      <div className="real-empty">
        <p className="real-error" role="alert">
          {error}
        </p>
        <button className="button button-secondary" onClick={retry}>
          重试
        </button>
      </div>
    );
  return <>{backgroundError && <div className="session-warning" role="status"><span>刷新失败：{backgroundError}</span><button className="button button-secondary compact" onClick={retry}>重试</button></div>}{children}</>;
}
export function Empty({ children = "暂无记录" }: { children?: ReactNode }) {
  return <p className="real-empty">{children}</p>;
}
export function Card({
  title,
  children,
  actions,
  className,
}: {
  title?: string;
  children: ReactNode;
  actions?: ReactNode;
  className?: string;
}) {
  return (
    <section className={`real-card ${className || ""}`}>
      {(title || actions) && (
        <div className="real-card-heading">
          <h2>{title}</h2>
          {actions}
        </div>
      )}
      {children}
    </section>
  );
}
export const date = (value: string) =>
  new Date(value).toLocaleString("zh-CN", {
    timeZone: "Asia/Shanghai",
    hour12: false,
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
    feedback: <ActionFeedback error={error} message={message} dismiss={() => { setError(""); setMessage(""); }} />,
  };
}
export function Form({
  children,
  onSubmit,
  busy,
}: {
  children: ReactNode;
  onSubmit: (values: FormData) => void;
  busy: boolean;
}) {
  return (
    <form
      className="real-form"
      onSubmit={(event: FormEvent<HTMLFormElement>) => {
        event.preventDefault();
        onSubmit(new FormData(event.currentTarget));
      }}
    >
      <fieldset disabled={busy}>{children}</fieldset>
    </form>
  );
}
export function Field({
  name,
  label,
  defaultValue,
  type = "text",
  required = false,
  min,
  max,
}: {
  name: string;
  label: string;
  defaultValue?: string | number;
  type?: string;
  required?: boolean;
  min?: number;
  max?: number;
}) {
  return (
    <label>
      {label}
      <input
        name={name}
        type={type}
        defaultValue={defaultValue}
        required={required}
        min={min}
        max={max}
      />
    </label>
  );
}
export const value = (form: FormData, key: string) =>
  String(form.get(key) || "");

export const serverName = (server: string) =>
  server === "official" ? "官服" : "渠道服";
export const WEEKDAYS = ["一", "二", "三", "四", "五", "六", "日"];
export const APPLICATION_STATUSES = ["pending", "approved", "rejected", "withdrawn"];
export const canManage = (me: Me, guildId: string) =>
  me.membership?.guild_id === guildId && me.membership.role !== "member";
export function useProfileOptions() {
  const query = useQuery<{ servers: Option[]; styles: Option[] }>(
    "/dictionaries/profile-options",
  );
  const styleLabel = (code: string) =>
    query.data?.styles.find((option) => option.code === code)?.label || code;
  return { ...query, styleLabel };
}
export function Pager({
  page,
  total,
  setPage,
}: {
  page: number;
  total: number;
  setPage: (page: number) => void;
}) {
  return (
    <div className="real-actions">
      <button disabled={page <= 1} onClick={() => setPage(page - 1)}>
        上一页
      </button>
      <span>
        第 {page} 页 · 共 {total} 条
      </span>
      <button disabled={page * 20 >= total} onClick={() => setPage(page + 1)}>
        下一页
      </button>
    </div>
  );
}
export function Modal({
  title,
  busy,
  close,
  children,
}: {
  title: string;
  busy: boolean;
  close: () => void;
  children: ReactNode;
}) {
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy) close();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [busy, close]);
  return (
    <div className="modal-backdrop" onClick={() => !busy && close()}>
      <section
        className="create-modal"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onClick={(event) => event.stopPropagation()}
      >
        <header>
          <h2>{title}</h2>
          <button
            className="text-button"
            aria-label={`关闭${title}`}
            disabled={busy}
            onClick={close}
          >
            ×
          </button>
        </header>
        {children}
      </section>
    </div>
  );
}
