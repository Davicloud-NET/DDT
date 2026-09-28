// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useNavigate, useSearch } from "@tanstack/react-router";
import { useState } from "react";

import type { RuleView } from "./rules";

// The rule whose drawer is open, as it was when it opened; null for a new one. key opens a fresh form each time.
export interface OpenRule {
  key: number;
  rule: RuleView | null;
}

// Which rule's drawer the rules page shows. A rule named in the address, such as from a machine role's page, opens
// once the list is there.
export function useRuleDrawer(rules: readonly RuleView[] | undefined) {
  const search = useSearch({ from: "/shell/deployment/rules" });
  const navigate = useNavigate({ from: "/deployment/rules" });
  const [drawer, setDrawer] = useState<OpenRule | null>(null);
  const [linked, setLinked] = useState(search.rule ?? null);

  if (linked !== null && rules !== undefined) {
    const found = rules.find((rule) => rule.id === linked);

    setLinked(null);

    if (found !== undefined) {
      setDrawer({ key: 1, rule: found });
    }
  }

  return {
    drawer,
    open: (rule: RuleView | null) => {
      setDrawer((current) => ({ key: (current?.key ?? 0) + 1, rule }));
    },
    close: () => {
      setDrawer(null);

      if (search.rule !== undefined) {
        void navigate({ search: {}, replace: true });
      }
    },
    // The drawer stays open as the rule a save made.
    showSaved: (saved: RuleView) => {
      setDrawer((current) => (current === null ? current : { ...current, rule: saved }));
    },
  };
}
