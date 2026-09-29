// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { chooseMenuItem, menuItems, press } from "@/test/aria";
import { deploymentSummary, machineSummary, viewer } from "@/test/builders";

import { label, moreFor, open, row, waiting } from "./MachinesPage.fixtures";

describe("MachinesPage", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  describe("stopping and cancelling", () => {
    const deploying = machineSummary({
      state: "Deploying",
      deployment: deploymentSummary({
        state: "Running",
        stepIndex: 1,
        stepName: "Apply image",
        percent: 10,
      }),
    });

    it("stops a running deployment after saying what that leaves behind", async () => {
      const { server } = await open([deploying], {
        [`DELETE /api/machines/${deploying.id}/deployments/current`]: {
          body: {
            ...deploying,
            state: "Failed",
            deployment: deploymentSummary({
              state: "Failed",
              stepIndex: 1,
              stepName: "Apply image",
              error: "Stopped by operator.",
            }),
          },
        },
      });

      await chooseMenuItem(
        await screen.findByRole("button", { name: `More for ${label}` }),
        "Stop the run",
      );
      const dialog = await screen.findByRole("dialog", { name: `Stop the run on ${label}?` });

      expect(dialog).toHaveAccessibleDescription(
        "This stops the run on Virtual Machine (00:15:5D:01:02:03). Its disk is left half written; assign a sequence again to deploy it.",
      );

      press(within(dialog).getByRole("button", { name: "Stop the run" }));

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      expect(within(row("Virtual Machine")).getByText("Step 2 failed")).toBeInTheDocument();
      expect(server.changes().map((request) => `${request.method} ${request.path}`)).toEqual([
        `DELETE /api/machines/${deploying.id}/deployments/current`,
      ]);
      expect(server.count("GET /api/machines")).toBe(1);
    });

    it("closes the stop confirmation when the deployment it was opened for ends", async () => {
      const { server, hub } = await open([deploying]);

      await chooseMenuItem(
        await screen.findByRole("button", { name: `More for ${label}` }),
        "Stop the run",
      );
      await screen.findByRole("dialog", { name: `Stop the run on ${label}?` });

      // Meanwhile the agent reported that the same run failed.
      act(() => {
        hub?.push("machineChanged", {
          ...deploying,
          state: "Failed",
          deployment: deploymentSummary({
            state: "Failed",
            stepIndex: 1,
            stepName: "Apply image",
            error: "The image could not be applied.",
          }),
        });
      });

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      expect(within(row("Virtual Machine")).getByText("Step 2 failed")).toBeInTheDocument();
      expect(await menuItems(moreFor(label))).not.toContain("Stop the run");
      expect(server.changes()).toEqual([]);
    });

    it("closes the stop confirmation when another deployment replaced the one it was opened for", async () => {
      const { server, hub } = await open([deploying]);

      await chooseMenuItem(
        await screen.findByRole("button", { name: `More for ${label}` }),
        "Stop the run",
      );
      await screen.findByRole("dialog", { name: `Stop the run on ${label}?` });

      // Meanwhile the run failed, and a technician picked another sequence at the machine.
      act(() => {
        hub?.push("machineChanged", {
          ...deploying,
          deployment: deploymentSummary({
            id: "0193a4b2-0000-7000-8000-0000000000d2",
            state: "Running",
            stepIndex: 0,
            stepName: "Partition",
            source: "Console",
            requestedBy: "tech",
          }),
        });
      });

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      expect(server.changes()).toEqual([]);

      // The new deployment can be stopped after its own confirmation.
      await chooseMenuItem(moreFor(label), "Stop the run");
      expect(
        await screen.findByRole("dialog", { name: `Stop the run on ${label}?` }),
      ).toBeInTheDocument();
    });

    it("cancels an assigned deployment", async () => {
      const assigned = machineSummary({ deployment: deploymentSummary({ state: "Assigned" }) });
      const { server } = await open([assigned], {
        [`DELETE /api/machines/${assigned.id}/deployments/current`]: {
          body: {
            ...assigned,
            deployment: deploymentSummary({
              state: "Cancelled",
              finishedUtc: new Date().toISOString(),
            }),
          },
        },
      });

      expect(await screen.findByText("Assigned by operator")).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Assign" })).not.toBeInTheDocument();

      await chooseMenuItem(moreFor(label), "Cancel the assignment");

      expect(await within(row("Virtual Machine")).findByText(/^Stopped/)).toBeInTheDocument();
      expect(server.changes().map((request) => `${request.method} ${request.path}`)).toEqual([
        `DELETE /api/machines/${assigned.id}/deployments/current`,
      ]);
      expect(
        within(row("Virtual Machine")).getByRole("button", { name: "Assign" }),
      ).toBeInTheDocument();
    });

    it("offers no actions to viewers", async () => {
      await open(
        [deploying, waiting({ id: "m2", assignedName: "PC-WAITING" })],
        {},
        { user: viewer },
      );

      expect(await screen.findByRole("link", { name: "PC-WAITING" })).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /^More for/ })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Assign" })).not.toBeInTheDocument();
    });
  });
});
