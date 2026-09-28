// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Link } from "@tanstack/react-router";

import type { RuleView } from "@/rules/rules";
import { ruleName } from "@/rules/ruleText";

// The rules that give a role, each a link that opens it on the rules page.
export function RuleLinks({ rules }: { rules: readonly RuleView[] }) {
  return (
    <ul className="flex flex-col gap-0.5">
      {rules.map((rule) => (
        <li key={rule.id} className="min-w-0 truncate">
          <Link
            to="/deployment/rules"
            search={{ rule: rule.id }}
            className="text-ink hover:underline"
          >
            {ruleName(rule)}
          </Link>
        </li>
      ))}
    </ul>
  );
}
