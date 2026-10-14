# 燕云百业组队平台

Next.js 15 / React 19 前端，ASP.NET Core 10 模块单体后端，PostgreSQL 16、Redis 7.4 与 SignalR。业务页面已接真实接口；QQ 登录按本次决定暂停，邮件仅用于邮箱注册验证，不发送业务邮件。无附件、头像或文件存储。

## 当前功能

- 邮箱验证码注册、密码登录、JWT、Redis 刷新会话轮换、退出和 CSRF。
- 个人资料、游戏角色、默认角色、在线时段、散人筛选。
- 百业、申请与审批、邀请码和定向邀请、成员任免、社长转让。
- 模板、副本周历、场次、报名与指派、满员候补、冲突检查、取消递补。
- 本周和累计至当前周的参与统计、站内通知、已读、开打提醒及 SignalR。
- 退会、移出、解散、注销的关联硬删除。社长退会即解散，页面二次确认。

实际开放 60 个 HTTP 操作。详细交付与验证见 [开发进度](doc/06-后端开发进度.md)，初始化资料见 [初始化与运行说明](doc/07-初始化数据与运行说明.md)。

## 本机运行

需要 .NET 10 SDK 与 Node.js。已配置的服务器 PostgreSQL/Redis 和 Resend 凭证保存在被忽略的 `apps/api/appsettings.Local.json`，Development 自动加载，发布不复制该文件。

```powershell
dotnet restore "Yanyun.slnx" --locked-mode
dotnet run --project "apps/api/Yanyun.Api.csproj" --launch-profile http
```

另一个终端启动前端：

```powershell
npm ci
npm run dev
```

前端 `http://localhost:3000`；API `http://localhost:5080`。Next.js 转发 `/api/v1` 和 `/hubs`，可通过 `API_INTERNAL_URL` 更改目标。当前转发采用 SignalR Long Polling，生产直连反向代理可另行启用 WebSocket。

Development 未配置 JWT 签名密钥时使用进程级随机密钥，重启后旧 Access Token 失效；生产必须提供有效 `Auth:SigningKey`。Data Protection 密钥需持久化，否则重启会破坏 CSRF Cookie。

| 地址 | 用途 |
|---|---|
| `/health/live` | 进程存活 |
| `/health/ready` | PostgreSQL、Redis 均可用才返回 200 |
| `/openapi/v1.json` | Development 实际接口与类型，生产不公开 |
| `/api/v1/auth/csrf` | 请求凭证；认证动作匿名获取，注销携带本人 Bearer 获取 |
| `/hubs/notifications` | 已验证账号的本人通知连接 |

## 数据库与配置

服务器项目迁移已按用户确认执行：InitialFramework、PhaseOneBusiness、QuartzScheduling；SimplifyNotifications（删除 Outbox 与 Quartz 表）待确认后执行。启动不会自动建表。正式流派字典和系统副本模板尚待用户提供；演示数据未导入正式 schema。

```powershell
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project "apps/api/Yanyun.Api.csproj"
dotnet ef migrations list --project "apps/api/Yanyun.Api.csproj"
```

已有服务器基础设施可继续使用。新环境容器说明见 `docker/compose.yml` 和 `docker/compose.server.yml`；API 的生产配置使用环境变量或部署秘密，填写数据库、Redis、JWT、Resend、前端来源域名及可信代理。Compose 不包含 Next.js 和公网 HTTPS 入口，本机无 Docker，容器运行尚未验证。

## 验证与类型生成

```powershell
dotnet test "Yanyun.slnx" -c Release --no-restore
npm run typecheck
npm run build
# API 启动后，更新实际接口生成类型。
npm run api:generate
```

真实基础设施测试需显式开启：

```powershell
$env:YANYUN_RUN_INFRA_TESTS="1"
dotnet test "Yanyun.slnx" -c Release --no-restore
```

测试使用独立随机 PostgreSQL schema 和 Redis 命名空间，验证码邮件为测试替身，隔离正式数据。测试 schema 保留，未自动 DROP。常规 CI 不连接本机秘密配置，跳过这些基础设施测试；CI 检查生成类型、编译、普通测试、迁移一致性和前端生产构建。

运行结果与尚未完成的上线验收见 [开发进度](doc/06-后端开发进度.md)。Resend 真实测试仅确认供应商受理，不代表收件方已收到；生产部署、负载、移动端和备份恢复需另行验收。
