// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";

import { formattingLocale } from "@/i18n/i18n";
import { cx } from "@/ui/cx";

import { clockNote, firstLine, type AgentLogLevel, type MachineLogEntry } from "./log";
import { ROW_HEIGHT } from "./logRows";

const levelClass: Record<AgentLogLevel, string> = {
  Information: "text-console-muted",
  Warning: "text-console-attention",
  Error: "text-console-fail",
};

// One log line per row, showing only the message's first line. index places the row in the viewport.
export function LogRow({
  line,
  index,
  selected,
  onSelect,
}: {
  line: MachineLogEntry;
  index: number;
  selected: boolean;
  onSelect: (line: MachineLogEntry) => void;
}) {
  const { text, more } = firstLine(line.message);
  const note = clockNote(line);

  return (
    <button
      type="button"
      aria-pressed={selected}
      onClick={() => {
        onSelect(line);
      }}
      className={cx(
        "absolute inset-x-0 top-0 flex h-5 cursor-pointer items-center gap-3 px-3 text-left leading-5 whitespace-nowrap outline-none hover:bg-console-selected focus-visible:bg-console-selected",
        selected && "bg-console-selected",
      )}
      style={{ transform: `translateY(${String(index * ROW_HEIGHT)}px)` }}
    >
      <time
        dateTime={line.timestampUtc}
        title={note ?? undefined}
        className={cx(
          "shrink-0 text-console-muted",
          note !== null && "underline decoration-dotted",
        )}
      >
        {new Date(line.timestampUtc).toLocaleTimeString(formattingLocale())}
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
}
