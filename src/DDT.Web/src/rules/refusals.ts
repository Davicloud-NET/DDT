// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { ApiError } from "@/lib/api";
import type { Findings } from "@/sequences/problems";
import type { SequenceProblem } from "@/sequences/sequences";

// How the drawers of the rules, the machine roles and the accounts read the server's refusals: field problems at the
// fields they name, and the thing as it is now where a save came too late.

export const noFindings: Findings = { problems: [], warnings: [] };

// A refused save's field problems as findings, each at the field the server names, such as name or values[1].name;
// null for a refusal that names no field. place moves a field, such as a value's past the blank rows left out.
export function refusalFindings(
  error: unknown,
  place: (field: string) => string = (field) => field,
): Findings | null {
  if (!(error instanceof ApiError) || error.status !== 400) {
    return null;
  }

  const problems: SequenceProblem[] = Object.entries(error.problem?.errors ?? {}).flatMap(
    ([field, messages]) =>
      messages.map((message) => ({ stepId: null, field: place(field), message })),
  );

  return problems.length === 0 ? null : { problems, warnings: [] };
}

// What a 409 carries when the server answers with the thing as it is now, as it does for a save over a newer one.
export function conflictOf(error: unknown): { revision: number } | null {
  if (!(error instanceof ApiError) || error.status !== 409) {
    return null;
  }

  const body = error.problem as unknown;

  return typeof body === "object" && body !== null && "revision" in body
    ? (body as { revision: number })
    : null;
}

// A failed save's message where no field, no notice of someone else's save and no notice of a deletion says it.
export function otherRefusal(error: Error, gone: boolean): string | null {
  return refusalFindings(error) === null && conflictOf(error) === null && !gone
    ? error.message
    : null;
}

// The findings of fields a drawer does not show, for a notice at its top.
export function unplaced(findings: Findings, shown: (field: string) => boolean): string[] {
  return findings.problems
    .filter((problem) => problem.field === null || !shown(problem.field))
    .map((problem) => problem.message);
}
