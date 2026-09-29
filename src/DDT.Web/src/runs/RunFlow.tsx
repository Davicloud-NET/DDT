// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import type { ReactNode } from "react";

import type { DeploymentSummary, DeploymentView } from "@/deployments/deployments";

import { NodeDetails } from "./flow/NodeDetails";
import { RunFlowCanvas } from "./flow/RunFlowCanvas";
import { useFollowRun } from "./flow/useFollowRun";
import { useNodeFocus } from "./flow/useNodeFocus";
import { useRunFlowModel } from "./flow/useRunFlowModel";

interface RunFlowProps {
  view: DeploymentView;
  // The run as the machine list tracks it. It's newer than the copy the run was read with.
  summary: DeploymentSummary;
  now: number;
  onShowLog: (stepId: string) => void;
  // Extra content next to the flow, under the details, such as the run's values.
  children?: ReactNode;
}

// A run on the flow of the sequence it was given, with the chosen node's details next to it. It's a default export
// because the machine's page loads it with React's lazy when it shows a flow.
export default function RunFlow({ view, summary, now, onShowLog, children }: RunFlowProps) {
  const { t: translate } = useLingui();
  const model = useRunFlowModel(view, summary);
  const currentId = model.path.current?.node.id ?? null;
  const active = summary.state === "Running" || summary.state === "Assigned";
  const follow = useFollowRun(model.layout, currentId);
  const focus = useNodeFocus(model.layout, model.index, currentId, follow.viewport);
  const selected =
    focus.selectedId === null ? null : (model.path.byId.get(focus.selectedId) ?? null);

  return (
    <div className="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_26.5rem]">
      <RunFlowCanvas
        model={model}
        follow={follow}
        focus={focus}
        canFollow={active && currentId !== null}
        variables={view.variables ?? {}}
        now={now}
      />

      <div className="flex min-w-0 flex-col gap-4">
        {selected === null ? (
          <section
            aria-label={translate`The chosen step`}
            className="rounded-panel bg-panel p-4 type-small text-muted shadow-panel"
          >
            <Trans>Choose a step in the flow to see what it did.</Trans>
          </section>
        ) : (
          <NodeDetails
            node={selected}
            run={summary}
            subjects={model.subjects}
            now={now}
            onShowLog={onShowLog}
          />
        )}
        {children}
      </div>
    </div>
  );
}
