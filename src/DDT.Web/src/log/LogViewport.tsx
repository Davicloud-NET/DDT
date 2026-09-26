// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { useLingui } from "@lingui/react/macro";
import { useEffect, useLayoutEffect, useRef, useState } from "react";

import { formattingLocale } from "@/i18n/i18n";
import { cx } from "@/ui/cx";

import { clockNote, firstLine, type AgentLogLevel, type MachineLogEntry } from "./log";

// Rows never wrap, so every row has this height, which the row class sets.
const ROW_HEIGHT = 20;

// Rows rendered above and below the visible ones, so fast scrolling shows no gap.
const OVERSCAN = 30;

// Without a layout, as in tests, the newest rows are rendered.
const UNMEASURED_ROWS = 100;

// Scrolling up further than this from the bottom pauses following.
const FOLLOW_SLACK = 2 * ROW_HEIGHT;

const levelClass: Record<AgentLogLevel, string> = {
  Information: "text-console-muted",
  Warning: "text-console-attention",
  Error: "text-console-fail",
};

export interface LogViewportProps {
  lines: readonly MachineLogEntry[];
  following: boolean;
  onFollowingChange: (following: boolean) => void;
  selectedId: number | null;
  onSelect: (line: MachineLogEntry) => void;
}

// The log on the console well, rendering only the rows in view. Following keeps the newest line in view; lines
// loaded before the first one leave the rows in view where they are.
export function LogViewport({
  lines,
  following,
  onFollowingChange,
  selectedId,
  onSelect,
}: LogViewportProps) {
  const { t } = useLingui();
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
  const locale = formattingLocale();

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
        setScrollTop(element.scrollTop);

        if (following && fromBottom > FOLLOW_SLACK) {
          onFollowingChange(false);
        } else if (!following && fromBottom < 1) {
          onFollowingChange(true);
        }
      }}
    >
      <div className="relative" style={{ height: total * ROW_HEIGHT }}>
        {lines.slice(start, end).map((line, offset) => {
          const { text, more } = firstLine(line.message);
          const note = clockNote(line);
          const selected = line.id === selectedId;

          return (
            <button
              key={line.id}
              type="button"
              aria-pressed={selected}
              onClick={() => {
                onSelect(line);
              }}
              className={cx(
                "absolute inset-x-0 top-0 flex h-5 cursor-pointer items-center gap-3 px-3 text-left leading-5 whitespace-nowrap outline-none hover:bg-console-selected focus-visible:bg-console-selected",
                selected && "bg-console-selected",
              )}
              style={{ transform: `translateY(${String((start + offset) * ROW_HEIGHT)}px)` }}
            >
              <time
                dateTime={line.timestampUtc}
                title={note ?? undefined}
                className={cx(
                  "shrink-0 text-console-muted",
                  note !== null && "underline decoration-dotted",
                )}
              >
                {new Date(line.timestampUtc).toLocaleTimeString(locale)}
              </time>
              <span className={cx("w-4 shrink-0 font-bold", levelClass[line.level])}>
                {line.level === "Error" ? "E" : line.level === "Warning" ? "W" : "I"}
              </span>
              <span
                className={cx(
                  "min-w-0 truncate",
                  line.level === "Error" && "text-console-fail",
                  line.level === "Warning" && "text-console-attention",
                )}
              >
                {text}
                {more > 0 ? (
                  <span className="text-console-muted">
                    {" "}
                    {plural(more, { one: "(+# line)", other: "(+# lines)" })}
                  </span>
                ) : null}
              </span>
            </button>
          );
        })}
      </div>
    </div>
  );
}
