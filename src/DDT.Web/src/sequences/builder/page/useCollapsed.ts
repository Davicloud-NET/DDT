// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

// The containers shown closed in this browser, for each sequence.
export function useCollapsed(sequenceId: string): [ReadonlySet<string>, (id: string) => void] {
  const key = `ddt.flow.collapsed.${sequenceId}`;
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(() => {
    try {
      const stored: unknown = JSON.parse(window.localStorage.getItem(key) ?? "[]");

      return new Set(Array.isArray(stored) ? stored.filter((id) => typeof id === "string") : []);
    } catch {
      return new Set();
    }
  });

  const toggle = (id: string) => {
    setCollapsed((current) => {
      const next = new Set(current);

      if (!next.delete(id)) {
        next.add(id);
      }

      try {
        window.localStorage.setItem(key, JSON.stringify([...next]));
      } catch {
        // If storage fails, the state only lasts while the page is open.
      }

      return next;
    });
  };

  return [collapsed, toggle];
}
