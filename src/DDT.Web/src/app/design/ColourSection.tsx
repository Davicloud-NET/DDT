// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { DesignSection } from "./DesignSection";

const colours = [
  "frame",
  "page",
  "panel",
  "raised",
  "well",
  "field",
  "hover",
  "selected",
  "line",
  "line-soft",
  "control",
  "ink",
  "ink-2",
  "muted",
  "run",
  "attention",
  "fail",
  "ok",
  "key-primary",
  "rail-done",
  "console",
];

export function ColourSection() {
  return (
    <DesignSection title="Colour">
      <div className="grid grid-cols-[repeat(auto-fill,minmax(7.5rem,1fr))] gap-3">
        {colours.map((name) => (
          <div key={name} className="flex flex-col gap-1">
            <span
              className="h-10 rounded-key shadow-[inset_0_0_0_1px_var(--color-line)]"
              style={{ background: `var(--color-${name})` }}
            />
            <span className="type-data text-ink-2">{name}</span>
          </div>
        ))}
      </div>
    </DesignSection>
  );
}
