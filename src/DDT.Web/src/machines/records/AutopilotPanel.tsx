// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { fullTime, relativeTime } from "@/lib/relativeTime";
import { Facts } from "@/ui/Facts";
import { Panel } from "@/ui/Panel";

import type { HardwareHashUploadState, MachineHardwareHashView } from "./machineRecords";

// The hardware hash the machine's last Register with Autopilot step collected, and how far its upload is.
export function AutopilotPanel({ hash, now }: { hash: MachineHardwareHashView; now: number }) {
  return (
    <Panel title={<Trans>Autopilot</Trans>}>
      <Facts
        items={[
          { label: <Trans>Serial number</Trans>, value: hash.serialNumber, mono: true },
          ...(hash.groupTag === null
            ? []
            : [{ label: <Trans>Group tag</Trans>, value: hash.groupTag }]),
          {
            label: <Trans>Collected</Trans>,
            value: (
              <span title={fullTime(hash.collectedUtc)}>
                {relativeTime(hash.collectedUtc, now)}
              </span>
            ),
          },
          {
            label: <Trans>Upload</Trans>,
            value:
              hash.uploadMessage === null
                ? uploadText(hash.uploadState)
                : `${uploadText(hash.uploadState)}: ${hash.uploadMessage}`,
          },
        ]}
      />
    </Panel>
  );
}

function uploadText(state: HardwareHashUploadState): string {
  switch (state) {
    case "Pending":
      return t`Waiting`;
    case "Imported":
      return t`Imported`;
    case "Failed":
      return t`Failed`;
    default:
      return t`Not uploaded`;
  }
}
