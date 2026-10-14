"use client";

export const formatTime = (minutes: number) =>
  `${String(Math.floor(minutes / 60)).padStart(2, "0")}:${String(minutes % 60).padStart(2, "0")}`;
export function OnlineTime({
  label,
  minutes,
  end = false,
  onChange,
}: {
  label: string;
  minutes: number;
  end?: boolean;
  onChange: (value: number) => void;
}) {
  return (
    <label>
      {label}
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
    </label>
  );
}
