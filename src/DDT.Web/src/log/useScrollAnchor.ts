// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLayoutEffect, useRef, type RefObject } from "react";

import type { MachineLogEntry } from "./log";
import { ROW_HEIGHT } from "./logRows";

// The viewport's ref. Following keeps the newest line in view; lines loaded before the first one leave the rows in
// view where they are.
export function useScrollAnchor(
  lines: readonly MachineLogEntry[],
  following: boolean,
): RefObject<HTMLDivElement | null> {
  const viewport = useRef<HTMLDivElement>(null);
  const firstShown = useRef<number | null>(null);

  useLayoutEffect(() => {
    const element = viewport.current;
    const previous = firstShown.current;
    const first = lines[0]?.id ?? null;
    firstShown.current = first;

    if (element === null) {
      return;
    }

    if (following) {
      element.scrollTop = element.scrollHeight;
    } else if (previous !== null && first !== null && first < previous) {
      const added = lines.findIndex((line) => line.id === previous);

      if (added > 0) {
        element.scrollTop += added * ROW_HEIGHT;
      }
    }
  }, [lines, following]);

  return viewport;
}
