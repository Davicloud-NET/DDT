// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { fieldFindings, withFieldAt } from "./problems";
import { phaseRuns, railSteps } from "./sequenceView";
import { newStep } from "./steps";

describe("the editor's rail", () => {
  it("draws a label over each run of steps in the same phase", () => {
    expect(phaseRuns(["WindowsPE", "WindowsPE", "Windows", "WindowsPE"])).toEqual([
      { phase: "WindowsPE", steps: 2 },
      { phase: "Windows", steps: 1 },
      { phase: "WindowsPE", steps: 1 },
    ]);
    expect(phaseRuns([])).toEqual([]);
  });

  it("marks a step with a problem, else with a warning, and says it in words", () => {
    const steps = [
      { ...newStep("applyImage", "i"), name: "Windows 11" },
      newStep("runScript", "s"),
      { ...newStep("reboot", "r"), name: " " },
    ];
    const rail = railSteps(steps, {
      problems: [{ stepId: "i", field: "imageId", message: "Choose the image to apply." }],
      warnings: [
        { stepId: "i", field: null, message: "It is large." },
        { stepId: "s", field: "script", message: "The script is empty." },
      ],
    });

    expect(rail.map((step) => step.mark)).toEqual(["problem", "warning", undefined]);
    expect(rail.map((step) => step.label)).toEqual([
      "Step 1, Windows 11, Apply image, 1 problem, 1 warning",
      "Step 2, Run script, 1 warning",
      "Step 3, Unnamed step, Restart",
    ]);
  });
});

describe("the findings of a field", () => {
  const findings = {
    problems: [{ stepId: "s", field: "conditions[0]", message: "Compare with something." }],
    warnings: [{ stepId: "s", field: "conditions[0].value", message: "Looks odd." }],
  };

  it("keeps problems and warnings apart", () => {
    expect(fieldFindings(findings, "conditions[0].value")).toEqual({
      problems: [],
      warnings: ["Looks odd."],
    });
  });

  it("shows those of a whole condition at its value", () => {
    expect(
      fieldFindings(
        withFieldAt(findings, "conditions[0]", "conditions[0].value"),
        "conditions[0].value",
      ),
    ).toEqual({ problems: ["Compare with something."], warnings: ["Looks odd."] });
  });
});
