"use client";
import Link from "next/link";
import {
  CalendarCheck,
  House,
  CalendarDays,
  UserRound,
  Users,
  Bell,
  Settings,
  LogOut,
} from "lucide-react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback, useEffect, useRef, useState } from "react";
import {
  HubConnectionBuilder,
  HubConnectionState,
  HttpTransportType,
  LogLevel,
} from "@microsoft/signalr";
import {
  api,
  logout,
  refresh,
  token,
  watchSession,
  sessionVersion,
} from "@/lib/api";
import { Me } from "@/lib/contracts";
import { AuthPage } from "./auth-page";
import {
  AccountDeletion,
  MyApplications,
  MyInvitations,
  SettingsPage,
} from "./settings-pages";
import {
  GuildHome,
  GuildList,
  GuildSettings,
  InvitationPage,
  MembersPage,
  PlayersPage,
} from "./guild-pages";
import { MyWeekPage, RaidsPage } from "./raid-pages";
import { NotificationPage } from "./notification-page";
import { RefreshProvider } from "./business-ui";
import {
  Avatar,
  Button,
  ButtonLink,
  ConfirmProvider,
  EmptyState,
  PageActionsTarget,
  Skeleton,
  usePageActionsTarget,
} from "./ui";

export default function PlatformPage() {
  return (
    <Suspense fallback={<Skeleton />}>
      <Platform />
    </Suspense>
  );
}
function Platform() {
  const path = usePathname();
  const router = useRouter();
  const params = useSearchParams();
  const publicPage = path === "/login" || path === "/register";
  const [me, setMe] = useState<Me | null>(null);
  const [ready, setReady] = useState(false);
  const [error, setError] = useState("");
  const [unread, setUnread] = useState(0);
  const [revision, setRevision] = useState(0);
  const [session, setSession] = useState(sessionVersion());
  const [actionsTarget, actionsRef] = usePageActionsTarget();
  const notificationTimer = useRef<ReturnType<typeof setTimeout> | undefined>(
    undefined,
  );
  const reload = useCallback(async () => {
    const result = await api<Me>("/me");
    setMe(result);
    setError("");
  }, []);
  const refreshUnread = useCallback(() => {
    api<{ unread_count: number }>("/notifications/unread-count")
      .then((data) => setUnread(data.unread_count))
      .catch(() => {});
  }, []);
  // 推送可能连续到达，合并 200ms 内的变更后统一刷新当前页面数据。
  const notifications = useCallback(() => {
    clearTimeout(notificationTimer.current);
    notificationTimer.current = setTimeout(() => {
      setRevision((v) => v + 1);
      refreshUnread();
    }, 200);
  }, [refreshUnread]);
  useEffect(
    () =>
      watchSession(() => {
        setMe(null);
        clearTimeout(notificationTimer.current);
        setUnread(0);
        setError("");
        setReady(false);
        setSession(sessionVersion());
        if (!token()) router.replace("/login");
      }),
    [router],
  );
  useEffect(() => {
    if (publicPage) return;
    let active = true;
    setError("");
    (async () => {
      if (!token() && !(await refresh())) {
        if (active) router.replace("/login");
        return;
      }
      const result = await api<Me>("/me");
      if (active) {
        setMe(result);
        setReady(true);
      }
    })().catch((e) => {
      if (active) {
        setError(e.message);
        setReady(true);
      }
    });
    return () => {
      active = false;
    };
  }, [publicPage, router, session]);
  useEffect(() => {
    if (publicPage || !me || me.requires_email_binding) return;
    let stopped = false;
    const connection = new HubConnectionBuilder()
      .withUrl(
        process.env.NEXT_PUBLIC_SIGNALR_URL ||
          (process.env.NODE_ENV === "development"
            ? `${window.location.protocol}//${window.location.hostname}:5080/hubs/notifications`
            : "/hubs/notifications"),
        {
          accessTokenFactory: async () => {
            if (!token()) await refresh();
            return token() || "";
          },
          transport:
            HttpTransportType.WebSockets |
            HttpTransportType.ServerSentEvents |
            HttpTransportType.LongPolling,
        },
      )
      .withAutomaticReconnect([0, 2000, 10000, 30000])
      .configureLogging(LogLevel.None)
      .build();
    // 任何推送都可能改变百业成员关系或角色（通过申请、任免、转让、移出等），同步刷新账户信息。
    connection.on("notifications.changed", async () => {
      notifications();
      await reload().catch(() => {});
    });
    connection.onreconnected(() => {
      notifications();
      void reload().catch(() => {});
    });
    let retry: ReturnType<typeof setTimeout> | undefined;
    let startedOnce = false;
    async function start() {
      if (stopped || connection.state !== HubConnectionState.Disconnected)
        return;
      try {
        if ((startedOnce || !token()) && !(await refresh())) return;
        if (stopped) return;
        startedOnce = true;
        await connection.start();
        if (!stopped) refreshUnread();
      } catch {
        if (!stopped) retry = setTimeout(() => void start(), 5000);
      }
    }
    connection.onclose(async () => {
      if (!stopped) retry = setTimeout(() => void start(), 5000);
    });
    // Strict Mode 的试运行会先清理 effect；延后启动避免创建马上被关闭的连接。
    const initialStart = setTimeout(() => void start(), 0);
    return () => {
      stopped = true;
      clearTimeout(initialStart);
      clearTimeout(retry);
      clearTimeout(notificationTimer.current);
      void connection.stop();
    };
  }, [
    publicPage,
    me?.user_id,
    me?.requires_email_binding,
    notifications,
    refreshUnread,
    reload,
  ]);
  const lastFocusRefresh = useRef(Date.now());
  useEffect(() => {
    if (publicPage || !me) return;
    const fn = () => {
      if (
        document.visibilityState !== "visible" ||
        Date.now() - lastFocusRefresh.current < 30000
      )
        return;
      lastFocusRefresh.current = Date.now();
      notifications();
      void reload().catch(() => {});
    };
    const onOnline = () => {
      lastFocusRefresh.current = 0;
      fn();
    };
    window.addEventListener("focus", fn);
    window.addEventListener("online", onOnline);
    return () => {
      window.removeEventListener("focus", fn);
      window.removeEventListener("online", onOnline);
    };
  }, [publicPage, me?.user_id, notifications, reload]);
  if (publicPage)
    return <AuthPage key={path} register={path === "/register"} />;
  if (!ready || (error && !me))
    return (
      <div className="platform platform-state">
        <header className="platform-topbar">
          <Link className="platform-brand" href="/login">
            <span className="brand-mark">燕</span>
            <strong>百业组队</strong>
          </Link>
        </header>
        <main className="state-panel-wrap">
          {error ? (
            <section className="card state-panel" role="alert">
              <span className="kicker">连接状态</span>
              <h1>暂时无法加载页面</h1>
              <p>{error}</p>
              <Button variant="primary" onClick={() => location.reload()}>
                重试
              </Button>
            </section>
          ) : (
            <Skeleton rows={4} />
          )}
        </main>
      </div>
    );
  if (!me) return <Skeleton />;
  const guild = me.membership;
  const parts = path.split("/").filter(Boolean);
  const guildId = parts[0] === "guilds" ? parts[1] : undefined;
  const myGuild = guild?.guild_id;
  const inMyGuild = !!myGuild && guildId === myGuild;
  const nav = [
    { href: "/me/week", label: "我的本周", icon: CalendarCheck, active: path === "/me/week" },
    { href: "/players", label: "玩家", icon: UserRound, active: parts[0] === "players" },
    { href: "/guilds", label: "百业", icon: Users, active: parts[0] === "guilds" && !inMyGuild },
    { href: "/notifications", label: "消息", icon: Bell, active: path === "/notifications", badge: unread },
  ];
  const guildSections: Record<string, string> = {
    raids: "副本周历",
    members: "成员管理",
    settings: "百业设置",
  };
  const titles: Record<string, string> = {
    "/me/week": "我的本周",
    "/guilds": "百业",
    "/players": "玩家广场",
    "/notifications": "通知中心",
    "/settings": "个人设置",
    "/settings/applications": "我的申请",
    "/settings/invitations": "收到的邀请",
    "/settings/account-deletion": "注销账号",
  };
  // 百业内页面以百业名作上下文，页面名作标题；其余页面统一归属“百业协作”。
  const title =
    titles[path] ||
    (parts[0] === "players" ? "玩家资料" : parts[0] === "invitations" ? "百业邀请" : guildId ? guildSections[parts[2]] || "百业主页" : "百业平台");
  const kicker = guildId && inMyGuild ? guild!.guild_name : guildId ? "百业" : "百业协作";
  let content;
  if (path === "/settings/account-deletion")
    content = <AccountDeletion me={me} />;
  else if (me.requires_email_binding)
    content = (
      <EmptyState
        title="当前账号尚未验证邮箱"
        action={<ButtonLink href="/settings/account-deletion" variant="danger-ghost">注销账号</ButtonLink>}
      >
        QQ 登录暂缓，验证邮箱后才能使用百业功能。
      </EmptyState>
    );
  else if (path === "/me/week")
    content = <MyWeekPage me={me} unread={unread} />;
  else if (path === "/settings")
    content = <SettingsPage me={me} reload={reload} />;
  else if (path === "/settings/applications") content = <MyApplications />;
  else if (path === "/settings/invitations") content = <MyInvitations />;
  else if (path === "/guilds") content = <GuildList me={me} reload={reload} />;
  else if (parts[0] === "guilds" && guildId && parts.length === 2)
    content = <GuildHome id={guildId} me={me} reload={reload} />;
  else if (parts[0] === "guilds" && guildId && parts[2] === "settings")
    content = <GuildSettings id={guildId} me={me} reload={reload} />;
  else if (parts[0] === "guilds" && guildId && parts[2] === "members")
    content = <MembersPage id={guildId} me={me} reload={reload} />;
  else if (parts[0] === "guilds" && guildId && parts[2] === "raids")
    content = (
      <RaidsPage id={guildId} me={me} eventId={params.get("event") || undefined} />
    );
  else if (parts[0] === "players")
    content = <PlayersPage id={parts[1]} me={me} />;
  else if (parts[0] === "invitations")
    content = <InvitationPage path={path} reload={reload} />;
  else if (path === "/notifications")
    content = (
      <NotificationPage
        changed={() => {
          notifications();
          void reload().catch(() => {});
        }}
      />
    );
  else
    content = (
      <EmptyState title="页面不存在" action={<ButtonLink href="/me/week">返回我的本周</ButtonLink>} />
    );
  return (
    <ConfirmProvider>
      <div className="platform">
        <header className="platform-topbar">
          <Link className="platform-brand" href="/me/week">
            <span className="brand-mark">燕</span>
            <strong>百业组队</strong>
          </Link>
          <details className="user-menu">
            <summary>
              <Avatar name={me.nickname} id={me.user_id} size="sm" />
              <span>{me.nickname}</span>
            </summary>
            <div className="menu">
              <Link href="/settings">
                <Settings size={16} strokeWidth={1.75} aria-hidden />
                个人设置
              </Link>
              <button type="button" className="menu-danger" onClick={() => logout().catch((e) => setError(e.message))}>
                <LogOut size={16} strokeWidth={1.75} aria-hidden />
                退出登录
              </button>
            </div>
          </details>
        </header>
        <div className="platform-layout">
          <aside className="platform-side" aria-label="主导航">
            <small>工作台</small>
            {nav.map((item) => (
              <Link
                className={item.active ? "current" : undefined}
                aria-current={item.active ? "page" : undefined}
                key={item.href}
                href={item.href}
              >
                <item.icon size={18} strokeWidth={1.75} aria-hidden />
                <span className="nav-label">{item.label}</span>
                {!!item.badge && <b className="nav-badge">{item.badge}</b>}
              </Link>
            ))}
            {guild && (
              <>
                <small>我的百业</small>
                <Link
                  href={`/guilds/${myGuild}`}
                  className={inMyGuild && parts.length === 2 ? "current" : undefined}
                >
                  <House size={18} strokeWidth={1.75} aria-hidden />
                  <span className="nav-label">{guild.guild_name}</span>
                </Link>
                {Object.entries(guildSections)
                  .filter(([key]) => key !== "settings" || guild.role !== "member")
                  .map(([key, label]) => (
                    <Link
                      key={key}
                      className={`subnav${inMyGuild && parts[2] === key ? " current" : ""}`}
                      href={`/guilds/${myGuild}/${key}`}
                    >
                      {label}
                    </Link>
                  ))}
              </>
            )}
          </aside>
          <main className={`platform-main${parts[2] === "raids" ? " platform-main-wide" : ""}`}>
            {error && (
              <div className="notice notice-warn" role="status">
                <span>暂时无法更新账户信息，当前页面仍可继续使用。</span>
                <Button
                  size="sm"
                  onClick={() => {
                    setError("");
                    void reload().catch((cause) => setError(cause.message));
                  }}
                >
                  重试
                </Button>
              </div>
            )}
            <div className="page-head">
              <div>
                <span className="kicker">{kicker}</span>
                <h1>{title}</h1>
              </div>
              <div className="page-actions" ref={actionsRef} />
            </div>
            <div className="page-body" key={`${path}:${me.user_id}`}>
              <PageActionsTarget value={actionsTarget}>
                <RefreshProvider value={revision}>{content}</RefreshProvider>
              </PageActionsTarget>
            </div>
          </main>
        </div>
        <nav className="mobile-tabs" aria-label="底部导航">
          <Link className={path === "/me/week" ? "active" : undefined} href="/me/week">
            <CalendarCheck size={24} strokeWidth={1.75} aria-hidden />
            <span>我的本周</span>
          </Link>
          <Link
            className={parts[2] === "raids" ? "active" : undefined}
            href={myGuild ? `/guilds/${myGuild}/raids` : "/guilds"}
          >
            <CalendarDays size={24} strokeWidth={1.75} aria-hidden />
            <span>副本</span>
          </Link>
          <Link
            className={parts[0] === "guilds" && parts[2] !== "raids" ? "active" : undefined}
            href="/guilds"
          >
            <Users size={24} strokeWidth={1.75} aria-hidden />
            <span>百业</span>
          </Link>
          <Link className={path === "/notifications" ? "active" : undefined} href="/notifications">
            <Bell size={24} strokeWidth={1.75} aria-hidden />
            <span>消息</span>
            {!!unread && <b className="nav-badge">{unread}</b>}
          </Link>
        </nav>
      </div>
    </ConfirmProvider>
  );
}
