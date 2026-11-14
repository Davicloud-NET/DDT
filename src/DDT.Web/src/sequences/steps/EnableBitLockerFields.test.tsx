// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { render, screen } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";

import { press } from "@/test/aria";

import { newStep } from "../steps";
import type { EnableBitLockerStep, PreprovisionBitLockerStep } from "../windowsSteps";
import { EnableBitLockerFields } from "./EnableBitLockerFields";
import { PreprovisionBitLockerFields } from "./PreprovisionBitLockerFields";

const catalog = { images: [], packages: [], applications: [], bundles: [], domainConfigured: null };
const findings = { problems: [], warnings: [], notes: [] };

function Enable({ onStep }: { onStep: (step: EnableBitLockerStep) => void }) {
  const [step, setStep] = useState(newStep("enableBitLocker", "b") as EnableBitLockerStep);

  return (
    <I18nProvider i18n={i18n}>
      <EnableBitLockerFields
        step={step}
        sequence="Windows"
        findings={findings}
        catalog={catalog}
        onChange={(patch) => {
          setStep((before) => {
            const next = { ...before, ...patch } as EnableBitLockerStep;
            onStep(next);

            return next;
          });
        }}
      />
    </I18nProvider>
  );
}

describe("EnableBitLockerFields", () => {
  it("skips without a TPM when told, and asks for the limit only when it waits", () => {
    let last: EnableBitLockerStep | null = null;
    render(
      <Enable
        onStep={(step) => {
          last = step;
        }}
      />,
    );

    expect(screen.getByRole("checkbox", { name: "Fail without a TPM" })).toBeChecked();
    expect(screen.queryByRole("textbox", { name: "Wait at most (minutes)" })).toBeNull();
    press(screen.getByRole("checkbox", { name: "Fail without a TPM" }));
    press(
      screen.getByRole("checkbox", { name: "Back up the recovery password to Active Directory" }),
    );
    press(screen.getByRole("checkbox", { name: "Wait until the disk is encrypted" }));

    expect(last).toMatchObject({
      ifNoTpm: "Skip",
      backupToActiveDirectory: false,
      waitForEncryption: true,
      waitMinutes: 120,
    });
    expect(screen.getByRole("textbox", { name: "Wait at most (minutes)" })).toBeDefined();
  });
});

describe("PreprovisionBitLockerFields", () => {
  it("skips by default and fails when told", () => {
    let last: PreprovisionBitLockerStep = newStep(
      "preprovisionBitLocker",
      "p",
    ) as PreprovisionBitLockerStep;
    render(
      <I18nProvider i18n={i18n}>
        <PreprovisionBitLockerFields
          step={last}
          sequence="Windows"
          findings={findings}
          catalog={catalog}
          onChange={(patch) => {
            last = { ...last, ...patch };
          }}
        />
      </I18nProvider>,
    );

    const fail = screen.getByRole("checkbox", {
      name: "Fail without a TPM or a boot image with BitLocker",
    });
    expect(fail).not.toBeChecked();
    press(fail);

    expect(last.ifUnavailable).toBe("Fail");
  });
});
