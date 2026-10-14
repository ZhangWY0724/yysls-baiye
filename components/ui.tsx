"use client";
import Link from "next/link";
import { ChevronLeft, ChevronRight, X, type LucideIcon } from "lucide-react";
import {
  ButtonHTMLAttributes,
  createContext,
  ReactNode,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from "react";
import { createPortal } from "react-dom";

/* 基础展示组件：样式全部来自 globals.css 中的同名类，页面不再直接拼按钮/输入框样式。 */

type Variant = "primary" | "secondary" | "ghost" | "danger" | "danger-ghost";
type Size = "md" | "sm";
const buttonClass = (variant: Variant, size: Size, block?: boolean) =>
  `btn btn-${variant} btn-${size}${block ? " btn-block" : ""}`;
const iconSize = (size: Size) => (size === "sm" ? 15 : 16);

export function Button({
  variant = "secondary",
  size = "md",
  icon: Icon,
  block,
  type = "button",
  children,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: Variant;
  size?: Size;
  icon?: LucideIcon;
  block?: boolean;
}) {
  return (
    <button type={type} className={buttonClass(variant, size, block)} {...props}>
      {Icon && <Icon size={iconSize(size)} strokeWidth={1.75} aria-hidden />}
      {children}
    </button>
  );
}

export function ButtonLink({
  href,
  variant = "secondary",
  size = "md",
  icon: Icon,
  block,
  children,
}: {
  href: string;
  variant?: Variant;
  size?: Size;
  icon?: LucideIcon;
  block?: boolean;
  children: ReactNode;
}) {
  return (
    <Link href={href} className={buttonClass(variant, size, block)}>
      {Icon && <Icon size={iconSize(size)} strokeWidth={1.75} aria-hidden />}
      {children}
    </Link>
  );
}

export function IconButton({
  label,
  icon: Icon,
  tone,
  size = "md",
  className,
  ...props
}: Omit<ButtonHTMLAttributes<HTMLButtonElement>, "children"> & {
  label: string;
  icon: LucideIcon;
  tone?: "danger";
  size?: Size;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      className={`icon-btn icon-btn-${size}${tone ? ` icon-btn-${tone}` : ""}${className ? ` ${className}` : ""}`}
      {...props}
    >
      <Icon size={size === "sm" ? 16 : 18} strokeWidth={1.75} aria-hidden />
    </button>
  );
}

export function Field({
  label,
  hint,
  children,
  className,
}: {
  label: ReactNode;
  hint?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <label className={`field${className ? ` ${className}` : ""}`}>
      <span className="field-label">{label}</span>
      {children}
      {hint && <small className="field-hint">{hint}</small>}
    </label>
  );
}

export function Switch({
  label,
  name,
  checked,
  defaultChecked,
  onChange,
}: {
  label: ReactNode;
  name?: string;
  checked?: boolean;
  defaultChecked?: boolean;
  onChange?: (checked: boolean) => void;
}) {
  return (
    <label className="switch">
      <input
        type="checkbox"
        role="switch"
        name={name}
        checked={checked}
        defaultChecked={defaultChecked}
        onChange={onChange && ((event) => onChange(event.target.checked))}
      />
      <span className="switch-track" aria-hidden />
      <span>{label}</span>
    </label>
  );
}

export function Tabs<T extends string>({
  items,
  value,
  onChange,
  label,
}: {
  items: readonly (readonly [T, ReactNode])[];
  value: T;
  onChange: (value: T) => void;
  label: string;
}) {
  return (
    <div className="tabs" role="tablist" aria-label={label}>
      {items.map(([key, text]) => (
        <button
          key={key}
          type="button"
          role="tab"
          aria-selected={value === key}
          className={value === key ? "selected" : undefined}
          onClick={() => onChange(key)}
        >
          {text}
        </button>
      ))}
    </div>
  );
}

export type Tone = "ok" | "warn" | "bad" | "muted" | "accent";
export function StatusPill({ tone, children }: { tone: Tone; children: ReactNode }) {
  return <span className={`pill pill-${tone}`}>{children}</span>;
}

// 成员与百业文字标识：底色按 ID 哈希取自规范色板，保证同一对象颜色稳定。
const BADGE_COLORS = ["#2F5D62", "#8A6A3D", "#6B5B8C", "#A05A4A", "#4C7A56", "#5B7699", "#8C5B73"];
export function Avatar({
  name,
  id,
  size = "md",
  square,
}: {
  name: string;
  id?: string;
  size?: "xs" | "sm" | "md" | "lg" | "xl";
  square?: boolean;
}) {
  let hash = 0;
  for (const char of id || name) hash = (hash * 31 + char.charCodeAt(0)) >>> 0;
  return (
    <span
      className={`avatar avatar-${size}${square ? " avatar-square" : ""}`}
      style={{ background: BADGE_COLORS[hash % BADGE_COLORS.length] }}
      title={name}
      aria-hidden
    >
      {name.slice(0, 1)}
    </span>
  );
}

export function EmptyState({
  icon: Icon,
  title,
  children,
  action,
  compact,
}: {
  icon?: LucideIcon;
  title?: ReactNode;
  children?: ReactNode;
  action?: ReactNode;
  compact?: boolean;
}) {
  return (
    <div className={`empty${compact ? " empty-compact" : ""}`}>
      {Icon && <Icon size={24} strokeWidth={1.75} aria-hidden />}
      {title && <strong>{title}</strong>}
      {children && <p>{children}</p>}
      {action}
    </div>
  );
}

export function Skeleton({ rows = 3 }: { rows?: number }) {
  return (
    <div className="skeleton" role="status" aria-label="正在加载">
      {Array.from({ length: rows }, (_, index) => (
        <span key={index} />
      ))}
    </div>
  );
}

function PagerFrame({
  info,
  prev,
  next,
  prevLabel = "上一页",
}: {
  info?: ReactNode;
  prev?: () => void;
  next?: () => void;
  prevLabel?: string;
}) {
  return (
    <nav className="pager" aria-label="分页">
      <Button size="sm" variant="ghost" icon={ChevronLeft} disabled={!prev} onClick={prev}>
        {prevLabel}
      </Button>
      {info && <span>{info}</span>}
      <Button size="sm" variant="ghost" disabled={!next} onClick={next}>
        下一页
        <ChevronRight size={15} strokeWidth={1.75} aria-hidden />
      </Button>
    </nav>
  );
}

export const PAGE_SIZE = 20;
export function Pager({
  page,
  total,
  setPage,
}: {
  page: number;
  total: number;
  setPage: (page: number) => void;
}) {
  if (page === 1 && total <= PAGE_SIZE) return null;
  return (
    <PagerFrame
      info={`第 ${page} / ${Math.max(1, Math.ceil(total / PAGE_SIZE))} 页 · 共 ${total} 条`}
      prev={page > 1 ? () => setPage(page - 1) : undefined}
      next={page * PAGE_SIZE < total ? () => setPage(page + 1) : undefined}
    />
  );
}

export function CursorPager({ onFirst, onNext }: { onFirst?: () => void; onNext?: () => void }) {
  if (!onFirst && !onNext) return null;
  return <PagerFrame prev={onFirst} next={onNext} prevLabel="返回最新" />;
}

function useEscape(active: boolean, close: () => void) {
  useEffect(() => {
    if (!active) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") close();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [active, close]);
}

export function Modal({
  title,
  busy = false,
  close,
  children,
}: {
  title: string;
  busy?: boolean;
  close: () => void;
  children: ReactNode;
}) {
  useEscape(!busy, close);
  return (
    <div className="overlay overlay-center" onClick={() => !busy && close()}>
      <section
        className="dialog"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onClick={(event) => event.stopPropagation()}
      >
        <header className="dialog-head">
          <h2>{title}</h2>
          <IconButton label={`关闭${title}`} icon={X} size="sm" disabled={busy} onClick={close} />
        </header>
        {children}
      </section>
    </div>
  );
}

export function Drawer({
  label,
  close,
  children,
}: {
  label: string;
  close: () => void;
  children: ReactNode;
}) {
  useEscape(true, close);
  return (
    <div className="overlay" onClick={close}>
      <aside
        className="drawer"
        role="dialog"
        aria-modal="true"
        aria-label={label}
        onClick={(event) => event.stopPropagation()}
      >
        <IconButton className="drawer-close" label={`关闭${label}`} icon={X} onClick={close} />
        {children}
      </aside>
    </div>
  );
}

/* 确认对话框：替代浏览器原生 confirm()，危险操作可要求输入指定文字。 */
type ConfirmOptions = {
  title: string;
  body: ReactNode;
  confirmLabel: string;
  danger?: boolean;
  typeToConfirm?: string;
};
type Pending = ConfirmOptions & { resolve: (ok: boolean) => void };
const ConfirmContext = createContext<(options: ConfirmOptions) => Promise<boolean>>(
  async () => false,
);
export const useConfirm = () => useContext(ConfirmContext);

export function ConfirmProvider({ children }: { children: ReactNode }) {
  const [pending, setPending] = useState<Pending | null>(null);
  const [typed, setTyped] = useState("");
  const confirm = useCallback(
    (options: ConfirmOptions) =>
      new Promise<boolean>((resolve) => {
        setTyped("");
        setPending({ ...options, resolve });
      }),
    [],
  );
  const finish = useCallback(
    (ok: boolean) => {
      pending?.resolve(ok);
      setPending(null);
    },
    [pending],
  );
  const cancel = useCallback(() => finish(false), [finish]);
  const ready = !pending?.typeToConfirm || typed === pending.typeToConfirm;
  return (
    <ConfirmContext.Provider value={confirm}>
      {children}
      {pending && (
        <Modal title={pending.title} close={cancel}>
          <div className="dialog-body">{pending.body}</div>
          {pending.typeToConfirm && (
            <Field label={`输入「${pending.typeToConfirm}」确认`}>
              <input autoFocus value={typed} onChange={(event) => setTyped(event.target.value)} />
            </Field>
          )}
          <footer className="dialog-actions">
            <Button variant="ghost" onClick={cancel}>
              取消
            </Button>
            <Button
              variant={pending.danger ? "danger" : "primary"}
              disabled={!ready}
              onClick={() => finish(true)}
            >
              {pending.confirmLabel}
            </Button>
          </footer>
        </Modal>
      )}
    </ConfirmContext.Provider>
  );
}

/* 页面主操作挂到外壳标题栏右侧，所有页面的操作位置一致。 */
const PageActionsContext = createContext<HTMLElement | null>(null);
export const PageActionsTarget = PageActionsContext.Provider;
export function PageActions({ children }: { children: ReactNode }) {
  const target = useContext(PageActionsContext);
  return target ? createPortal(children, target) : null;
}
export function usePageActionsTarget() {
  const [target, setTarget] = useState<HTMLElement | null>(null);
  const ref = useRef<(node: HTMLElement | null) => void>((node) => setTarget(node));
  return [target, ref.current] as const;
}
