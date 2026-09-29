// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { Link } from "@tanstack/react-router";

import { currentStepLabel, type DeploymentSummary } from "@/deployments/deployments";
import { formattingLocale } from "@/i18n/i18n";
import { formatDuration } from "@/lib/format";
import { useLiveMarks } from "@/live/useLiveMarks";
import { machinesQuery } from "@/machines/machines";
import { cx } from "@/ui/cx";
import { EmptyState } from "@/ui/EmptyState";
import { Panel } from "@/ui/Panel";
import { StateTag } from "@/ui/StateTag";

import { runSourceLabel, runStateLabel, runStateTone } from "./runView";

// Every run of one machine, newest first, with the shown one marked. The current run comes from the machine list. So
// a run that starts animates in, and one whose state changes flashes.
export function MachineRuns({
  machineId,
  runs,
  shownId,
  now,
}: {
  machineId: string;
  runs: readonly DeploymentSummary[];
  shownId: string | null;
  now: number;
}) {
  const { i18n, t } = useLingui();
  const locale = formattingLocale();
  const mark = useLiveMarks({
    queryKey: machinesQuery.queryKey,
    items: (machines) =>
      machines.flatMap((machine) =>
        machine.id === machineId && machine.deployment !== null ? [machine.deployment] : [],
      ),
    id: (run) => run.id,
    signature: (run) => run.state,
    tone: (run) => runStateTone[run.state],
  });

  return (
    <Panel title={<Trans>Runs of this machine</Trans>} flush>
      {runs.length === 0 ? (
        <EmptyState title={<Trans>No runs yet</Trans>}>
          <Trans>
            Assign a sequence above, or add a rule that chooses one for machines like it under
            Deployment, Rules.
          </Trans>
        </EmptyState>
      ) : (
        <ol aria-label={t`Runs of this machine`} className="flex flex-col">
          {runs.map((run) => {
            const shown = run.id === shownId;
            const step = run.state === "Failed" ? currentStepLabel(run) : null;
            const end = run.finishedUtc === null ? now : Date.parse(run.finishedUtc);
            const assigned = new Date(run.createdUtc).toLocaleString(locale);
            const duration =
              run.startedUtc === null ? null : formatDuration(end - Date.parse(run.startedUtc));
            const by = run.requestedBy;

            return (
              <li
                key={run.id}
                aria-current={shown ? "true" : undefined}
                className={cx(
                  "grid grid-cols-[minmax(0,1fr)_auto] gap-x-4 gap-y-1 border-b border-line-soft px-4 py-3 last:border-b-0 sm:grid-cols-[minmax(0,1.4fr)_8rem_minmax(0,1fr)_7rem]",
                  shown && "bg-selected",
                  mark(run.id),
                )}
              >
                <span className="flex min-w-0 flex-col">
                  <Link
                    to="/machines/$machineId"
                    params={{ machineId }}
                    search={{ run: run.id }}
                    className="truncate type-label text-ink hover:underline"
                  >
                    {run.title}
                  </Link>
                  <span className="type-small text-muted">{assigned}</span>
                </span>
                <span className="flex flex-col items-start gap-1">
                  <StateTag tone={runStateTone[run.state]}>
                    {i18n._(runStateLabel[run.state])}
                  </StateTag>
                  {step !== null ? (
                    <span className="type-small text-fail-text">
                      <Trans>At {step}</Trans>
                    </span>
                  ) : null}
                </span>
                <span className="flex min-w-0 flex-col type-small text-ink-2 max-sm:col-span-2">
                  <span>{i18n._(runSourceLabel[run.source])}</span>
                  {by !== null ? <span className="text-muted">{by}</span> : null}
                </span>
                <span className="type-small text-ink-2 max-sm:col-span-2 sm:text-right">
                  {duration ?? <Trans>Not started</Trans>}
                </span>
              </li>
            );
          })}
        </ol>
      )}
    </Panel>
  );
}
