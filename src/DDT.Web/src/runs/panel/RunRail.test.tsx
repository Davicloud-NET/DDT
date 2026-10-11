// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { SequencePhase } from "@/sequences/sequences";
import { newStep } from "@/sequences/steps";
import { deploymentSummary, stepView } from "@/test/builders";
import { leaf } from "@/test/trees";

import { runPath } from "../runPath";
import { RunRail } from "./RunRail";

// A run whose steps were reported in the phases, after the step that makes the sequence its kind.
function renderRail(first: "writeRawImage" | "partition", ...phases: SequencePhase[]) {
  const ids = ["a", "b"].slice(0, phases.length - 1);
  const steps = [newStep(first, "w"), ...ids.map(leaf)];
  const path = runPath(
    { version: 5, steps },
    steps.map((step, index) =>
      stepView({ stepId: step.id, index, state: "Done", phase: phases[index] ?? "WindowsPE" }),
    ),
  );

  render(
    <I18nProvider i18n={i18n}>
      <RunRail run={deploymentSummary({ state: "Running" })} view={null} path={path} now={0} />
    </I18nProvider>,
  );
}

describe("the phases above a run's rail", () => {
  it("names the Linux boot environment and the installed Linux for a Linux run", () => {
    renderRail("writeRawImage", "LinuxBoot", "LinuxBoot", "Linux");

    expect(screen.getByText("In the Linux boot environment")).toBeInTheDocument();
    expect(screen.getByText("In the installed Linux")).toBeInTheDocument();
    expect(screen.queryByText(/Windows/)).not.toBeInTheDocument();
  });

  it("names Windows PE and the installed Windows for a run that installs Windows", () => {
    renderRail("partition", "WindowsPE", "Windows", "Windows");

    expect(screen.getByText("In Windows PE")).toBeInTheDocument();
    expect(screen.getByText("In the installed Windows")).toBeInTheDocument();
    expect(screen.queryByText(/Linux/)).not.toBeInTheDocument();
  });

  it("names none for a run that stays in its boot environment", () => {
    renderRail("writeRawImage", "LinuxBoot", "LinuxBoot");

    expect(screen.queryByText(/^In /)).not.toBeInTheDocument();
  });
});
