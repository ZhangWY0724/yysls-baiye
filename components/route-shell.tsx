"use client";

import { usePathname } from "next/navigation";
import type { ReactNode } from "react";
import PlatformPage from "./platform-page";

export default function RouteShell({ children }: { children: ReactNode }) {
  const path = usePathname();
  return path === "/" || path === "/design-system" ? (
    children
  ) : (
    <PlatformPage />
  );
}
