// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { fieldFindings, withFieldAt } from "./problems";

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
