// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { FlagSetting } from "../../fields/FlagSetting";
import type { Findings } from "../../problems";
import type { StepPatch } from "../../sequenceEdits";
import type { SequenceStep } from "../../sequences";
import { isContainer } from "../../steps";

// What happens when the node fails, and whether a step restarts the machine after it.
export function FailureSettings({
  node,
  findings,
  change,
}: {
  node: SequenceStep;
  findings: Findings;
  change: (patch: StepPatch) => void;
}) {
  const container = isContainer(node);

  return (
    <div className="flex flex-col gap-3">
      <FlagSetting
        label={
          container ? (
            <Trans>Go on when a step in here fails</Trans>
          ) : (
            <Trans>Go on when this step fails</Trans>
          )
        }
        field="continueOnError"
        findings={findings}
        hint={<Trans>Otherwise the run stops here.</Trans>}
        value={node.continueOnError}
        onChange={(continueOnError) => {
          change({ continueOnError });
        }}
      />
      {!container && (node.kind !== "reboot" || node.rebootAfter) ? (
        <FlagSetting
          label={<Trans>Restart after this step</Trans>}
          field="rebootAfter"
          findings={findings}
          hint={<Trans>The machine restarts once the step is done.</Trans>}
          value={node.rebootAfter}
          onChange={(rebootAfter) => {
            change({ rebootAfter });
          }}
        />
      ) : null}
    </div>
  );
}
