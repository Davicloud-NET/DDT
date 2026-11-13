// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { render, screen } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";

import { chooseOption, fill, nth, press } from "@/test/aria";

import type { Findings } from "../problems";
import type { WriteUnattendStep } from "../sequences";
import { newStep } from "../steps";
import { WriteUnattendFields } from "./WriteUnattendFields";

const catalog = { images: [], packages: [], applications: [], bundles: [], domainConfigured: null };
const none: Findings = { problems: [], warnings: [], notes: [] };

function Fields({ onStep }: { onStep?: (step: WriteUnattendStep) => void }) {
  const [step, setStep] = useState(newStep("writeUnattend", "u") as WriteUnattendStep);

  return (
    <I18nProvider i18n={i18n}>
      <WriteUnattendFields
        step={step}
        sequence="Windows"
        findings={none}
        catalog={catalog}
        onChange={(patch) => {
          setStep((before) => {
            const next = { ...before, ...patch } as WriteUnattendStep;
            onStep?.(next);

            return next;
          });
        }}
      />
    </I18nProvider>
  );
}

describe("WriteUnattendFields", () => {
  it("skips every out-of-box page it can by default", () => {
    render(<Fields />);

    for (const name of [
      "Skip the licence page",
      "Skip the OEM registration page",
      "Skip the online account pages",
      "Skip the wireless page",
    ]) {
      expect(screen.getByRole("checkbox", { name })).toBeChecked();
    }
  });

  it("shows the wireless page when told to", () => {
    let last: WriteUnattendStep | null = null;
    render(
      <Fields
        onStep={(step) => {
          last = step;
        }}
      />,
    );

    press(screen.getByRole("checkbox", { name: "Skip the wireless page" }));

    expect(last).toMatchObject({ hideWirelessSetup: false, hideEulaPage: true });
  });

  it("takes a display language, an organization and an owner, and empty is the default", () => {
    let last: WriteUnattendStep | null = null;
    render(
      <Fields
        onStep={(step) => {
          last = step;
        }}
      />,
    );

    fill(screen.getByRole("combobox", { name: "Display language" }), "de-DE");
    fill(screen.getByRole("combobox", { name: "Organization" }), "Davicloud");
    fill(screen.getByRole("combobox", { name: "Owner" }), " ");

    expect(last).toMatchObject({ uiLanguage: "de-DE", organization: "Davicloud", owner: null });
  });

  it("signs in as the local administrator a number of times", async () => {
    let last: WriteUnattendStep | null = null;
    render(
      <Fields
        onStep={(step) => {
          last = step;
        }}
      />,
    );

    expect(screen.queryByRole("textbox", { name: "How often" })).not.toBeInTheDocument();
    await chooseOption(document.body, "Sign in automatically", "As the local administrator");

    expect(last).toMatchObject({ autoLogon: "LocalAdministrator", autoLogonCount: 1 });
    expect(screen.getByRole("textbox", { name: "How often" })).toHaveValue("1");
  });

  it("adds first-logon commands in order and removes one", () => {
    let last: WriteUnattendStep | null = null;
    render(
      <Fields
        onStep={(step) => {
          last = step;
        }}
      />,
    );
    const field = screen.getByRole("textbox", { name: "Add a command" });

    fill(field, "cmd /c echo one");
    press(screen.getByRole("button", { name: "Add" }));
    fill(field, " cmd /c echo two ");
    press(screen.getByRole("button", { name: "Add" }));

    expect(last).toMatchObject({ firstLogonCommands: ["cmd /c echo one", "cmd /c echo two"] });

    press(nth(screen.getAllByRole("button", { name: /Remove/ }), 0));

    expect(last).toMatchObject({ firstLogonCommands: ["cmd /c echo two"] });
  });
});
