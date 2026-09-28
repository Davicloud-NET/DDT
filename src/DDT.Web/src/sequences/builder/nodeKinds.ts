// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import type { StepKind } from "../sequences";
import { stepKindLabel } from "../steps";

// What the flow adds: the nodes that shape it, and the steps that do the work, in the order a sequence usually has
// them.
export const flowKinds: readonly StepKind[] = ["if", "repeat", "group", "setVariable", "pause"];

export const workKinds: readonly StepKind[] = [
  "partition",
  "applyImage",
  "injectDrivers",
  "writeUnattend",
  "joinDomain",
  "runScript",
  "reboot",
  "writeRawImage",
  "writeCloudInitSeed",
];

// A kind as the palette and the menus offer it.
export function addLabel(kind: StepKind): string {
  return kind === "setVariable" ? t`Set a variable` : stepKindLabel(kind);
}
