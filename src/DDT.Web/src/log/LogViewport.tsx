// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useEffect, useLayoutEffect, useRef, useState } from "react";

import { cx } from "@/lib/cx";

import { clockNote, firstLine, type MachineLogEntry } from "./log";

import styles from "./LogViewport.module.scss";

// The row height in LogViewport.module.scss; rows never wrap, so every row has it.
const ROW_HEIGHT = 20;
// Rows rendered above and below the visible ones, so fast scrolling shows no gap.
const OVERSCAN = 30;
// Without a layout, as in tests, the newest rows are rendered.
const UNMEASURED_ROWS = 100;
// Scrolling up further than this from the bottom pauses following.
const FOLLOW_SLACK = 2 * ROW_HEIGHT;

export interface LogViewportProps {
  lines: readonly MachineLogEntry[];
  following: boolean;
  onFollowingChange: (following: boolean) => void;
  selectedId: number | null;
  onSelect: (line: MachineLogEntry) => void;
}

// Renders only the rows in view. Following keeps the newest line in view; lines loaded before the first one
// leave the rows in view where they are.
export function LogViewport({
  lines,
  following,
  onFollowingChange,
  selectedId,
  onSelect,
}: LogViewportProps) {
  const viewport = useRef<HTMLDivElement>(null);
  const firstShown = useRef<number | null>(null);
  const [scrollTop, setScrollTop] = useState(0);
  const [height, setHeight] = useState(0);

  // A ResizeObserver reports the size once when it starts observing.
  useEffect(() => {
    const element = viewport.current;

    if (element === null || typeof ResizeObserver === "undefined") {
      return;
    }

    const observer = new ResizeObserver(() => {
      setHeight(element.clientHeight);
    });
    observer.observe(element);

    return () => {
      observer.disconnect();
    };
  }, []);

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

  const total = lines.length;
  const visible = Math.ceil(height / ROW_HEIGHT);
  const start =
    height === 0
      ? Math.max(0, total - UNMEASURED_ROWS)
      : following
        ? Math.max(0, total - visible - OVERSCAN)
        : Math.max(0, Math.floor(scrollTop / ROW_HEIGHT) - OVERSCAN);
  const end = height === 0 || following ? total : Math.min(total, start + visible + 2 * OVERSCAN);

  return (
    <div
      ref={viewport}
      className={styles.viewport}
      role="log"
      aria-live="off"
      aria-label="Log lines"
      tabIndex={0}
      onScroll={(event) => {
        const element = event.currentTarget;
        const fromBottom = element.scrollHeight - element.scrollTop - element.clientHeight;
        setScrollTop(element.scrollTop);

        if (following && fromBottom > FOLLOW_SLACK) {
          onFollowingChange(false);
        } else if (!following && fromBottom < 1) {
          onFollowingChange(true);
        }
      }}
    >
      <div className={styles.spacer} style={{ height: total * ROW_HEIGHT }}>
        {lines.slice(start, end).map((line, offset) => {
          const { text, more } = firstLine(line.message);
          const note = clockNote(line);

          return (
            <button
              key={line.id}
              type="button"
              className={cx(styles.row, line.id === selectedId && styles.selected)}
              style={{ transform: `translateY(${String((start + offset) * ROW_HEIGHT)}px)` }}
              data-level={line.level}
              aria-pressed={line.id === selectedId}
              onClick={() => {
                onSelect(line);
              }}
            >
              <time
                className={cx(styles.time, note !== null && styles.skewed)}
                dateTime={line.timestampUtc}
                title={note ?? undefined}
              >
                {new Date(line.timestampUtc).toLocaleTimeString()}
              </time>
              <span className={styles.level}>{line.level}</span>
              <span className={styles.message}>
                {text}
                {more > 0 && <span className={styles.more}> (+{String(more)} lines)</span>}
              </span>
            </button>
          );
        })}
      </div>
    </div>
  );
}
