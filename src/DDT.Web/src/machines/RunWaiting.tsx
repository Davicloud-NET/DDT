// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { isWaiting, type DeploymentSummary, type DeploymentView } from "@/deployments/deployments";
import { InputsDialog } from "@/inputs/InputsDialog";

import { useRunWaitingActions } from "./waiting/useRunWaitingActions";
import { WaitingText } from "./waiting/WaitingText";
import { WaitKey } from "./waiting/WaitKey";
import { waitState } from "./waiting/waitState";

interface RunWaitingProps {
  machineId: string;
  // The machine's current run, as the machine list follows it.
  run: DeploymentSummary | null;
  // The run as read, with its pause and its inputs; null until it loaded.
  view: DeploymentView | null;
  canAct: boolean;
}

// A banner across the page, in the waiting colour, for a run that waits at a Pause or for answers. Its button
// continues the run or gives the answers. The buttons are for operators, and the person at the machine can do the
// same there.
export function RunWaiting({ machineId, run, view, canAct }: RunWaitingProps) {
  const { t: translate } = useLingui();
  const { asking, setAsking, proceed, answer } = useRunWaitingActions(machineId);
  const shown = view?.summary.id === run?.id ? view : null;

  if (run === null || !isWaiting(run)) {
    return null;
  }

  const wait = waitState(run, shown);
  const { pause, inputs } = wait;
  const sequenceName = run.title;

  return (
    <section
      aria-label={translate`The run waits`}
      className="flex flex-wrap items-center gap-x-4 gap-y-3 rounded-panel bg-attention py-3.5 pr-4 pl-5 text-on-attention"
    >
      <WaitingText wait={wait} error={proceed.isError ? proceed.error.message : null} />
      {canAct ? (
        wait.paused ? (
          <WaitKey
            isDisabled={pause === null || proceed.isPending}
            onPress={() => {
              if (pause !== null) {
                proceed.mutate(pause);
              }
            }}
          >
            <Trans>Continue the run</Trans>
          </WaitKey>
        ) : (
          <WaitKey
            isDisabled={shown === null || inputs.length === 0}
            onPress={() => {
              answer.reset();
              setAsking(true);
            }}
          >
            <Trans>Give the answers</Trans>
          </WaitKey>
        )
      ) : null}

      {asking ? (
        <InputsDialog
          title={<Trans>Answers for {sequenceName}</Trans>}
          inputs={inputs}
          confirmLabel={<Trans>Give the answers</Trans>}
          isBusy={answer.isPending}
          error={answer.error}
          onClose={() => {
            setAsking(false);
          }}
          onSubmit={(answers) => {
            answer.mutate(answers);
          }}
        >
          <p>
            <Trans>The run starts once every required answer is given.</Trans>
          </p>
        </InputsDialog>
      ) : null}
    </section>
  );
}
