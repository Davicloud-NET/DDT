// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import type { DeploymentSummary, DeploymentView } from "@/deployments/deployments";
import type { ContinueRunRequest } from "@/deployments/waitingRun";
import { formattingLocale } from "@/i18n/i18n";
import { askedInput, type AskedInput } from "@/inputs/inputs";
import { runPath } from "@/runs/runPath";

// What a waiting run waits for, as its notice says it.
export interface WaitState {
  // True at a Pause step. False when the run waits at its start for answers.
  paused: boolean;
  // What continues the run. Null until the page knows where it paused.
  pause: ContinueRunRequest | null;
  message: string | null;
  pausedAt: string;
  continues: string | null;
  // The inputs still without an answer.
  inputs: AskedInput[];
}

// shown is the run as read, or null while it has not loaded.
export function waitState(run: DeploymentSummary, shown: DeploymentView | null): WaitState {
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
  const unanswered = (shown?.inputs ?? []).filter((input) => !input.answered);

  return {
    paused: run.activity !== "WaitingForInput",
    pause,
    message: run.pauseMessage ?? shown?.pause?.message ?? null,
    pausedAt: pauseTitle(at?.entry.number ?? null, at?.step?.name ?? at?.node.name ?? null),
    continues: shown?.pause?.continuesUtc ?? null,
    inputs: unanswered.map((input) => askedInput(input.input, shown?.definition?.inputs ?? null)),
  };
}

function pauseTitle(number: number | null, name: string | null): string {
  if (name === null) {
    return t`The run is paused.`;
  }

  return number === null ? t`Paused at ${name}.` : t`Paused at ${number}, ${name}.`;
}

export function unansweredText(labels: readonly string[]): string {
  const list = new Intl.ListFormat(formattingLocale(), { type: "conjunction" }).format(labels);

  return t`It asks for ${list}.`;
}

export function continuesText(utc: string): string {
  const time = new Date(utc).toLocaleTimeString(formattingLocale(), {
    hour: "2-digit",
    minute: "2-digit",
  });

  return t`It goes on by itself at ${time}, or earlier when someone lets it go on here or at the machine.`;
}
