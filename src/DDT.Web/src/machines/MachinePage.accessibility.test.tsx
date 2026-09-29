// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { screen } from "@testing-library/react";
import { afterEach, describe, it, vi } from "vitest";

import { press } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import { logLine } from "@/test/builders";

import { answers, machine, open, run, runId, view } from "./MachinePage.fixtures";

describe("MachinePage", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  describe("accessibility", () => {
    it("has no violations with a run under way", async () => {
      await open(
        answers(
          [machine()],
          [run()],
          [view()],
          [logLine(1, { deploymentId: runId, stepId: "s1", level: "Warning" })],
        ),
      );

      await screen.findByText("Line 1");
      await expectNoAxeViolations();
    });

    it("has no violations for a machine without runs", async () => {
      await open(answers([machine({ state: "Pending", deployment: null })], [], []));

      await screen.findByText("No runs yet");
      await expectNoAxeViolations();
    });

    it("has no violations with the stop confirmation open", async () => {
      await open(answers([machine()], [run()], [view()]));

      press(await screen.findByRole("button", { name: "Stop the run" }));
      await screen.findByRole("dialog");
      await expectNoAxeViolations();
    });

    it("has no violations for a removed machine", async () => {
      await open(answers([], [], []));

      await screen.findByText("This machine was removed");
      await expectNoAxeViolations();
    });
  });
});
