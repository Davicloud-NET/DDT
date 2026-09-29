// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

// A drawer's title: the kind of item in small text, above its name.
export function DrawerTitle({ over, children }: { over: ReactNode; children: ReactNode }) {
  return (
    <span className="flex min-w-0 flex-col gap-1">
      <span className="type-small text-muted">{over}</span>{" "}
      <span className="truncate type-subtitle">{children}</span>
    </span>
  );
}
