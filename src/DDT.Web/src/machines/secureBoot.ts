// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { MachineSummary } from "@/machines/machines";
import type { SequenceSummary } from "@/sequences/sequences";

// What an operator is told before a run writes a raw disk image that may not start with Secure Boot on, and the
// allowance offered. The server refuses the run where the machine said Secure Boot is on and nobody allowed the image
// (required); where the machine did not say, the agent asks the firmware and stops before erasing anything.
export interface SecureBootRisk {
  required: boolean;
  warning: string;
  allowLabel: string;
}

// Null where the sequence writes no such image, or the machine said Secure Boot is off.
export function secureBootRisk(
  machine: MachineSummary,
  sequence: SequenceSummary | null | undefined,
): SecureBootRisk | null {
  const image = sequence?.rawImageName ?? null;
  const capability = sequence?.rawImageBootCapability ?? null;

  if (
    image === null ||
    capability === null ||
    capability === "SecureBootOk" ||
    machine.secureBootEnabled === false
  ) {
    return null;
  }

  const why =
    capability === "NotSigned"
      ? `${image} is not signed for Secure Boot`
      : `${image} may not start with Secure Boot on, as DDT could not tell whether it is signed for it`;
  const allowLabel = `Write ${image} anyway`;

  return machine.secureBootEnabled === true
    ? {
        required: true,
        warning: `${why}, and this machine has Secure Boot on. The run writes it only if you allow it, and the machine then starts it once Secure Boot is turned off in its firmware setup or your own key is enrolled.`,
        allowLabel,
      }
    : {
        required: false,
        warning: `${why}. The machine has not said whether Secure Boot is on. If it is, the run stops before it erases anything, unless you allow the image here.`,
        allowLabel,
      };
}

// "Secure Boot on" or "off", or null for a machine that did not say.
export function secureBootLabel(machine: MachineSummary): string | null {
  switch (machine.secureBootEnabled) {
    case true:
      return "Secure Boot on";
    case false:
      return "Secure Boot off";
    case null:
      return null;
  }
}
