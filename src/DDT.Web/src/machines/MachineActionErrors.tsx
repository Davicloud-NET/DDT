// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Notice } from "@/ui/Notice";

import { recoveryKeysToConfirm } from "./actions/removalKeys";
import type { MachineActionState } from "./useMachineActions";

// Shows why the last action on a machine failed. It covers the actions that don't open their own dialog.
export function MachineActionErrors({ actions }: { actions: MachineActionState }) {
  // A removal that asks to confirm the machine's recovery keys opens its dialog instead.
  const failed = [actions.decide, actions.prepareApproval, actions.remove, actions.cancel].find(
    (mutation) => mutation.isError && recoveryKeysToConfirm(mutation.error) === null,
  );

  return failed?.error ? <Notice tone="fail">{failed.error.message}</Notice> : null;
}
