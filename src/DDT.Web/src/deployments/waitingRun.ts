// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { InputAnswer } from "@/inputs/inputs";
import { ApiError, apiPost } from "@/lib/api";

import type { DeploymentView } from "./deployments";

// The server's ContinueRunRequest: the Pause step and the visit the page showed. So a late click can't continue a
// later pause.
export interface ContinueRunRequest {
  stepId: string;
  pass: number;
}

// The run as the server has it after an answer or a continue. late is set if the server refused because the pause was
// already continued, or the answers were given at the machine first. The run is then as it is now.
export interface RunAnswer {
  view: DeploymentView;
  late: boolean;
}

function isDeploymentView(body: unknown): body is DeploymentView {
  return typeof body === "object" && body !== null && "summary" in body && "steps" in body;
}

// A refusal with 409 carries the run as it is now.
async function withLateAnswer(send: () => Promise<DeploymentView>): Promise<RunAnswer> {
  try {
    return { view: await send(), late: false };
  } catch (error) {
    if (error instanceof ApiError && error.status === 409 && isDeploymentView(error.problem)) {
      return { view: error.problem, late: true };
    }

    throw error;
  }
}

// Continues a run that a Pause step holds. Operators only.
export function continueRun(machineId: string, request: ContinueRunRequest): Promise<RunAnswer> {
  return withLateAnswer(() =>
    apiPost<DeploymentView>(`/api/machines/${machineId}/deployments/current/continue`, request),
  );
}

// Answers the inputs a run waits for at its start. Operators only. A refused answer's field is "answers.<name>".
export function answerInputs(machineId: string, answers: InputAnswer[]): Promise<RunAnswer> {
  return withLateAnswer(() =>
    apiPost<DeploymentView>(`/api/machines/${machineId}/deployments/current/answers`, {
      answers,
    }),
  );
}
