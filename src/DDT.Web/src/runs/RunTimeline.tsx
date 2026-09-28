// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { formattingLocale } from "@/i18n/i18n";
import { Panel } from "@/ui/Panel";

import type { TimelineEntry } from "./runs";

// The moments of a run on the server's clock, oldest first, with the date shown only when it changes.
export function RunTimeline({ entries }: { entries: readonly TimelineEntry[] }) {
  const locale = formattingLocale();
  const days = entries.map((entry) => new Date(entry.utc).toLocaleDateString(locale));

  return (
    <Panel title={<Trans>Timeline</Trans>}>
      <ol className="flex flex-col">
        {entries.map((entry, index) => {
          const moment = new Date(entry.utc);
          const day = days[index];
          const showDay = index === 0 || day !== days[index - 1];

          return (
            <li key={entry.key} className="grid grid-cols-[4.5rem_minmax(0,1fr)] gap-3 py-1.5">
              <time dateTime={entry.utc} className="type-data text-muted">
                {moment.toLocaleTimeString(locale, { hour: "2-digit", minute: "2-digit" })}
                {showDay ? <span className="block type-small">{day}</span> : null}
              </time>
              <span className="type-body text-ink">{entry.text}</span>
            </li>
          );
        })}
      </ol>
    </Panel>
  );
}
