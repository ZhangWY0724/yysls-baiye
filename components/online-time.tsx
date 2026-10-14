"use client";
import { Field } from "./ui";

export const formatTime = (minutes: number) =>
  `${String(Math.floor(minutes / 60)).padStart(2, "0")}:${String(minutes % 60).padStart(2, "0")}`;
// 结束时间 00:00 表示当天 24:00（1440 分钟）。
export function OnlineTime({
  label,
  minutes,
  end = false,
  hideLabel = false,
  onChange,
}: {
  label: string;
  minutes: number;
  end?: boolean;
  hideLabel?: boolean;
  onChange: (value: number) => void;
}) {
  const input = (
    <input
      aria-label={label}
      type="time"
      required
      value={formatTime(minutes === 1440 ? 0 : minutes)}
      onChange={(event) => {
        if (!event.target.value) return;
        const [hour, minute] = event.target.value.split(":").map(Number);
        const value = hour * 60 + minute;
        onChange(end && value === 0 ? 1440 : value);
      }}
    />
  );
  return hideLabel ? input : <Field label={label}>{input}</Field>;
}
