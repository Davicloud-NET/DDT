// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import type { SequenceSummary } from "@/sequences/sequences";
import { machineSummary } from "@/test/builders";

import { secureBootLabel, secureBootRisk } from "./secureBoot";

const linux: SequenceSummary = {
  id: "s1",
  name: "Install Linux",
  description: null,
  revision: 1,
  stepCount: 2,
  problemCount: 0,
  warningCount: 0,
  erasesDisk: true,
  needsComputerName: false,
  continuesInWindows: false,
  updatedUtc: "2026-09-15T10:00:00Z",
  updatedBy: null,
  rawImageName: "noble",
  rawImageBootCapability: "NotSigned",
};

describe("secureBootRisk", () => {
  it("has nothing to allow for a signed image, a Windows sequence, or a machine with Secure Boot off", () => {
    const on = machineSummary({ secureBootEnabled: true });

    expect(secureBootRisk(on, { ...linux, rawImageBootCapability: "SecureBootOk" })).toBeNull();
    expect(
      secureBootRisk(on, { ...linux, rawImageName: null, rawImageBootCapability: null }),
    ).toBeNull();
    expect(secureBootRisk(on, null)).toBeNull();
    expect(secureBootRisk(machineSummary({ secureBootEnabled: false }), linux)).toBeNull();
  });

  it("requires the allowance where the machine has Secure Boot on", () => {
    expect(secureBootRisk(machineSummary({ secureBootEnabled: true }), linux)).toEqual({
      required: true,
      warning:
        "noble is not signed for Secure Boot, and this machine has Secure Boot on. The run writes it only if you allow it, and the machine then starts it once Secure Boot is turned off in its firmware setup or your own key is enrolled.",
      allowLabel: "Write noble anyway",
    });
  });

  it("offers the allowance where the machine did not say, for an image DDT could not judge", () => {
    const risk = secureBootRisk(machineSummary({ secureBootEnabled: null }), {
      ...linux,
      rawImageBootCapability: "Unknown",
    });

    expect(risk?.required).toBe(false);
    expect(risk?.warning).toBe(
      "noble may not start with Secure Boot on, as DDT could not tell whether it is signed for it. The machine has not said whether Secure Boot is on. If it is, the run stops before it erases anything, unless you allow the image here.",
    );
  });
});

describe("secureBootLabel", () => {
  it("says on or off, and nothing for a machine that did not say", () => {
    expect(secureBootLabel(machineSummary({ secureBootEnabled: true }))).toBe("Secure Boot on");
    expect(secureBootLabel(machineSummary({ secureBootEnabled: false }))).toBe("Secure Boot off");
    expect(secureBootLabel(machineSummary({ secureBootEnabled: null }))).toBeNull();
  });
});
