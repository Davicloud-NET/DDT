// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { fullTime, relativeTime } from "@/lib/relativeTime";
import { useCanDecide } from "@/machines/useCanDecide";
import { Button } from "@/ui/Button";
import { Facts } from "@/ui/Facts";
import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";

import type { HardwareHashUploadState, MachineHardwareHashView } from "./machineRecords";
import { useAutopilotActions } from "./useAutopilotActions";

interface AutopilotPanelProps {
  machineId: string;
  hash: MachineHardwareHashView;
  now: number;
}

// The hardware hash the machine's last Register with Autopilot step collected, and how far its upload is. Operators
// download Intune's CSV, or upload a hash again that was never uploaded or failed.
export function AutopilotPanel({ machineId, hash, now }: AutopilotPanelProps) {
  const canDecide = useCanDecide();
  const { download, upload } = useAutopilotActions();
  const error = download.error ?? upload.error;

  return (
    <Panel title={<Trans>Autopilot</Trans>}>
      {error === null ? null : <Notice tone="fail">{error.message}</Notice>}
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
      {canDecide ? (
        <div className="flex flex-wrap gap-2">
          <Button
            size="sm"
            isPending={download.isPending}
            onPress={() => {
              download.mutate([machineId]);
            }}
          >
            <Trans>Download CSV</Trans>
          </Button>
          {hash.uploadState === "None" || hash.uploadState === "Failed" ? (
            <Button
              size="sm"
              variant="quiet"
              isPending={upload.isPending}
              onPress={() => {
                upload.mutate(machineId);
              }}
            >
              <Trans>Upload to Autopilot</Trans>
            </Button>
          ) : null}
        </div>
      ) : null}
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
