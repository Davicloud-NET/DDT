// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { isActive, type AgentEnvironment } from "@/deployments/deployments";
import type { MachineSummary } from "@/machines/machines";
import type { SequenceSummary } from "@/sequences/sequences";

export function environmentName(environment: AgentEnvironment): string {
  switch (environment) {
    case "WindowsPE":
      return t`Windows PE`;
    case "LinuxBoot":
      return t`Linux boot environment`;
    case "Windows":
      return t`Installed Windows`;
    case "Linux":
      return t`Installed Linux`;
  }
}

// The boot environment a run of the sequence starts in, like the server's SequenceKinds.BootEnvironment.
export function startsIn(sequence: Pick<SequenceSummary, "kind">): AgentEnvironment {
  return sequence.kind === "Linux" ? "LinuxBoot" : "WindowsPE";
}

// The boot environment the machine was asked to restart into, in words. Null while it was not asked.
export function restartsInto(machine: MachineSummary): string | null {
  const next = machine.nextEnvironment ?? null;

  return next === null ? null : environmentName(next);
}

// What a list row says while the machine was asked to restart into the other boot environment. Null otherwise.
export function restartLine(machine: MachineSummary): string | null {
  switch (machine.nextEnvironment ?? null) {
    case "WindowsPE":
      return t`Restarts into Windows PE`;
    case "LinuxBoot":
      return t`Restarts into the Linux boot environment`;
    default:
      return null;
  }
}

// What a list row says about a machine that has no run to show: where it restarts into, else what it started. Null
// for a server that does not say.
export function environmentLine(machine: MachineSummary): string | null {
  const restart = restartLine(machine);

  if (restart !== null) {
    return restart;
  }

  switch (machine.agentEnvironment) {
    case "WindowsPE":
      return t`Started Windows PE`;
    case "LinuxBoot":
      return t`Started the Linux boot environment`;
    default:
      return null;
  }
}

// What a list row says once the server stopped restarting the machine for its run. Null otherwise.
export function restartsFailedLine(machine: MachineSummary): string | null {
  switch (machine.restartsFailed ?? null) {
    case "WindowsPE":
      return t`Did not start Windows PE after restarting`;
    case "LinuxBoot":
      return t`Did not start the Linux boot environment after restarting`;
    default:
      return null;
  }
}

// The same for the machine's page, with what to look at. Null otherwise.
export function restartsFailedNotice(machine: MachineSummary): string | null {
  const title = machine.deployment?.title ?? "";

  switch (machine.restartsFailed ?? null) {
    case "WindowsPE":
      return t`This machine did not start Windows PE after restarting, so DDT no longer restarts it. Check that it starts from the network and that no DHCP server names another boot file. ${title} stays assigned and starts once the machine starts Windows PE.`;
    case "LinuxBoot":
      return t`This machine did not start the Linux boot environment after restarting, so DDT no longer restarts it. Check that it starts from the network and that no DHCP server names another boot file. ${title} stays assigned and starts once the machine starts the Linux boot environment.`;
    default:
      return null;
  }
}

// The facts about where the machine's agent runs: the environment it last registered from and, while the machine was
// asked to restart into the other boot environment, that one. None for a server that does not say.
export function environmentFacts(machine: MachineSummary): { label: string; value: string }[] {
  const started = machine.agentEnvironment;
  const next = restartsInto(machine);

  if (started === undefined) {
    return [];
  }

  return [
    { label: t`Environment`, value: environmentName(started) },
    ...(next === null ? [] : [{ label: t`Restarts into`, value: next }]),
  ];
}

// What the assign and approval dialogs say when the sequence starts in the other boot environment than the one the
// machine waits in. A warning when the server cannot start that one, so the machine could not get there.
export interface StartNotice {
  warning: boolean;
  text: string;
}

// A machine in one of these states sits in the boot environment it started, where its agent waits for a run. One
// that last registered from an installed system is in neither, and the server restarts none from there.
function waitsInBootEnvironment(machine: MachineSummary): boolean {
  const started = machine.agentEnvironment;

  return (
    (started === "WindowsPE" || started === "LinuxBoot") &&
    !isActive(machine.deployment) &&
    (machine.state === "Pending" || machine.state === "Approved" || machine.state === "Failed")
  );
}

// Null when there is nothing to say: the machine waits where the sequence starts, or it waits nowhere and netboot
// starts it there the next time. existing are the boot environments the server can start, and undefined for a server
// that does not say.
export function startNotice(
  machine: MachineSummary,
  sequence: SequenceSummary | null,
  existing: readonly AgentEnvironment[] | null | undefined,
): StartNotice | null {
  const started = machine.agentEnvironment;

  if (sequence === null || existing === null || existing === undefined || started === undefined) {
    return null;
  }

  const needed = startsIn(sequence);
  const name = sequence.name;

  if (started === needed || !waitsInBootEnvironment(machine)) {
    return null;
  }

  if (!existing.includes(needed)) {
    return {
      warning: true,
      text:
        needed === "LinuxBoot"
          ? t`${name} runs in the Linux boot environment, which this server does not have. The run stays assigned and does not start until the server has it.`
          : t`${name} runs in Windows PE, and this server has no boot image to start it from. The run stays assigned and does not start until an administrator builds one on the Boot image page.`,
    };
  }

  // An agent from before the Linux boot environment cannot take its sign-in along, so the server does not restart it.
  if (machine.changesBootEnvironment === false) {
    return {
      warning: true,
      text:
        needed === "LinuxBoot"
          ? t`${name} runs in the Linux boot environment, and this machine started Windows PE with an agent too old to restart into it. The run stays assigned and starts once the machine is started from the network again.`
          : t`${name} runs in Windows PE, and this machine started the Linux boot environment with an agent too old to restart into it. The run stays assigned and starts once the machine is started from the network again.`,
    };
  }

  return {
    warning: false,
    text:
      needed === "LinuxBoot"
        ? t`${name} runs in the Linux boot environment, and this machine started Windows PE. The machine restarts into the Linux boot environment first.`
        : t`${name} runs in Windows PE, and this machine started the Linux boot environment. The machine restarts into Windows PE first.`,
  };
}
