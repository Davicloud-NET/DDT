// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";

import { press } from "@/test/aria";
import { packageSummary } from "@/test/builders";

import type { ServicePackagesStep } from "../sequences";
import { newStep } from "../steps";
import { ServicePackagesFields } from "./ServicePackagesFields";

const german = packageSummary({
  id: "0193a4b2-0000-7000-8000-0000000000d1",
  name: "de-DE language pack",
  kind: "WindowsPackage",
  targets: [],
  windows: {
    content: "Language",
    languages: ["de-DE"],
    architecture: "x64",
    kbNumbers: [],
    builds: [],
  },
});

const catalog = {
  images: [],
  packages: [packageSummary(), german],
  applications: [],
  bundles: [],
  domainConfigured: null,
};

function Fields({ onStep }: { onStep: (step: ServicePackagesStep) => void }) {
  const [step, setStep] = useState(newStep("servicePackages", "s") as ServicePackagesStep);

  return (
    <I18nProvider i18n={i18n}>
      <ServicePackagesFields
        step={step}
        sequence="Windows"
        findings={{ problems: [], warnings: [], notes: [] }}
        catalog={catalog}
        onChange={(patch) => {
          setStep((before) => {
            const next = { ...before, ...patch } as ServicePackagesStep;
            onStep(next);

            return next;
          });
        }}
      />
    </I18nProvider>
  );
}

describe("ServicePackagesFields", () => {
  it("offers only Windows packages, and cleans up unless told not to", async () => {
    let last: ServicePackagesStep | null = null;
    render(
      <Fields
        onStep={(step) => {
          last = step;
        }}
      />,
    );

    press(screen.getByRole("button", { name: /Add a Windows package/ }));
    const options = await screen.findAllByRole("option");
    expect(options.map((option) => option.textContent)).toEqual(["de-DE language packLanguage"]);
    press(within(screen.getByRole("listbox")).getByRole("option", { name: /de-DE language pack/ }));
    press(screen.getByRole("checkbox", { name: "Add the updates marked for the image's build" }));
    press(screen.getByRole("checkbox", { name: "Clean up after the updates" }));

    expect(last).toMatchObject({ packages: [german.id], matchingUpdates: true, cleanup: false });
  });
});
