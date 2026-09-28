// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";

import { cx } from "@/ui/cx";

// The names a template field offers, as the listbox its input controls.
export function CompletionList({
  listId,
  offered,
  chosen,
  optionId,
  onInsert,
}: {
  listId: string;
  offered: readonly string[];
  chosen: number;
  optionId: (index: number) => string;
  onInsert: (name: string) => void;
}) {
  const { t } = useLingui();

  return (
    <ul
      id={listId}
      role="listbox"
      aria-label={t`Values to use`}
      className="absolute inset-x-0 top-full z-30 mt-1 max-h-64 overflow-auto rounded-overlay bg-raised p-1 shadow-overlay entering:animate-pop-in"
    >
      {offered.map((name, index) => (
        <li
          key={name}
          id={optionId(index)}
          role="option"
          aria-selected={index === chosen}
          className={cx(
            "cursor-pointer rounded-key px-2.5 py-1.5 type-data text-ink motion-highlight",
            index === chosen && "bg-hover",
          )}
          onMouseDown={(event) => {
            // The field keeps the focus.
            event.preventDefault();
          }}
          onClick={() => {
            onInsert(name);
          }}
        >
          {name}
        </li>
      ))}
    </ul>
  );
}
