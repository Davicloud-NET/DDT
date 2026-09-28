// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { Button as AriaButton } from "react-aria-components";

import type { Subject } from "@/conditions/conditionSubjects";
import type { DeploymentSummary } from "@/deployments/deployments";
import { nodeTitle } from "@/sequences/flow/flowLabels";
import { StateTag } from "@/ui/StateTag";

import { decisionOutcomes, decisionTitle } from "../decisions";
import type { PathNode } from "../runPath";
import { crumbText, pathStateLabel, pathStateTone } from "../runView";
import { passesText, timeText } from "./detailsText";
import { Outcome } from "./Outcome";

interface NodeDetailsProps {
  node: PathNode;
  run: DeploymentSummary;
  subjects: readonly Subject[];
  now: number;
  onShowLog: (stepId: string) => void;
}

// The chosen node: what it is and how it stands, when it ran, why it failed, what it decided and with which values,
// and a way to its lines in the log.
export function NodeDetails({ node, run, subjects, now, onShowLog }: NodeDetailsProps) {
  const { i18n, t: translate } = useLingui();
  const step = node.step;
  const title = nodeTitle({ ...node.node, name: step?.name ?? node.node.name });
  const decision = node.state === "notTaken" ? null : decisionTitle(node.node, step);
  const outcomes = node.state === "notTaken" ? [] : decisionOutcomes(node.node, step);
  // Only for a node inside a container; the flow shows a leaf's number beside its name.
  const place = node.ancestors.length === 0 ? null : crumbText(node.ancestors);

  return (
    <section
      aria-label={translate`The chosen step`}
      className="flex flex-col gap-3 rounded-panel bg-panel p-4 shadow-panel"
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-0.5">
          {place === null ? null : <span className="type-small text-muted">{place}</span>}
          <h3 className="type-heading text-ink">{title}</h3>
        </div>
        <StateTag tone={pathStateTone[node.state]}>{i18n._(pathStateLabel[node.state])}</StateTag>
      </div>
      <p className="type-small text-muted">{timeText(node, run, now)}</p>
      {(step?.pass ?? 0) > 1 && node.entry.number !== null ? (
        <p className="type-small text-muted">{passesText(step?.pass ?? 0)}</p>
      ) : null}
      {step?.state === "Failed" ? (
        <p className="type-small text-fail-text">
          {step.error ?? <Trans>The step failed without saying why.</Trans>}
        </p>
      ) : null}
      {decision !== null ? (
        <div className="flex flex-col gap-2.5 rounded-key bg-well p-3">
          <span className="type-label text-ink">{decision}</span>
          {outcomes.map((outcome) => (
            <Outcome key={outcome.path} outcome={outcome} subjects={subjects} />
          ))}
        </div>
      ) : null}
      {step !== null && step.startedUtc !== null ? (
        <AriaButton
          className="w-fit cursor-pointer type-label text-ink underline underline-offset-3 outline-none hover:text-ink-2 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
          onPress={() => {
            onShowLog(step.stepId);
          }}
        >
          <Trans>Show the log of this step</Trans>
        </AriaButton>
      ) : null}
    </section>
  );
}
