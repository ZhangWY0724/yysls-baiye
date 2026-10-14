"use client";

import { useState } from "react";
import { Bell, CalendarDays, Check, Pencil, Plus, Trash2, Users, X } from "lucide-react";
import { Card } from "@/components/business-ui";
import {
  Avatar,
  Button,
  ConfirmProvider,
  EmptyState,
  Field,
  IconButton,
  Pager,
  Skeleton,
  StatusPill,
  Switch,
  Tabs,
  useConfirm,
} from "@/components/ui";

/* 组件规范页：直接渲染业务页面使用的同一批组件，规范与实现不会再分叉。 */

const tokens = [
  ["--bg", "页面背景"],
  ["--surface", "卡片、抽屉"],
  ["--sunk", "泳道、分段控件底"],
  ["--line", "边框、分割线"],
  ["--text", "正文"],
  ["--muted", "次要文字"],
  ["--accent", "主色：青黛"],
  ["--accent-soft", "主色浅底"],
  ["--ok", "满员、成功"],
  ["--warn", "缺人、待处理"],
  ["--bad", "缺承伤/奶妈、危险操作"],
];

function ConfirmDemo() {
  const confirm = useConfirm();
  const [result, setResult] = useState("");
  return (
    <div className="row-wrap">
      <Button
        variant="danger-ghost"
        icon={Trash2}
        onClick={async () =>
          setResult(
            (await confirm({
              title: "移出成员",
              body: "移出后将删除该成员在本百业的全部记录。",
              confirmLabel: "移出",
              danger: true,
            }))
              ? "已确认"
              : "已取消",
          )
        }
      >
        危险操作确认
      </Button>
      <Button
        variant="danger-ghost"
        onClick={async () =>
          setResult(
            (await confirm({
              title: "永久解散百业",
              body: "输入百业名称后才能确认。",
              confirmLabel: "永久解散",
              danger: true,
              typeToConfirm: "青山",
            }))
              ? "已确认"
              : "已取消",
          )
        }
      >
        输入名称确认
      </Button>
      {result && <span className="section-desc">结果：{result}</span>}
    </div>
  );
}

export default function DesignSystemPage() {
  const [tab, setTab] = useState<"a" | "b" | "c">("a");
  const [page, setPage] = useState(2);
  return (
    <ConfirmProvider>
      <main className="gallery">
        <header className="page-head">
          <div>
            <span className="kicker">燕云百业 · 设计规范</span>
            <h1>组件与令牌</h1>
          </div>
        </header>

        <Card title="颜色令牌">
          <div className="swatch-grid">
            {tokens.map(([token, label]) => (
              <div className="swatch" key={token}>
                <span style={{ background: `var(${token})` }} />
                <strong>{token}</strong>
                <small>{label}</small>
              </div>
            ))}
          </div>
        </Card>

        <Card title="字体与字号">
          <div className="type-scale">
            <p className="type-serif" style={{ fontSize: 28 }}>页面主标题 28 · 衬线</p>
            <p className="type-serif" style={{ fontSize: 22 }}>页面标题 22 · 衬线</p>
            <p style={{ fontSize: 16, fontWeight: 600 }}>卡片标题 16 · 无衬线 600</p>
            <p style={{ fontSize: 15 }}>表单与长文 15</p>
            <p style={{ fontSize: 13 }}>正文与列表 13</p>
            <p style={{ fontSize: 12, color: "var(--muted)" }}>辅助信息与状态 12（最小字号）</p>
          </div>
        </Card>

        <Card title="按钮">
          <div className="row-wrap">
            <Button variant="primary" icon={Plus}>主要</Button>
            <Button>次要</Button>
            <Button variant="ghost">幽灵</Button>
            <Button variant="danger">危险</Button>
            <Button variant="danger-ghost" icon={Trash2}>危险弱化</Button>
            <Button variant="primary" disabled>不可用</Button>
          </div>
          <div className="row-wrap">
            <Button size="sm" variant="primary">小号主要</Button>
            <Button size="sm">小号次要</Button>
            <Button size="sm" variant="ghost" icon={Pencil}>编辑</Button>
            <Button size="sm" variant="danger-ghost">移出</Button>
            <IconButton label="标为已读" icon={Check} />
            <IconButton label="关闭" icon={X} size="sm" />
            <IconButton label="移除" icon={X} size="sm" tone="danger" />
          </div>
          <p className="section-desc">每个区域最多一个主要按钮；行内操作用小号幽灵按钮；危险操作先弹确认框，确认框里才用实底危险按钮。</p>
        </Card>

        <Card title="表单">
          <div className="form">
            <div className="form-row">
              <Field label="百业名称" hint="2–40 个字">
                <input placeholder="例如：青山" />
              </Field>
              <Field label="区服">
                <select defaultValue="official">
                  <option value="official">官服</option>
                  <option value="channel">渠道服</option>
                </select>
              </Field>
            </div>
            <Field label="简介">
              <textarea placeholder="介绍一下百业" />
            </Field>
            <div className="row-wrap">
              <Switch label="开放招募" defaultChecked />
              <label className="check">
                <input type="checkbox" defaultChecked />
                多选项
              </label>
            </div>
          </div>
        </Card>

        <Card title="状态、标识与分段">
          <div className="row-wrap">
            <StatusPill tone="ok">满员</StatusPill>
            <StatusPill tone="warn">缺 2 人</StatusPill>
            <StatusPill tone="bad">缺承伤</StatusPill>
            <StatusPill tone="accent">已发布</StatusPill>
            <StatusPill tone="muted">已结束</StatusPill>
          </div>
          <div className="row-wrap">
            {["玄山", "青禾", "鹿鸣", "云归", "砚秋", "昭月", "舟遥"].map((name) => (
              <Avatar key={name} name={name} />
            ))}
            <Avatar name="青山百业" size="lg" square />
          </div>
          <Tabs
            label="示例"
            items={[
              ["a", "成员"],
              ["b", "审批申请"],
              ["c", "参与情况"],
            ]}
            value={tab}
            onChange={setTab}
          />
        </Card>

        <Card title="空状态、加载与分页">
          <div className="split split-even">
            <EmptyState icon={CalendarDays} title="这周还没有安排" action={<Button>浏览副本</Button>}>
              去看看有哪些副本在招人。
            </EmptyState>
            <div className="stack">
              <Skeleton />
              <EmptyState compact icon={Bell}>暂无通知</EmptyState>
            </div>
          </div>
          <Pager page={page} total={86} setPage={setPage} />
        </Card>

        <Card title="确认对话框">
          <ConfirmDemo />
        </Card>

        <Card title="列表行">
          <div className="list">
            <div className="list-row">
              <Avatar name="云归" />
              <span className="list-main">
                <strong>
                  云归<StatusPill tone="muted">管理员</StatusPill>
                </strong>
                <small>鸣金虹 / 鸣金影</small>
              </span>
              <div className="list-actions">
                <Button size="sm" variant="ghost" icon={Users}>设为管理员</Button>
                <Button size="sm" variant="danger-ghost">移出</Button>
              </div>
            </div>
          </div>
        </Card>
      </main>
    </ConfirmProvider>
  );
}
