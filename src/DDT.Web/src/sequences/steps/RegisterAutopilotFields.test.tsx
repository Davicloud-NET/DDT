// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { render, screen } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";

import { fill, press } from "@/test/aria";

import type { RegisterAutopilotStep } from "../sequences";
import { newStep } from "../steps";
import { RegisterAutopilotFields } from "./RegisterAutopilotFields";

const catalog = { images: [], packages: [], applications: [], bundles: [], domainConfigured: null };

function Fields({ onStep }: { onStep: (step: RegisterAutopilotStep) => void }) {
  const [step, setStep] = useState(newStep("registerAutopilot", "a") as RegisterAutopilotStep);

  return (
    <I18nProvider i18n={i18n}>
      <RegisterAutopilotFields
        step={step}
        sequence="Windows"
        findings={{ problems: [], warnings: [], notes: [] }}
        catalog={catalog}
        onChange={(patch) => {
          setStep((before) => {
            const next = { ...before, ...patch } as RegisterAutopilotStep;
            onStep(next);

            return next;
          });
        }}
      />
    </I18nProvider>
  );
}

describe("RegisterAutopilotFields", () => {
  it("takes a group tag, and asks for the limit only when it waits", () => {
    let last: RegisterAutopilotStep | null = null;
    render(
      <Fields
        onStep={(step) => {
          last = step;
        }}
      />,
    );

    expect(screen.queryByRole("textbox", { name: "Wait at most (minutes)" })).toBeNull();
    fill(screen.getByRole("textbox", { name: "Group tag" }), "Sales");
    press(screen.getByRole("checkbox", { name: "Wait until Autopilot has imported the hash" }));

    expect(last).toMatchObject({ groupTag: "Sales", waitForImport: true, waitMinutes: 30 });
    expect(screen.getByRole("textbox", { name: "Wait at most (minutes)" })).toBeDefined();
  });
});
