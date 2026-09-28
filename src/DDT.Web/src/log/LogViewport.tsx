// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { useState } from "react";

import { useElementSize } from "@/ui/useElementSize";

import type { MachineLogEntry } from "./log";
import { followingAfterScroll, ROW_HEIGHT, rowsInView } from "./logRows";
import { LogRow } from "./LogRow";
import { useScrollAnchor } from "./useScrollAnchor";

export interface LogViewportProps {
  lines: readonly MachineLogEntry[];
  following: boolean;
  onFollowingChange: (following: boolean) => void;
  selectedId: number | null;
  onSelect: (line: MachineLogEntry) => void;
}

// The log on the console well. It renders only the rows in view, because a log holds up to MAX_BUFFERED_LINES lines.
export function LogViewport({
  lines,
  following,
  onFollowingChange,
  selectedId,
  onSelect,
}: LogViewportProps) {
  const { t } = useLingui();
  const viewport = useScrollAnchor(lines, following);
  const [scrollTop, setScrollTop] = useState(0);
  const height = useElementSize(viewport)?.height ?? 0;
  const total = lines.length;
  const { start, end } = rowsInView({ total, height, scrollTop, following });

  return (
    <div
      ref={viewport}
      role="log"
      aria-live="off"
      aria-label={t`Log lines`}
      tabIndex={0}
      className="h-[26rem] overflow-auto rounded-key bg-console font-mono text-[12.5px] text-console-text outline-none focus-visible:outline-2 focus-visible:outline-focus"
      onScroll={(event) => {
        const element = event.currentTarget;
        const fromBottom = element.scrollHeight - element.scrollTop - element.clientHeight;
        const next = followingAfterScroll(following, fromBottom);
        setScrollTop(element.scrollTop);

        if (next !== following) {
          onFollowingChange(next);
        }
      }}
    >
      <div className="relative" style={{ height: total * ROW_HEIGHT }}>
        {lines.slice(start, end).map((line, offset) => (
          <LogRow
            key={line.id}
            line={line}
            index={start + offset}
            selected={line.id === selectedId}
            onSelect={onSelect}
          />
        ))}
      </div>
    </div>
  );
}
