"use client";
import { useRouter } from "next/navigation";
import { FormEvent, useEffect, useState } from "react";
import { api, signIn } from "@/lib/api";
import { Button, ButtonLink, Field } from "./ui";

export function AuthPage({ register }: { register: boolean }) {
  const router = useRouter();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [nickname, setNickname] = useState("");
  const [code, setCode] = useState("");
  const [challenge, setChallenge] = useState("");
  const [sentEmail, setSentEmail] = useState("");
  const [cooldown, setCooldown] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    if (!cooldown) return;
    const timer = setTimeout(() => setCooldown(cooldown - 1), 1000);
    return () => clearTimeout(timer);
  }, [cooldown]);
  async function send() {
    setBusy(true);
    setError("");
    try {
      const result = await api<{ challenge_id: string; resend_after: number }>(
        "/auth/email-registration/code",
        "POST",
        { email },
      );
      setChallenge(result.challenge_id);
      setSentEmail(email);
      setCooldown(result.resend_after);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      if (register && (!challenge || email !== sentEmail))
        throw new Error("请先为当前邮箱发送验证码");
      await signIn(
        register ? "/auth/register" : "/auth/login",
        register
          ? {
              email,
              password,
              nickname: nickname.trim() || null,
              challenge_id: challenge,
              verification_code: code,
            }
          : { email, password },
      );
      router.push("/me/week");
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <main className="login-page">
      <section className="login-card">
        <span className="brand-mark brand-mark-lg">燕</span>
        <span className="kicker">燕云十六声 · 百业工具</span>
        <h1>{register ? "邮箱注册" : "邮箱登录"}</h1>
        <p className="section-desc">
          {register ? "验证邮箱后创建账号，开始安排副本。" : "使用邮箱和密码登录百业平台。"}
        </p>
        <form className="form" onSubmit={submit}>
          <Field label="邮箱">
            <input
              required
              type="email"
              maxLength={254}
              autoComplete="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          </Field>
          {register && (
            <>
              <Field label="昵称（可选）">
                <input maxLength={32} value={nickname} onChange={(e) => setNickname(e.target.value)} />
              </Field>
              <Field
                label="验证码"
                hint={challenge ? "验证码已发送，请检查收件箱及垃圾邮件。" : undefined}
              >
                <span className="input-with-action">
                  <input
                    required
                    inputMode="numeric"
                    pattern="[0-9]{6}"
                    maxLength={6}
                    value={code}
                    onChange={(e) => setCode(e.target.value)}
                  />
                  <Button disabled={busy || cooldown > 0 || !email} onClick={send}>
                    {cooldown ? `${cooldown} 秒后重发` : "发送验证码"}
                  </Button>
                </span>
              </Field>
            </>
          )}
          <Field label="密码" hint={register ? "至少 8 位" : undefined}>
            <input
              required
              type="password"
              minLength={register ? 8 : 1}
              maxLength={128}
              autoComplete={register ? "new-password" : "current-password"}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </Field>
          {error && (
            <p role="alert" className="notice notice-bad">
              {error}
            </p>
          )}
          <Button type="submit" variant="primary" block disabled={busy}>
            {busy ? "处理中…" : register ? "验证并注册" : "登录"}
          </Button>
        </form>
        <div className="auth-switch">
          <span>{register ? "已有账号？" : "还没有账号？"}</span>
          <ButtonLink block href={register ? "/login" : "/register"}>
            {register ? "返回登录" : "邮箱注册"}
          </ButtonLink>
        </div>
        <footer>非官方玩家工具，与游戏运营方无关</footer>
      </section>
    </main>
  );
}
