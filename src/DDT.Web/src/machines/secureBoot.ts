// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

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

    const cas = describeUefiCas(signedUnder);

    return machine.secureBootEnabled === true && untrusted(machine.trustedUefiCas, signedUnder)
      ? {
          required: true,
          warning: t`${image} is signed under ${cas}, which this machine's firmware does not trust. The run writes it only if you allow it, and the machine then starts it once that CA is allowed or Secure Boot is turned off in its firmware setup.`,
          allowLabel: t`Write ${image} anyway`,
        }
      : null;
  }

  const allowLabel = t`Write ${image} anyway`;
  const unsigned = capability === "NotSigned";

  if (machine.secureBootEnabled === true) {
    return {
      required: true,
      warning: unsigned
        ? t`${image} is not signed for Secure Boot, and this machine has Secure Boot on. The run writes it only if you allow it, and the machine then starts it once Secure Boot is turned off in its firmware setup or your own key is enrolled.`
        : t`${image} may not start with Secure Boot on, as DDT could not tell whether it is signed for it, and this machine has Secure Boot on. The run writes it only if you allow it, and the machine then starts it once Secure Boot is turned off in its firmware setup or your own key is enrolled.`,
      allowLabel,
    };
  }

  return {
    required: false,
    warning: unsigned
      ? t`${image} is not signed for Secure Boot. The machine has not said whether Secure Boot is on. If it is, the run stops before it erases anything, unless you allow the image here.`
      : t`${image} may not start with Secure Boot on, as DDT could not tell whether it is signed for it. The machine has not said whether Secure Boot is on. If it is, the run stops before it erases anything, unless you allow the image here.`,
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

  const [first = "", second = ""] = years;

  switch (years.length) {
    case 1:
      return t`Microsoft's third-party UEFI CA ${first}`;
    case 2:
      return t`Microsoft's third-party UEFI CAs ${first} and ${second}`;
    default:
      return t`Microsoft's third-party UEFI CA`;
  }
}

// What the firmware's trust adds to "Secure Boot on", or null when it trusts both CAs or did not say.
function trustNote(machine: MachineSummary): string | null {
  const trusted = uefiCas(machine.trustedUefiCas);

  if (trusted === null || trusted.size === 2) {
    return null;
  }

  const cas = describeUefiCas(machine.trustedUefiCas);

  return trusted.size === 0 ? t`without Microsoft's third-party UEFI CA` : t`with ${cas} only`;
}

// "On", "Off" or "Not reported", with what the firmware trusts, for the machine's page.
export function secureBootFact(machine: MachineSummary): string {
  switch (machine.secureBootEnabled) {
    case true: {
      const note = trustNote(machine);

      return note === null ? t`On` : t`On, ${note}`;
    }
    case false:
      return t`Off`;
    case null:
      return t`Not reported`;
  }
}

// "Secure Boot on" or "off", or null for a machine that did not say.
export function secureBootLabel(machine: MachineSummary): string | null {
  switch (machine.secureBootEnabled) {
    case true: {
      const note = trustNote(machine);

      return note === null ? t`Secure Boot on` : t`Secure Boot on, ${note}`;
    }
    case false:
      return t`Secure Boot off`;
    case null:
      return null;
  }
}
