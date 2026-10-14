"use client";

export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string,
    public details?: unknown,
  ) {
    super(message);
  }
}

let accessToken: string | null = null;
let csrfToken: string | null = null;
let refreshPromise: Promise<boolean> | null = null;
// 登录状态版本：登录、退出或其他标签页切换账号后递增，旧请求的结果一律丢弃。
let epoch = 0;
const listeners = new Set<() => void>();
let channel: BroadcastChannel | null = null;

export const sessionVersion = () => epoch;
export const token = () => accessToken;
export const idempotency = () => ({ "Idempotency-Key": crypto.randomUUID() });
const sessionChanged = () =>
  new ApiError(401, "SESSION_CHANGED", "登录状态已变更");

function setSession(next: string | null, broadcast?: "login" | "logout") {
  epoch++;
  accessToken = next;
  csrfToken = null;
  for (const fn of listeners) fn();
  if (broadcast) channel?.postMessage(broadcast);
}

export function watchSession(fn: () => void) {
  listeners.add(fn);
  if (typeof BroadcastChannel !== "undefined" && !channel) {
    channel = new BroadcastChannel("yanyun-session");
    channel.onmessage = (event) => {
      if (event.data === "logout" || event.data === "login") clearSession(false);
    };
  }
  return () => {
    listeners.delete(fn);
  };
}

export function clearSession(broadcast = true) {
  setSession(null, broadcast ? "logout" : undefined);
}

async function requestJson(url: string, init: RequestInit) {
  const controller = new AbortController();
  const abort = () => controller.abort();
  init.signal?.addEventListener("abort", abort, { once: true });
  if (init.signal?.aborted) controller.abort();
  let timedOut = false;
  const timer = setTimeout(() => {
    timedOut = true;
    controller.abort();
  }, 20000);
  try {
    const response = await fetch(url, { ...init, signal: controller.signal });
    let result;
    try {
      result = JSON.parse(await response.text());
    } catch {
      throw new ApiError(
        response.status,
        "INVALID_RESPONSE",
        response.ok ? "服务响应格式异常，请重试" : "服务暂时不可用，请稍后重试",
      );
    }
    if (!response.ok)
      throw new ApiError(
        response.status,
        result?.error?.code || "REQUEST_FAILED",
        result?.error?.message || "请求失败",
        result?.error?.details,
      );
    if (!result || typeof result !== "object" || !("data" in result))
      throw new ApiError(response.status, "INVALID_RESPONSE", "服务响应格式异常，请重试");
    return result.data;
  } catch (error) {
    if (timedOut) throw new ApiError(504, "REQUEST_TIMEOUT", "请求超时，请重试");
    if (init.signal?.aborted) throw new DOMException("请求已取消", "AbortError");
    if (error instanceof ApiError) throw error;
    throw new ApiError(0, "NETWORK_ERROR", "网络连接失败，请检查网络后重试");
  } finally {
    clearTimeout(timer);
    init.signal?.removeEventListener("abort", abort);
  }
}

async function send<T>(
  path: string,
  method: string,
  body: unknown,
  headers: Record<string, string>,
  withToken: boolean,
  signal?: AbortSignal,
): Promise<T> {
  const stamp = epoch;
  const data = await requestJson(path, {
    method,
    credentials: "include",
    cache: "no-store",
    headers: {
      ...(body === undefined ? {} : { "Content-Type": "application/json" }),
      ...(withToken && accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      ...headers,
    },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  });
  if (stamp !== epoch) throw sessionChanged();
  return data;
}

// 认证接口使用匿名凭证并缓存；注销账号需携带本人 Bearer 每次重新获取。
async function csrf(authenticated: boolean) {
  if (!authenticated && csrfToken) return csrfToken;
  const result = await send<{ request_token: string }>(
    "/api/v1/auth/csrf",
    "GET",
    undefined,
    {},
    authenticated,
  );
  if (!authenticated) csrfToken = result.request_token;
  return result.request_token;
}

export function refresh() {
  if (refreshPromise) return refreshPromise;
  const stamp = epoch;
  const attempt = async () => {
    try {
      const result = await send<{ access_token: string }>(
        "/api/v1/auth/refresh",
        "POST",
        undefined,
        { "X-CSRF-TOKEN": await csrf(false) },
        false,
      );
      if (stamp !== epoch) return false;
      accessToken = result.access_token;
      return true;
    } catch (error) {
      if (stamp !== epoch) return false;
      if (!(error instanceof ApiError)) throw error;
      if (error.code === "SESSION_CHANGED") return false;
      if (error.status === 401 || error.status === 403) {
        clearSession();
        return false;
      }
      throw error;
    }
  };
  // 多标签页共享同一刷新会话，用锁串行化轮换。
  refreshPromise = (async () =>
    typeof navigator !== "undefined" && navigator.locks
      ? await navigator.locks.request("yanyun-refresh", attempt)
      : await attempt())().finally(() => {
    refreshPromise = null;
  });
  return refreshPromise;
}

export async function api<T>(
  path: string,
  method = "GET",
  body?: unknown,
  headers: Record<string, string> = {},
  signal?: AbortSignal,
): Promise<T> {
  const stamp = epoch;
  const auth = path.startsWith("/auth/");
  const needsCsrf = method !== "GET" && (auth || path === "/me/account-deletion");
  const attempt = async () =>
    send<T>(
      `/api/v1${path}`,
      method,
      body,
      needsCsrf ? { ...headers, "X-CSRF-TOKEN": await csrf(!auth) } : headers,
      !auth,
      signal,
    );
  try {
    return await attempt();
  } catch (error) {
    if (stamp !== epoch) throw sessionChanged();
    if (!(error instanceof ApiError)) throw error;
    // Access Token 过期：刷新后用相同请求头（含幂等标识）重试一次。
    if (!auth && error.status === 401 && error.code !== "ACCOUNT_NOT_FOUND" && (await refresh())) {
      if (stamp !== epoch) throw sessionChanged();
      return attempt();
    }
    if (error.code === "ACCOUNT_NOT_FOUND" || error.code === "ACCOUNT_DISABLED") clearSession();
    throw error;
  }
}

export async function signIn(path: "/auth/login" | "/auth/register", body: unknown) {
  const result = await api<{ access_token: string }>(path, "POST", body);
  setSession(result.access_token, "login");
}

export async function logout() {
  await api("/auth/logout", "POST");
  clearSession();
}
