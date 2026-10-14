import type { Metadata } from "next";
import "./globals.css";
import RouteShell from "@/components/route-shell";

export const metadata: Metadata = {
  title: "燕云十六声 · 百业组队平台",
  description: "百业副本排班协作平台",
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="zh-CN">
      <body>
        <RouteShell>{children}</RouteShell>
      </body>
    </html>
  );
}
