// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";

import { AutopilotPanel } from "./AutopilotPanel";
import { machineRecordsQuery } from "./machineRecords";
import { RecoveryKeysPanel } from "./RecoveryKeysPanel";

// What the machine's runs handed the server. Nothing shows until a run did, and the hub's pushes keep it current.
export function MachineRecordPanels({ machineId, now }: { machineId: string; now: number }) {
  const records = useQuery(machineRecordsQuery(machineId)).data;
  const keys = records?.recoveryKeys ?? [];
  const hash = records?.hardwareHash ?? null;

  if (keys.length === 0 && hash === null) {
    return null;
  }

  return (
    <div className="grid items-start gap-4 xl:grid-cols-2">
      {keys.length > 0 ? <RecoveryKeysPanel machineId={machineId} keys={keys} now={now} /> : null}
      {hash !== null ? <AutopilotPanel hash={hash} now={now} /> : null}
    </div>
  );
}
