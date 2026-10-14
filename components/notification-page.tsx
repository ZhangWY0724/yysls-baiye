"use client";
import Link from "next/link";
import { useState } from "react";
import { api } from "@/lib/api";
import { Notice, NoticePage } from "@/lib/contracts";
import {
  Card,
  date,
  Empty,
  QueryState,
  useAction,
  useQuery,
} from "./business-ui";
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
  return "/me/week";
}
export function NotificationPage({ changed }: { changed: () => void }) {
  const [cursor, setCursor] = useState<string | null>(null);
  const [unread, setUnread] = useState(false);
  const query = useQuery<NoticePage>(
    `/notifications?unread_only=${unread}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}&limit=20`,
  );
  const action = useAction(changed);
  return (
    <Card
      className="notification-card"
      title="站内通知"
      actions={
        <button
          className="button button-secondary compact"
          disabled={action.busy}
          onClick={() =>
            action.run(() => api("/notifications/read", "POST", { all: true }))
          }
        >
          全部已读
        </button>
      }
    >
      {action.feedback}
      <label className="real-checkbox notification-filter">
        <input
          type="checkbox"
          checked={unread}
          onChange={(e) => {
            setUnread(e.target.checked);
            setCursor(null);
          }}
        />
        只看未读
      </label>
      <QueryState {...query} retry={query.reload}>
        {!query.data?.items.length && <Empty>暂无通知</Empty>}
        {query.data?.items.map((notice) => (
          <div
            className={`real-row ${notice.read_at ? "" : "real-unread"}`}
            key={notice.id}
          >
            <div>
              <strong>{notice.title}</strong>
              <p>{notice.body}</p>
              <small>{date(notice.created_at)}</small>
            </div>
            <Link
              href={noticeLink(notice)}
              onClick={() => {
                if (!notice.read_at)
                  action.run(
                    () =>
                      api("/notifications/read", "POST", {
                        notification_ids: [notice.id],
                      }),
                    "已标为已读",
                  );
              }}
            >
              查看
            </Link>
            {!notice.read_at && (
              <button
                className="button button-secondary compact"
                disabled={action.busy}
                onClick={() =>
                  action.run(() =>
                    api("/notifications/read", "POST", {
                      notification_ids: [notice.id],
                    }),
                  )
                }
              >
                已读
              </button>
            )}
          </div>
        ))}
        <div className="real-actions notification-pagination">
          <button
            className="button button-secondary compact"
            disabled={!cursor}
            onClick={() => setCursor(null)}
          >
            返回最新
          </button>
          <button
            className="button button-secondary compact"
            disabled={!query.data?.next_cursor}
            onClick={() => setCursor(query.data!.next_cursor)}
          >
            下一页
          </button>
        </div>
      </QueryState>
    </Card>
  );
}
