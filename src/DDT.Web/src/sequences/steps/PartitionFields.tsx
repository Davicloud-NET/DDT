// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { FormField } from "../FormField";
import { NumberInput } from "../NumberInput";
import { fieldMessages } from "../problems";
import type { PartitionStep } from "../sequences";
import type { KindFieldsProps } from "./kindFields";

import styles from "../form.module.scss";

export function PartitionFields({ step, findings, onChange }: KindFieldsProps<PartitionStep>) {
  return (
    <>
      <p className={styles.explain}>
        Erases the whole disk and creates the EFI system, MSR, Windows and recovery partitions.
      </p>
      <FormField
        label="System partition (MB)"
        messages={fieldMessages(findings, "systemPartitionMegabytes")}
        hint="260 to 4096 MB."
      >
        {(control) => (
          <NumberInput
            control={control}
            min={260}
            value={step.systemPartitionMegabytes}
            onChange={(systemPartitionMegabytes) => {
              onChange({ systemPartitionMegabytes });
            }}
          />
        )}
      </FormField>
      <FormField
        label="Recovery partition (MB)"
        messages={fieldMessages(findings, "recoveryPartitionMegabytes")}
        hint="300 to 65536 MB."
      >
        {(control) => (
          <NumberInput
            control={control}
            min={300}
            value={step.recoveryPartitionMegabytes}
            onChange={(recoveryPartitionMegabytes) => {
              onChange({ recoveryPartitionMegabytes });
            }}
          />
        )}
      </FormField>
    </>
  );
}
