// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button as AriaButton } from "react-aria-components";

import {
  answerInputs,
  continueRun,
  isWaiting,
  putRun,
  type ContinueRunRequest,
  type DeploymentSummary,
  type DeploymentView,
  type RunAnswer,
} from "@/deployments/deployments";
import { formattingLocale } from "@/i18n/i18n";
import { askedInput, type InputAnswer } from "@/inputs/inputs";
import { InputsDialog } from "@/inputs/InputsDialog";
import { runPath } from "@/runs/runPath";
import { cx } from "@/ui/cx";
import { showToast } from "@/ui/toasts";

const keyClass =
  "inline-flex h-9.5 shrink-0 cursor-pointer items-center justify-center rounded-key bg-on-attention px-4 type-label font-bold text-attention key-motion outline-none " +
  "hover:bg-on-attention/85 pressed:bg-on-attention/75 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-on-attention disabled:cursor-not-allowed disabled:opacity-60";

// A run that waits for someone, across the page in the colour for that: at a Pause step, with its message and a key
// that lets the run go on, or at its start for answers to its inputs, with a key that gives them. The keys are for
// operators; the person at the machine can do the same there. The server's answer is the run as it is then, which the
// page takes in as it is; one that came too late says so.
export function RunWaiting({
  machineId,
  run,
  view,
  canAct,
}: {
  machineId: string;
  // The machine's current run, as the machine list follows it.
  run: DeploymentSummary | null;
  // The run as read, with its pause and its inputs; null until it loaded.
  view: DeploymentView | null;
  canAct: boolean;
}) {
  const { t: translate } = useLingui();
  const queryClient = useQueryClient();
  const [asking, setAsking] = useState(false);
  const shown = view?.summary.id === run?.id ? view : null;

  const settle = (answer: RunAnswer, late: () => void) => {
    putRun(queryClient, answer.view);

    if (answer.late) {
      late();
    }
  };

  const proceed = useMutation({
    mutationFn: (request: ContinueRunRequest) => continueRun(machineId, request),
    onSuccess: (answer) => {
      settle(answer, () => {
        showToast({
          title: t`The run went on already`,
          description: t`Someone at the machine or on another page let it go on first.`,
        });
      });
    },
  });

  const answer = useMutation({
    mutationFn: (answers: InputAnswer[]) => answerInputs(machineId, answers),
    onSuccess: (result) => {
      setAsking(false);
      settle(result, () => {
        showToast({
          title: t`The run has its answers already`,
          description: t`They were given at the machine or on another page first.`,
        });
      });
    },
  });

  if (run === null || !isWaiting(run)) {
    return null;
  }

  const paused = run.activity !== "WaitingForInput";
  const sequenceName = run.title;
  const path =
    shown === null
      ? null
      : runPath(shown.definition, shown.steps, {
          activity: run.activity,
          pause: shown.pause ?? null,
        });
  const at = path?.current?.state === "paused" ? path.current : null;
  const pause: ContinueRunRequest | null =
    shown?.pause !== null && shown?.pause !== undefined
      ? { stepId: shown.pause.stepId, pass: shown.pause.pass }
      : at?.step !== null && at?.step !== undefined
        ? { stepId: at.step.stepId, pass: at.step.pass ?? 0 }
        : null;
  const message = run.pauseMessage ?? shown?.pause?.message ?? null;
  const pausedAt = pauseTitle(at?.entry.number ?? null, at?.step?.name ?? at?.node.name ?? null);
  const continues = shown?.pause?.continuesUtc ?? null;
  const unanswered = (shown?.inputs ?? []).filter((input) => !input.answered);
  const inputs = unanswered.map((input) =>
    askedInput(input.input, shown?.definition?.inputs ?? null),
  );

  return (
    <section
      aria-label={translate`The run waits`}
      className="flex flex-wrap items-center gap-x-4 gap-y-3 rounded-panel bg-attention py-3.5 pr-4 pl-5 text-on-attention"
    >
      <div className="flex min-w-0 flex-1 basis-80 flex-col gap-1">
        {paused ? (
          <p className="type-body">
            <strong className="font-bold">{pausedAt}</strong>
            {message === null || message === "" ? null : <> {message}</>}
          </p>
        ) : (
          <p className="type-body">
            <strong className="font-bold">
              <Trans>The run waits for answers before it starts.</Trans>
            </strong>
            {inputs.length > 0 ? <> {unansweredText(inputs.map((input) => input.label))}</> : null}
          </p>
        )}
        <p className="type-small">
          {paused ? (
            continues === null ? (
              <Trans>The person at the machine can let the run go on there, too.</Trans>
            ) : (
              continuesText(continues)
            )
          ) : (
            <Trans>The person at the machine can answer there, too.</Trans>
          )}
        </p>
        {proceed.isError ? (
          <p role="alert" className="type-small font-semibold">
            {proceed.error.message}
          </p>
        ) : null}
      </div>
      {canAct ? (
        paused ? (
          <AriaButton
            className={cx(keyClass)}
            isDisabled={pause === null || proceed.isPending}
            onPress={() => {
              if (pause !== null) {
                proceed.mutate(pause);
              }
            }}
          >
            <Trans>Continue the run</Trans>
          </AriaButton>
        ) : (
          <AriaButton
            className={cx(keyClass)}
            isDisabled={shown === null || inputs.length === 0}
            onPress={() => {
              answer.reset();
              setAsking(true);
            }}
          >
            <Trans>Give the answers</Trans>
          </AriaButton>
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

function pauseTitle(number: number | null, name: string | null): string {
  if (name === null) {
    return t`The run is paused.`;
  }

  return number === null ? t`Paused at ${name}.` : t`Paused at ${number}, ${name}.`;
}

function unansweredText(labels: readonly string[]): string {
  const list = new Intl.ListFormat(formattingLocale(), { type: "conjunction" }).format(labels);

  return t`It asks for ${list}.`;
}

function continuesText(utc: string): string {
  const time = new Date(utc).toLocaleTimeString(formattingLocale(), {
    hour: "2-digit",
    minute: "2-digit",
  });

  return t`It goes on by itself at ${time}, or earlier when someone lets it go on here or at the machine.`;
}
