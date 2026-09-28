// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import type { DeploymentStepView } from "@/deployments/deployments";
import { Button } from "@/ui/Button";
import { FilterSelector } from "@/ui/FilterSelector";
import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";

import { LineDetail } from "./LineDetail";
import { LogFilters } from "./LogFilters";
import { stepLabel } from "./logLabels";
import { LogLines } from "./LogLines";
import { LogStatusBar } from "./LogStatusBar";
import { useLogView } from "./useLogView";

export interface LogPanelProps {
  machineId: string;
  // The run the page shows, whose lines the panel shows first.
  runId: string | null;
  active: boolean;
  steps: readonly DeploymentStepView[];
  // Only this step's lines are shown.
  stepFilter: string | null;
  onStepFilterChange: (stepId: string | null) => void;
}

// A machine's log as the agent sent it: the run's lines or all of the machine's, filtered by level, text and
// step. New lines arrive live; scrolling up pauses following, and a bar says how many arrived meanwhile.
export function LogPanel({
  machineId,
  runId,
  active,
  steps,
  stepFilter,
  onStepFilterChange,
}: LogPanelProps) {
  const { t: translate } = useLingui();
  const view = useLogView({ machineId, runId, active, stepFilter });
  const log = view.log;
  const filteredStep = stepLabel(steps, stepFilter);
  const readError = log.error;

  return (
    <Panel
      title={<Trans>Log</Trans>}
      actions={
        runId === null ? null : (
          <FilterSelector
            label={translate`Which lines`}
            selected={view.scope}
            onChange={(id) => {
              view.setScope(id === "machine" ? "machine" : "run");
            }}
            options={[
              { id: "run", label: translate`This run` },
              { id: "machine", label: translate`Whole machine` },
            ]}
          />
        )
      }
    >
      <LogFilters view={view} />

      {filteredStep !== null ? (
        <p className="flex flex-wrap items-center gap-2 type-small text-ink-2">
          <Trans>Only the lines of {filteredStep}.</Trans>
          <Button
            size="sm"
            variant="quiet"
            onPress={() => {
              onStepFilterChange(null);
            }}
          >
            <Trans>Show every step</Trans>
          </Button>
        </p>
      ) : null}

      {readError !== null ? (
        <Notice tone="fail">
          <Trans>The log could not be read: {readError}</Trans>
        </Notice>
      ) : null}

      <LogStatusBar view={view} />

      {log.loaded && view.all.length === 0 && log.error === null ? (
        <p className="py-6 text-ink-2">
          {view.deploymentId === null ? (
            <Trans>This machine has sent no log lines yet.</Trans>
          ) : (
            <Trans>No log lines for this run yet. The agent sends its log while it runs.</Trans>
          )}
        </p>
      ) : (
        <LogLines view={view} />
      )}

      {view.selected !== null ? (
        <LineDetail
          line={view.selected}
          step={stepLabel(steps, view.selected.stepId)}
          onClose={() => {
            view.setSelected(null);
          }}
        />
      ) : null}
    </Panel>
  );
}
