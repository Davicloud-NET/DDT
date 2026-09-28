// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { screen, within } from "@testing-library/react";
import { afterEach, describe, it, vi } from "vitest";

import { press } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import { deploymentSummary, machineSummary } from "@/test/builders";

import {
  assigning,
  installWindows,
  label,
  linux,
  modelRule,
  open,
  openAssign,
  waiting,
} from "./MachinesPage.fixtures";

describe("MachinesPage", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  describe("accessibility", () => {
    it("has no violations in a list with a machine picked", async () => {
      await open(
        [
          waiting({ id: "m1", assignedName: "PC-WAITING", signedInBy: "bob" }),
          machineSummary({
            id: "m2",
            assignedName: "PC-RUNNING",
            state: "Deploying",
            deployment: deploymentSummary({
              state: "Running",
              stepIndex: 1,
              stepName: "Apply image",
              percent: 45,
              startedUtc: "2026-09-16T10:00:00Z",
            }),
          }),
        ],
        {},
        { path: "/machines?selected=m2" },
      );

      await screen.findByRole("complementary", { name: "PC-RUNNING" });
      await expectNoAxeViolations();
    });

    it("has no violations while no machine has registered", async () => {
      await open([]);

      await screen.findByText("No machines yet");
      await expectNoAxeViolations();
    });

    it("has no violations on a phone", async () => {
      await open([waiting({ assignedName: "PC-042" })], {}, { narrow: true });

      await screen.findByRole("list", { name: "Machines" });
      await expectNoAxeViolations();
    });

    it("has no violations with the assign dialog open", async () => {
      await open(
        [machineSummary({ secureBootEnabled: true })],
        assigning({
          "GET /api/sequences": { body: [linux] },
        }),
      );

      const dialog = await openAssign();
      await within(dialog).findByRole("checkbox", { name: "Write noble anyway" });
      await expectNoAxeViolations();
    });

    it("has no violations with the approval confirmation open", async () => {
      const machine = waiting();
      await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: { body: modelRule() },
        "GET /api/sequences": { body: [installWindows] },
      });

      press(await screen.findByRole("button", { name: "Approve" }));
      await screen.findByRole("dialog");
      await expectNoAxeViolations();
    });

    it("has no violations with a machine's menu open", async () => {
      await open([waiting()]);

      press(await screen.findByRole("button", { name: `More for ${label}` }));
      await screen.findByRole("menu");
      await expectNoAxeViolations();
    });
  });
});
