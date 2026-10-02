// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { IconCheck } from "@tabler/icons-react";
import type { ReactNode } from "react";

import { cx } from "@/ui/cx";

// One step of the setup checklist. done is null for a step the server cannot see, which stays without a mark.
export function ChecklistStep({ done, children }: { done: boolean | null; children: ReactNode }) {
  const { t: translate } = useLingui();

  return (
    <li className="flex items-start gap-3">
      <span
        role="img"
        aria-label={done === true ? translate`Done` : translate`Open`}
        className={cx(
          "mt-0.5 flex size-5 shrink-0 items-center justify-center rounded-full",
          done === true
            ? "bg-ok-text text-panel"
            : "shadow-[inset_0_0_0_1.5px_var(--color-control)]",
        )}
      >
        {done === true ? <IconCheck aria-hidden="true" size={13} stroke={3} /> : null}
      </span>
      <span className={cx("max-w-[80ch]", done === true ? "text-muted" : "text-ink")}>
        {children}
      </span>
    </li>
  );
}
