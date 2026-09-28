// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

// A group of fields inside a section, under a heading of its own.
export function SettingsGroup({ title, children }: { title: ReactNode; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-3 border-t border-line-soft pt-4 first:border-t-0 first:pt-0">
      <h3 className="type-label text-ink">{title}</h3>
      {children}
    </section>
  );
}
