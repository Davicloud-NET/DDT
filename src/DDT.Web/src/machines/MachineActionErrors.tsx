// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { MachineActionState } from "@/machines/useMachineActions";

import styles from "./MachineActionErrors.module.scss";

export interface MachineActionErrorsProps {
  actions: MachineActionState;
}

// Why an action without a dialog of its own failed. The dialogs show their own errors.
export function MachineActionErrors({ actions }: MachineActionErrorsProps) {
  const { decide, prepareApproval, remove, cancel } = actions;

  return (
    <>
      {decide.isError && <p className={styles.error}>{decide.error.message}</p>}
      {prepareApproval.isError && (
        <p className={styles.error} role="alert">
          The machine was not approved, because what the rules choose for it could not be read:{" "}
          {prepareApproval.error.message}
        </p>
      )}
      {remove.isError && <p className={styles.error}>{remove.error.message}</p>}
      {cancel.isError && (
        <p className={styles.error} role="alert">
          {cancel.error.message}
        </p>
      )}
    </>
  );
}
