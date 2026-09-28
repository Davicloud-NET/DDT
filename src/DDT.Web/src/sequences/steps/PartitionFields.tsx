// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { NumberSetting } from "../fields/NumberSetting";
import type { PartitionStep } from "../sequences";
import type { KindFieldsProps } from "./kindFields";

export function PartitionFields({ step, findings, onChange }: KindFieldsProps<PartitionStep>) {
  return (
    <>
      <p className="text-ink-2 sm:col-span-2">
        <Trans>
          Erases the whole disk and creates the EFI system, MSR, Windows and recovery partitions.
        </Trans>
      </p>
      <NumberSetting
        label={<Trans>System partition in MB</Trans>}
        field="systemPartitionMegabytes"
        findings={findings}
        hint={<Trans>From 260 to 4096 MB.</Trans>}
        minValue={260}
        maxValue={4096}
        value={step.systemPartitionMegabytes}
        onChange={(systemPartitionMegabytes) => {
          onChange({ systemPartitionMegabytes });
        }}
      />
      <NumberSetting
        label={<Trans>Recovery partition in MB</Trans>}
        field="recoveryPartitionMegabytes"
        findings={findings}
        hint={<Trans>From 300 to 65536 MB.</Trans>}
        minValue={300}
        maxValue={65536}
        value={step.recoveryPartitionMegabytes}
        onChange={(recoveryPartitionMegabytes) => {
          onChange({ recoveryPartitionMegabytes });
        }}
      />
    </>
  );
}
