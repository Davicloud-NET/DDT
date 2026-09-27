// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import { cx } from "./cx";

// A message in the flow of a page. "fail" is announced at once; the others wait for the reader.
export type NoticeTone = "fail" | "attention" | "info";

const tones: Record<NoticeTone, string> = {
  fail: "bg-fail text-on-fail",
  attention: "bg-attention text-on-attention",
  info: "bg-panel text-ink shadow-panel",
};

// Its actions are the keys that answer it, such as Undo, and sit under its text.
export function Notice({
  tone = "info",
  title,
  children,
  actions,
  className,
}: {
  tone?: NoticeTone;
  title?: ReactNode;
  children?: ReactNode;
  actions?: ReactNode;
  className?: string;
}) {
  return (
    <div
      role={tone === "fail" ? "alert" : "status"}
      className={cx("flex flex-col gap-1 rounded-key px-3.5 py-3", tones[tone], className)}
    >
      {title ? <span className="type-label">{title}</span> : null}
      {children ? <span className="type-small">{children}</span> : null}
      {actions ? <div className="flex flex-wrap items-center gap-2 pt-1.5">{actions}</div> : null}
    </div>
  );
}
