// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { useNow } from "@/lib/useNow";
import { Panel } from "@/ui/Panel";

import type { SectionSummary } from "../settings";

import { SectionRow } from "./SectionRow";

// Every settings section, with how it stands and a link to the page that edits it.
export function SectionsPanel({ sections }: { sections: SectionSummary[] }) {
  const { t } = useLingui();
  const now = useNow(30_000);

  return (
    <Panel title={<Trans>Settings sections</Trans>} flush>
      <p className="max-w-[80ch] px-4 pt-3 text-ink-2">
        <Trans>
          Each section is saved on the page of what it configures, and applies without a restart:
          some at their next use, others by rebuilding their part of the running server.
        </Trans>
      </p>
      <ul aria-label={t`Settings sections`} className="flex flex-col">
        {sections.map((summary) => (
          <SectionRow key={summary.section} summary={summary} now={now} />
        ))}
      </ul>
    </Panel>
  );
}
