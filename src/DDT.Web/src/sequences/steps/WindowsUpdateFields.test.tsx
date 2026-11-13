// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { render, screen } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";

import { fill, press } from "@/test/aria";

import type { Findings } from "../problems";
import type { WindowsUpdateStep } from "../sequences";
import { newStep } from "../steps";
import { WindowsUpdateFields } from "./WindowsUpdateFields";

const catalog = { images: [], packages: [], applications: [], bundles: [], domainConfigured: null };
const none: Findings = { problems: [], warnings: [], notes: [] };

function Fields({
  findings = none,
  excludedKbs = [],
  onStep,
}: {
  findings?: Findings;
  excludedKbs?: string[];
  onStep?: (step: WindowsUpdateStep) => void;
}) {
  const [step, setStep] = useState({
    ...newStep("windowsUpdate", "w"),
    excludedKbs,
  } as WindowsUpdateStep);

  return (
    <I18nProvider i18n={i18n}>
      <WindowsUpdateFields
        step={step}
        sequence="Windows"
        findings={findings}
        catalog={catalog}
        onChange={(patch) => {
          setStep((before) => {
            const next = { ...before, ...patch } as WindowsUpdateStep;
            onStep?.(next);

            return next;
          });
        }}
      />
    </I18nProvider>
  );
}

describe("WindowsUpdateFields", () => {
  it("starts with Microsoft products, no drivers, no optional updates and 8 restarts", () => {
    render(<Fields />);

    expect(
      screen.getByRole("checkbox", { name: "Updates for other Microsoft products" }),
    ).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Drivers" })).not.toBeChecked();
    expect(
      screen.getByRole("checkbox", { name: "Optional updates and previews" }),
    ).not.toBeChecked();
    expect(screen.getByRole("textbox", { name: "Restarts at most" })).toHaveValue("8");
  });

  it("turns drivers on", () => {
    let last: WindowsUpdateStep | null = null;
    render(
      <Fields
        onStep={(step) => {
          last = step;
        }}
      />,
    );

    press(screen.getByRole("checkbox", { name: "Drivers" }));

    expect(last).toMatchObject({ drivers: true });
  });

  it("takes KB numbers separated by commas or spaces", () => {
    let last: WindowsUpdateStep | null = null;
    render(
      <Fields
        onStep={(step) => {
          last = step;
        }}
      />,
    );

    fill(
      screen.getByRole("textbox", { name: "Leave out these updates" }),
      "KB5031234, 5031235 kb5031236",
    );

    expect(last).toMatchObject({ excludedKbs: ["KB5031234", "5031235", "kb5031236"] });
  });

  it("names the entry the server refuses", () => {
    render(
      <Fields
        excludedKbs={["KB5031234", "Q12"]}
        findings={{
          ...none,
          problems: [
            {
              stepId: "w",
              field: "excludedKbs[1]",
              code: "sequence.updatesKb",
              message: "Enter a KB number, such as KB5031234.",
              args: {},
            },
          ],
        }}
      />,
    );

    expect(screen.getByRole("textbox", { name: "Leave out these updates" })).toHaveValue(
      "KB5031234, Q12",
    );
    expect(screen.getByText("Q12: Enter a KB number, such as KB5031234.")).toBeInTheDocument();
  });
});
