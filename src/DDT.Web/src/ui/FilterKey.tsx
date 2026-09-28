// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";
import { ToggleButton } from "react-aria-components";

import { cx } from "./cx";

export interface FilterOption {
  id: string;
  label: ReactNode;
  count?: number;
  tone?: "attention" | "fail";
}

// The well FilterSelector and FilterChips hold their keys in.
export const filterWellClass =
  "flex flex-wrap gap-0.5 rounded-panel bg-well p-0.75 shadow-[inset_0_0_0_1px_var(--color-line-soft)]";

// Each key carries its count; a count that needs someone (waiting, failed) is printed on a signal-coloured tag.
export function FilterKey({ option }: { option: FilterOption }) {
  return (
    <ToggleButton
      id={option.id}
      className={cx(
        "flex h-8 cursor-pointer items-center gap-2 rounded-key px-2.75 type-label font-semibold text-ink-2 motion-colors outline-none",
        "hover:text-ink selected:bg-raised selected:text-ink selected:shadow-[0_0_0_1px_var(--color-line)]",
        "focus-visible:outline-2 focus-visible:outline-focus",
      )}
    >
      <span>{option.label}</span>
      {option.count !== undefined ? (
        <span
          className={cx(
            "inline-flex h-5 min-w-5 items-center justify-center rounded-tag px-1 type-numeral motion-colors",
            option.tone === "attention" && option.count > 0 && "bg-attention text-on-attention",
            option.tone === "fail" && option.count > 0 && "bg-fail text-on-fail",
            (option.tone === undefined || option.count === 0) && "text-muted",
          )}
        >
          {option.count}
        </span>
      ) : null}
    </ToggleButton>
  );
}
