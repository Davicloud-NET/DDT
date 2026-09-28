// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import { Panel } from "@/ui/Panel";

export function DesignSection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Panel title={title}>
      <div className="flex flex-col gap-4">{children}</div>
    </Panel>
  );
}
