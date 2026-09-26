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

// Null where the sequence writes no such image, or the machine said Secure Boot is off. An image signed for Secure Boot
// is such an image on a machine with Secure Boot on whose firmware does not trust Microsoft's third-party UEFI CA.
export function secureBootRisk(
  machine: MachineSummary,
  sequence: SequenceSummary | null | undefined,
): SecureBootRisk | null {
  const image = sequence?.rawImageName ?? null;
  const capability = sequence?.rawImageBootCapability ?? null;

  if (image === null || capability === null || machine.secureBootEnabled === false) {
    return null;
  }

  if (capability === "SecureBootOk") {
    const signedUnder = sequence?.rawImageSignedUnder ?? null;

    return machine.secureBootEnabled === true && untrusted(machine.trustedUefiCas, signedUnder)
      ? {
          required: true,
          warning: `${image} is signed under ${describeUefiCas(signedUnder)}, which this machine's firmware does not trust. The run writes it only if you allow it, and the machine then starts it once that CA is allowed or Secure Boot is turned off in its firmware setup.`,
          allowLabel: `Write ${image} anyway`,
        }
      : null;
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

// The CAs a flags value names, as the server writes them, or null when unknown.
function uefiCas(value: string | null): Set<string> | null {
  if (value === null) {
    return null;
  }

  return new Set(
    value
      .split(",")
      .map((part) => part.trim())
      .filter((part) => part !== "" && part !== "None"),
  );
}

// True when both are known and the firmware trusts none of the CAs the boot file is signed under.
function untrusted(trusted: string | null, signedUnder: string | null): boolean {
  const firmware = uefiCas(trusted);
  const file = uefiCas(signedUnder);

  return (
    firmware !== null &&
    file !== null &&
    file.size > 0 &&
    [...file].every((ca) => !firmware.has(ca))
  );
}

// "Microsoft's third-party UEFI CA 2011", "... CA 2023" or "... CAs 2011 and 2023".
export function describeUefiCas(value: string | null): string {
  const years = [...(uefiCas(value) ?? [])].map((ca) => ca.replace("Microsoft", "")).sort();

  switch (years.length) {
    case 1:
      return `Microsoft's third-party UEFI CA ${years[0] ?? ""}`;
    case 2:
      return `Microsoft's third-party UEFI CAs ${years.join(" and ")}`;
    default:
      return "Microsoft's third-party UEFI CA";
  }
}

// What the firmware's trust adds to "Secure Boot on", or null when it trusts both CAs or did not say.
function trustNote(machine: MachineSummary): string | null {
  const trusted = uefiCas(machine.trustedUefiCas);

  if (trusted === null || trusted.size === 2) {
    return null;
  }

  return trusted.size === 0
    ? "without Microsoft's third-party UEFI CA"
    : `with ${describeUefiCas(machine.trustedUefiCas)} only`;
}

// "On", "Off" or "Not reported", with what the firmware trusts, for the machine's page.
export function secureBootFact(machine: MachineSummary): string {
  switch (machine.secureBootEnabled) {
    case true: {
      const note = trustNote(machine);

      return note === null ? "On" : `On, ${note}`;
    }
    case false:
      return "Off";
    case null:
      return "Not reported";
  }
}

// "Secure Boot on" or "off", or null for a machine that did not say.
export function secureBootLabel(machine: MachineSummary): string | null {
  switch (machine.secureBootEnabled) {
    case true: {
      const note = trustNote(machine);

      return note === null ? "Secure Boot on" : `Secure Boot on, ${note}`;
    }
    case false:
      return "Secure Boot off";
    case null:
      return null;
  }
}
