"use client";
import Link from "next/link";
import { useState } from "react";
import { api } from "@/lib/api";
import { Notice, NoticePage } from "@/lib/contracts";
import { Bell, Check, CheckCheck } from "lucide-react";
import { Card, date, QueryState, useAction, useQuery } from "./business-ui";
import { Button, CursorPager, EmptyState, IconButton, PageActions, Tabs } from "./ui";
export function noticeLink(notice: Notice) {
  const target = notice.target;
  if (target.kind === "raid_event" && target.guild_id && target.event_id)
    return `/guilds/${target.guild_id}/raids?event=${target.event_id}`;
  if (target.kind === "invitation" && target.invitation_id)
    return `/invitations/${target.invitation_id}`;
  if (target.kind === "application")
    return notice.type === "application.received" && target.guild_id
      ? `/guilds/${target.guild_id}/members?tab=applications`
      : "/settings/applications";
  if (target.guild_id) return `/guilds/${target.guild_id}`;
  // 移出、解散类通知不再挂靠已删除的百业，跳转百业列表。
  if (target.kind === "guild") return "/guilds";
  return "/me/week";
}
export function NotificationPage({ changed }: { changed: () => void }) {
  const [cursor, setCursor] = useState<string | null>(null);
  const [filter, setFilter] = useState<"all" | "unread">("all");
  const query = useQuery<NoticePage>(
    `/notifications?unread_only=${filter === "unread"}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}&limit=20`,
  );
  const action = useAction(changed);
  const markRead = (notice: Notice, message?: string) =>
    action.run(() => api("/notifications/read", "POST", { notification_ids: [notice.id] }), message);
  return (
    <div className="stack">
      {action.feedback}
      <PageActions>
        <Button
          icon={CheckCheck}
          disabled={action.busy}
          onClick={() => action.run(() => api("/notifications/read", "POST", { all: true }), "已全部标为已读")}
        >
          全部已读
        </Button>
      </PageActions>
      <div className="toolbar">
        <Tabs
          label="通知筛选"
          items={[
            ["all", "全部"],
            ["unread", "未读"],
          ]}
          value={filter}
          onChange={(next) => {
            setFilter(next);
            setCursor(null);
          }}
        />
      </div>
      <Card>
        <QueryState {...query} retry={query.reload}>
          {!query.data?.items.length && (
            <EmptyState icon={Bell} title={filter === "unread" ? "没有未读通知" : "暂无通知"}>
              申请、邀请和排班变化会在这里提醒你。
            </EmptyState>
          )}
          <div className="list">
            {query.data?.items.map((notice) => (
              <div className={`list-row notice-row${notice.read_at ? "" : " is-unread"}`} key={notice.id}>
                <Link
                  className="list-link"
                  href={noticeLink(notice)}
                  onClick={() => {
                    if (!notice.read_at) void markRead(notice, "已标为已读");
                  }}
                >
                  <span className="unread-dot" aria-label={notice.read_at ? undefined : "未读"} />
                  <span className="list-main">
                    <strong>{notice.title}</strong>
                    <span className="list-desc">{notice.body}</span>
                    <small>{date(notice.created_at)}</small>
                  </span>
                </Link>
                {!notice.read_at && (
                  <IconButton
                    label="标为已读"
                    icon={Check}
                    size="sm"
                    disabled={action.busy}
                    onClick={() => markRead(notice)}
                  />
                )}
              </div>
            ))}
          </div>
          <CursorPager
            onFirst={cursor ? () => setCursor(null) : undefined}
            onNext={query.data?.next_cursor ? () => setCursor(query.data!.next_cursor) : undefined}
          />
        </QueryState>
      </Card>
    </div>
  );
}
