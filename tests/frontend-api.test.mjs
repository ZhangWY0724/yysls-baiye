import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import ts from 'typescript';

const source = await readFile(new URL('../lib/api.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
let sequence = 0;
const fresh = () => import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}#${sequence++}`);
const response = (data) => new Response(JSON.stringify({ data }), { status: 200 });

test('登录通知本标签页，旧账号迟到响应被拒绝', async () => {
  const client = await fresh();
  let finish; let changes = 0;
  globalThis.fetch = async (url) => {
    if (url.endsWith('/csrf')) return response({ request_token: 'test-csrf' });
    if (url.endsWith('/login')) return response({ access_token: 'test-token' });
    return new Promise(resolve => { finish = resolve; });
  };
  // 测试环境不建立跨标签页资源。
  const original = globalThis.BroadcastChannel; globalThis.BroadcastChannel = undefined;
  const stop = client.watchSession(() => changes++);
  const old = client.api('/me');
  await client.signIn('/auth/login', {});
  assert.equal(changes, 1); assert.equal(client.token(), 'test-token');
  finish(response({ user_id: 'old-user' }));
  await assert.rejects(old, error => error.code === 'SESSION_CHANGED');
  stop(); globalThis.BroadcastChannel = original;
});

test('代理 HTML 和网络失败返回稳定错误', async () => {
  const client = await fresh();
  globalThis.fetch = async () => new Response('<html>gateway</html>', { status: 502 });
  await assert.rejects(client.api('/broken'), error => error.code === 'INVALID_RESPONSE' && error.status === 502);
  globalThis.fetch = async () => { throw new TypeError('fetch failed'); };
  await assert.rejects(client.api('/offline'), error => error.code === 'NETWORK_ERROR');
});

test('Access Token 过期后刷新并以相同幂等标识重试一次', async () => {
  const client = await fresh();
  const keys = []; let refreshed = 0;
  globalThis.fetch = async (url, init) => {
    if (url.endsWith('/csrf')) return response({ request_token: 'test-csrf' });
    if (url.endsWith('/auth/refresh')) { refreshed++; return response({ access_token: 'fresh-token' }); }
    keys.push([init.headers['Idempotency-Key'], init.headers.Authorization]);
    if (keys.length === 1) return new Response(JSON.stringify({ error: { code: 'TOKEN_EXPIRED', message: '请登录后继续' } }), { status: 401 });
    return response({ saved: true });
  };
  assert.deepEqual(await client.api('/guilds', 'POST', {}, { 'Idempotency-Key': 'key-1' }), { saved: true });
  assert.equal(refreshed, 1);
  assert.deepEqual(keys, [['key-1', undefined], ['key-1', 'Bearer fresh-token']]);
});

test('匿名 CSRF 凭证复用，注销账号每次携带 Bearer 重新获取', async () => {
  const client = await fresh();
  const csrfCalls = [];
  globalThis.fetch = async (url, init) => {
    if (url.endsWith('/csrf')) { csrfCalls.push(init.headers.Authorization); return response({ request_token: `csrf-${csrfCalls.length}` }); }
    if (url.endsWith('/login')) return response({ access_token: 'test-token' });
    return response({ ok: true });
  };
  const original = globalThis.BroadcastChannel; globalThis.BroadcastChannel = undefined;
  await client.api('/auth/email-registration/code', 'POST', {});
  await client.signIn('/auth/login', {});
  await client.api('/me/account-deletion', 'POST', {});
  await client.api('/me/account-deletion', 'POST', {});
  assert.deepEqual(csrfCalls, [undefined, 'Bearer test-token', 'Bearer test-token']);
  globalThis.BroadcastChannel = original;
});
