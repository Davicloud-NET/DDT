// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { fullTime, relativeTime } from "@/lib/relativeTime";
import { StateTag } from "@/ui/StateTag";

import { ApplyStates } from "../parts/ApplyStates";
import type { SectionSummary } from "../settings";

import { SectionLink } from "./SectionLink";

export function SectionRow({ summary, now }: { summary: SectionSummary; now: number }) {
  const locked = summary.lockedCount;
  const problems = summary.problemCount;
  const version = summary.version;
  const savedBy = summary.updatedBy;
  const savedWhen = summary.updatedUtc === null ? null : relativeTime(summary.updatedUtc, now);

  return (
    <li className="flex flex-col gap-1.5 border-b border-line-soft px-4 py-3 last:border-b-0">
      <span className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
        <span className="flex-1 type-label">
          <SectionLink section={summary.section} />
        </span>
        {locked > 0 ? (
          <StateTag tone="idle">
            {plural(locked, { one: "# set in configuration", other: "# set in configuration" })}
          </StateTag>
        ) : null}
        {problems > 0 ? (
          <StateTag tone="fail">
            {plural(problems, { one: "# problem", other: "# problems" })}
          </StateTag>
        ) : null}
      </span>
      <span className="flex flex-wrap gap-x-3 gap-y-0.5 type-small text-muted">
        <span>
          {summary.kind === "Live" ? (
            <Trans>Applies at its next use</Trans>
          ) : (
            <Trans>Rebuilds its part of the server on save</Trans>
          )}
        </span>
        <span>
          <Trans>Version {version}</Trans>
        </span>
        <span {...(summary.updatedUtc === null ? {} : { title: fullTime(summary.updatedUtc) })}>
          {savedWhen === null ? (
            <Trans>Never changed on a page</Trans>
          ) : savedBy === null ? (
            <Trans>Saved {savedWhen}</Trans>
          ) : (
            <Trans>
              Saved {savedWhen} by {savedBy}
            </Trans>
          )}
        </span>
      </span>
      {summary.apply !== null && summary.apply.length > 0 ? (
        <ApplyStates states={summary.apply} version={version} />
      ) : null}
    </li>
  );
}
