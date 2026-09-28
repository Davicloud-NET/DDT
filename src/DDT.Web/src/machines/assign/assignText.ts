// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";

import type { DeploymentOptionsView } from "@/deployments/deployments";
import { relativeTime } from "@/lib/relativeTime";
import { WAITING_WINDOW_MS, type MachineSummary } from "@/machines/machines";
import type { SequenceSummary } from "@/sequences/sequences";

export function problemsText(sequence: SequenceSummary): string {
  const count = sequence.problemCount;
  const problems = plural(count, { one: "# problem", other: "# problems" });

  return t`${problems}, cannot run`;
}

export function warningsText(sequence: SequenceSummary): string {
  const name = sequence.name;
  const count = sequence.warningCount;
  const warnings = plural(count, { one: "# warning", other: "# warnings" });

  return t`${name} has ${warnings}. It runs, but look at the sequence first.`;
}

export function hardwareText(machine: MachineSummary): string {
  const model = machine.model ?? t`not reported`;
  const disks = machine.disks;

  if (disks !== null) {
    return t`Model: ${model}. Reported disks: ${disks}.`;
  }

  // A machine that reported no eligible disk gets its own message.
  return machine.eligibleDiskCount === 0
    ? t`Model: ${model}.`
    : t`Model: ${model}. The machine has not reported its disks.`;
}

export function lastSeenText(machine: MachineSummary, now: number): string {
  const when = relativeTime(machine.lastSeenUtc, now);
  const from = machine.lastSeenAddress;
  const by = machine.signedInBy;

  if (by === null) {
    return from === null
      ? t`Last seen ${when}; nobody has signed in at it.`
      : t`Last seen from ${from} ${when}; nobody has signed in at it.`;
  }

  return from === null
    ? t`Last seen ${when}; signed in by ${by}.`
    : t`Last seen from ${from} ${when}; signed in by ${by}.`;
}

export function nameRequiredText(sequence: SequenceSummary): string {
  const name = sequence.name;

  return sequence.rawImageName === null
    ? t`Enter a computer name. ${name} joins the domain under this name.`
    : t`Enter a computer name. ${name} gives this name to the machine in its cloud-init seed.`;
}

// The server asks for a name only when the sequence uses it and neither the machine nor its values give one.
export function nameHint(
  machine: MachineSummary,
  sequence: SequenceSummary | null,
  valuesName: string | null,
): string {
  const uses = sequence?.needsComputerName === true;
  const current = machine.assignedName;

  if (current === null) {
    if (uses && valuesName !== null) {
      return sequence.rawImageName === null
        ? t`Optional. Left empty, the machine is named ${valuesName}, as its values say, and joins the domain under it.`
        : t`Optional. Left empty, the machine is named ${valuesName}, as its values say, which its cloud-init seed gets.`;
    }

    if (uses) {
      const name = sequence.name;

      return sequence.rawImageName === null
        ? t`Required, because ${name} joins the domain under this name.`
        : t`Required, because ${name} gives this name to the machine in its cloud-init seed.`;
    }

    return sequence?.rawImageName == null
      ? t`Optional. Without a name, Windows picks one.`
      : t`Optional. Without a name, the image picks one.`;
  }

  if (!uses) {
    return t`Optional. Left empty, the machine keeps the name ${current}.`;
  }

  return sequence.rawImageName === null
    ? t`Optional. Left empty, the machine keeps the name ${current} and joins the domain under it.`
    : t`Optional. Left empty, the machine keeps the name ${current}, which its cloud-init seed gets.`;
}

// What the assignment does to a machine that is not authorized yet, with now on the server's clock. Without web
// approval, the assignment authorizes a machine waiting at the prompt; one not seen for a while is authorized by
// the next sign-in at it, or by its next netboot from a zero touch network. With web approval, zero touch is off,
// and a sign-in and an assignment together authorize the machine in either order.
export function pendingConsequence(
  machine: MachineSummary,
  options: DeploymentOptionsView,
  now: number,
): string {
  const by = machine.signedInBy;

  if (options.requireWebApproval) {
    return by === null
      ? t`It stays waiting until someone signs in at it.`
      : t`This also authorizes the machine, because ${by} signed in at it. It then runs the sequence and receives the deployment passwords.`;
  }

  if (now - Date.parse(machine.lastSeenUtc) <= WAITING_WINDOW_MS) {
    return t`This also authorizes the machine, which then runs the sequence and receives the deployment passwords.`;
  }

  return options.zeroTouchEnabled
    ? t`It stays waiting until someone signs in at it or it netboots from a zero touch network.`
    : t`It stays waiting until someone signs in at it.`;
}
