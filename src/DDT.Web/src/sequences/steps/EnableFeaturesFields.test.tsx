// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";

import { fill, press } from "@/test/aria";

import type { EnableFeaturesStep } from "../sequences";
import { newStep } from "../steps";
import { EnableFeaturesFields } from "./EnableFeaturesFields";

const catalog = { images: [], packages: [], applications: [], bundles: [], domainConfigured: null };

function Fields({ features }: { features: string[] }) {
  const [step, setStep] = useState({
    ...newStep("enableFeatures", "f"),
    features,
  } as EnableFeaturesStep);

  return (
    <I18nProvider i18n={i18n}>
      <EnableFeaturesFields
        step={step}
        sequence="Windows"
        findings={{ problems: [], warnings: [], notes: [] }}
        catalog={catalog}
        onChange={(patch) => {
          setStep((before) => ({ ...before, ...patch }));
        }}
      />
    </I18nProvider>
  );
}

function shown(): string[] {
  return within(screen.getByRole("list"))
    .getAllByRole("listitem")
    .map((row) => row.textContent);
}

describe("EnableFeaturesFields", () => {
  it("adds a common feature the typed text finds and names what it is", async () => {
    render(<Fields features={[]} />);

    const field = screen.getByRole("combobox");
    fill(field, "NetF");
    fireEvent.keyDown(field, { key: "ArrowDown" });
    press(await screen.findByRole("option", { name: /^NetFx3/ }));

    expect(shown()).toEqual(["1NetFx3.NET Framework 3.5"]);
    expect(screen.getByRole("combobox")).toHaveValue("");
  });

  it("takes any other name once, whatever its case", () => {
    render(<Fields features={["TelnetClient"]} />);

    fill(screen.getByRole("combobox"), " Printing-Foundation-LPDPrintService ");
    press(screen.getByRole("button", { name: "Add" }));
    fill(screen.getByRole("combobox"), "telnetclient");
    press(screen.getByRole("button", { name: "Add" }));

    expect(shown()).toEqual(["1TelnetClientTelnet client", "2Printing-Foundation-LPDPrintService"]);
  });

  it("removes a feature", () => {
    render(<Fields features={["TFTP", "NetFx3"]} />);

    press(screen.getByRole("button", { name: "Remove TFTP" }));

    expect(shown()).toEqual(["1NetFx3.NET Framework 3.5"]);
  });
});
