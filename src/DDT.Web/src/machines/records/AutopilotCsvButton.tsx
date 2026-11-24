// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Button } from "@/ui/Button";

import { useAutopilotActions } from "./useAutopilotActions";

// Intune's CSV for the machines the list shows. Those without a hardware hash are left out.
export function AutopilotCsvButton({ machineIds }: { machineIds: readonly string[] }) {
  const { download } = useAutopilotActions();

  return (
    <div className="flex items-center gap-2">
      {download.error === null ? null : (
        <span role="alert" className="type-small text-fail-text">
          {download.error.message}
        </span>
      )}
      <Button
        variant="quiet"
        isDisabled={machineIds.length === 0}
        isPending={download.isPending}
        onPress={() => {
          download.mutate(machineIds);
        }}
      >
        <Trans>Autopilot CSV</Trans>
      </Button>
    </div>
  );
}
