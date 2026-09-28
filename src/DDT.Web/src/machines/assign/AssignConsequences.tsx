// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { DeploymentOptionsView } from "@/deployments/deployments";
import { machineLabel, type MachineSummary } from "@/machines/machines";
import type { SecureBootRisk } from "@/machines/secureBoot";
import type { SequenceSummary } from "@/sequences/sequences";
import { Notice } from "@/ui/Notice";

import { hardwareText, lastSeenText, pendingConsequence, warningsText } from "./assignText";

interface AssignConsequencesProps {
  machine: MachineSummary;
  sequence: SequenceSummary | null;
  erases: boolean;
  severalDisks: boolean;
  risk: SecureBootRisk | null;
  // The id the allowance's checkbox is described by.
  warningId: string;
  // Undefined until the deployment settings loaded.
  options: DeploymentOptionsView | undefined;
  // On the server's clock.
  now: number;
}

// What the assignment does to this machine: its disk, Secure Boot, Windows, and for a waiting machine whether the
// assignment also authorizes it.
export function AssignConsequences({
  machine,
  sequence,
  erases,
  severalDisks,
  risk,
  warningId,
  options,
  now,
}: AssignConsequencesProps) {
  const label = machineLabel(machine);
  const sequenceName = sequence?.name ?? "";

  return (
    <div className="flex flex-col gap-2">
      {sequence !== null ? (
        erases ? (
          <Notice tone="attention">
            <Trans>
              {sequenceName} erases all data on the disk of {label}.
            </Trans>
          </Notice>
        ) : (
          <p>
            <Trans>
              {sequenceName} does not erase the disk of {label}.
            </Trans>
          </p>
        )
      ) : null}
      {risk !== null ? (
        <Notice tone="attention">
          <span id={warningId}>{risk.warning}</span>
        </Notice>
      ) : null}
      {sequence?.continuesInWindows === true ? (
        <p>
          <Trans>
            After the image is applied, the run goes on in the installed Windows, where the agent
            runs as a service until the run ends.
          </Trans>
        </p>
      ) : null}
      {sequence !== null && sequence.warningCount > 0 ? <p>{warningsText(sequence)}</p> : null}
      <p className="type-small text-muted">{hardwareText(machine)}</p>
      {erases && severalDisks ? (
        <Notice tone="fail">
          <Trans>
            This machine has more than one disk. Sign in at it and choose the disk there.
          </Trans>
        </Notice>
      ) : null}
      {erases && machine.eligibleDiskCount === 0 ? (
        <Notice tone="fail">
          <Trans>The machine reported no disk DDT can install on, so the run will fail.</Trans>
        </Notice>
      ) : null}
      {machine.state === "Pending" ? (
        <>
          <p>{lastSeenText(machine, now)}</p>
          {options !== undefined ? <p>{pendingConsequence(machine, options, now)}</p> : null}
        </>
      ) : null}
    </div>
  );
}
