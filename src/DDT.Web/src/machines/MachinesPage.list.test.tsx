// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { fill, press } from "@/test/aria";
import { deploymentSummary, machineSummary } from "@/test/builders";

import { fact, now, open, row, secondsBefore, waiting } from "./MachinesPage.fixtures";

describe("MachinesPage", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  describe("the list", () => {
    it("explains that no machine has registered yet", async () => {
      await open([]);

      expect(
        await screen.findByRole("heading", { level: 1, name: "All machines" }),
      ).toBeInTheDocument();
      expect(await screen.findByText("No machines yet")).toBeInTheDocument();
      expect(
        screen.getByText(/^Machines appear here on their own when they netboot/),
      ).toBeInTheDocument();
    });

    it("lists a machine with its name, hardware, state and who signed in at it", async () => {
      await open([waiting({ signedInBy: "bob" })]);

      const link = await screen.findByRole("link", { name: "Virtual Machine" });
      expect(link).toHaveAttribute("href", "/machines/0193a4b2-0000-7000-8000-000000000001");

      const machine = within(row("Virtual Machine"));
      expect(machine.getByText("Microsoft Corporation, serial 1234")).toBeInTheDocument();
      expect(machine.getByText("Waiting")).toBeInTheDocument();
      expect(machine.getByText("bob signed in at the machine")).toBeInTheDocument();
      expect(machine.getByText("Needs your approval")).toBeInTheDocument();
    });

    it("shows the machine picked in the list beside it, with its MAC and Secure Boot", async () => {
      const { router } = await open([
        machineSummary({ id: "m1", assignedName: "PC-ON", secureBootEnabled: true }),
        machineSummary({
          id: "m2",
          assignedName: "PC-OFF",
          primaryMac: "00155D0A0B0C",
          macAddresses: ["00155D0A0B0C"],
          secureBootEnabled: false,
        }),
      ]);

      press(within(await screen.findByRole("row", { name: /^PC-ON/ })).getByText("Ready"));

      await waitFor(() => {
        expect(router.state.location.search).toEqual({ selected: "m1" });
      });
      const on = screen.getByRole("complementary", { name: "PC-ON" });
      expect(fact(on, "MAC address")).toBe("00:15:5D:01:02:03");
      expect(fact(on, "Secure Boot")).toBe("On");
      expect(within(on).getByRole("link", { name: "Open machine" })).toHaveAttribute(
        "href",
        "/machines/m1",
      );

      press(within(row("PC-OFF")).getByText("Ready"));

      const off = await screen.findByRole("complementary", { name: "PC-OFF" });
      expect(fact(off, "MAC address")).toBe("00:15:5D:0A:0B:0C");
      expect(fact(off, "Secure Boot")).toBe("Off");

      press(within(off).getByRole("button", { name: "Close the details" }));

      await waitFor(() => {
        expect(screen.queryByRole("complementary")).not.toBeInTheDocument();
      });
      expect(router.state.location.search).toEqual({});
    });

    it("filters by state and finds a machine by its MAC with or without separators", async () => {
      const { router } = await open([
        waiting({ id: "m1", assignedName: "PC-WAITING" }),
        machineSummary({
          id: "m2",
          assignedName: "PC-READY",
          primaryMac: "00155D0A0B0C",
          macAddresses: ["00155D0A0B0C"],
        }),
      ]);

      await screen.findByRole("link", { name: "PC-READY" });
      const filters = screen.getByRole("radiogroup", { name: "Show machines by state" });
      press(within(filters).getByRole("radio", { name: /^Waiting/ }));

      await waitFor(() => {
        expect(screen.queryByRole("link", { name: "PC-READY" })).not.toBeInTheDocument();
      });
      expect(screen.getByRole("link", { name: "PC-WAITING" })).toBeInTheDocument();
      expect(router.state.location.search).toEqual({ state: "waiting" });

      press(within(filters).getByRole("radio", { name: /^All/ }));
      fill(screen.getByRole("searchbox", { name: "Find a machine" }), "00-15-5d-0a-0b-0c");

      await waitFor(() => {
        expect(screen.queryByRole("link", { name: "PC-WAITING" })).not.toBeInTheDocument();
      });
      expect(screen.getByRole("link", { name: "PC-READY" })).toBeInTheDocument();

      fill(screen.getByRole("searchbox", { name: "Find a machine" }), "PC-NONE");

      expect(await screen.findByText("No machine matches")).toBeInTheDocument();
      press(screen.getByRole("button", { name: "Show all machines" }));

      expect(await screen.findByRole("link", { name: "PC-WAITING" })).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "PC-READY" })).toBeInTheDocument();
    });

    it("lists machines as rows of their own on a phone", async () => {
      await open([waiting({ assignedName: "PC-042" })], {}, { narrow: true });

      const list = await screen.findByRole("list", { name: "Machines" });
      expect(within(list).getByRole("link", { name: "PC-042" })).toBeInTheDocument();
      expect(within(list).getByText("Waiting")).toBeInTheDocument();
      expect(within(list).getByRole("button", { name: "Approve" })).toBeInTheDocument();
      expect(screen.queryByRole("grid")).not.toBeInTheDocument();
    });

    it("shows each machine's run: the step and its percent, why no step runs, and where it failed", async () => {
      vi.useFakeTimers({ toFake: ["Date"], now });

      await open(
        [
          machineSummary({
            id: "m1",
            assignedName: "PC-RUNNING",
            state: "Deploying",
            deployment: deploymentSummary({
              stepCount: 9,
              state: "Running",
              stepIndex: 3,
              stepName: "Apply image",
              percent: 45,
              phase: "WindowsPE",
              activity: "Step",
              startedUtc: secondsBefore(125),
            }),
          }),
          machineSummary({
            id: "m7",
            assignedName: "PC-RESTARTING",
            state: "Deploying",
            lastSeenUtc: secondsBefore(60),
            deployment: deploymentSummary({
              stepCount: 9,
              state: "Running",
              stepIndex: 4,
              stepName: "Restart",
              phase: "WindowsPE",
              activity: "Restarting",
              startedUtc: secondsBefore(600),
            }),
          }),
          machineSummary({
            id: "m8",
            assignedName: "PC-SETUP",
            state: "Deploying",
            lastSeenUtc: secondsBefore(720),
            deployment: deploymentSummary({
              stepCount: 9,
              state: "Running",
              stepIndex: 5,
              stepName: "Write the answer file",
              phase: "Windows",
              activity: "WaitingForWindowsSetup",
              startedUtc: secondsBefore(1800),
            }),
          }),
          // The check before the start failed, so the disk was never touched and no step is recorded.
          machineSummary({
            id: "m2",
            assignedName: "PC-FAILED",
            state: "Failed",
            deployment: deploymentSummary({
              stepCount: 9,
              state: "Failed",
              error: "The disk is smaller than the image needs.",
              finishedUtc: secondsBefore(500),
            }),
          }),
          machineSummary({
            id: "m6",
            assignedName: "PC-STOPPED",
            state: "Failed",
            deployment: deploymentSummary({
              stepCount: 9,
              state: "Failed",
              stepIndex: 4,
              stepName: "Install agent",
              percent: 40,
              error: "Stopped by operator.",
              startedUtc: secondsBefore(900),
              finishedUtc: secondsBefore(800),
            }),
          }),
          machineSummary({
            id: "m3",
            assignedName: "PC-DONE",
            state: "Done",
            deployment: deploymentSummary({
              stepCount: 9,
              state: "Done",
              stepIndex: 8,
              stepName: "Restart",
              percent: 100,
              startedUtc: secondsBefore(1500),
              finishedUtc: secondsBefore(300),
            }),
          }),
          machineSummary({
            id: "m4",
            assignedName: "PC-CANCELLED",
            deployment: deploymentSummary({ state: "Cancelled" }),
          }),
          machineSummary({
            id: "m9",
            assignedName: "PC-RULE",
            deployment: deploymentSummary({ state: "Assigned", source: "Rule" }),
          }),
          machineSummary({ id: "m5", assignedName: "PC-NONE" }),
        ],
        {},
        { path: "/machines?selected=m1" },
      );

      const running = within(await screen.findByRole("row", { name: /^PC-RUNNING/ }));
      expect(running.getByText("Install Windows")).toBeInTheDocument();
      expect(running.getByText("Apply image 45%")).toBeInTheDocument();
      expect(
        running.getByRole("img", { name: "Step 4 of 9 running, 45 percent" }),
      ).toBeInTheDocument();
      expect(running.queryByText(/last contact/)).not.toBeInTheDocument();

      const panel = within(screen.getByRole("complementary", { name: "PC-RUNNING" }));
      expect(panel.getByText("Step 4 of 9")).toBeInTheDocument();
      expect(panel.getByText("Running for 2 min 5 s")).toBeInTheDocument();

      expect(
        within(row("PC-RESTARTING")).getByText("Restarting, last contact 1 minute ago"),
      ).toBeInTheDocument();
      expect(
        within(row("PC-SETUP")).getByText("Waiting for Windows setup, last contact 12 minutes ago"),
      ).toBeInTheDocument();

      // No step ran, so none failed.
      const failed = within(row("PC-FAILED"));
      expect(failed.getAllByText("Failed")).toHaveLength(2);
      expect(failed.queryByText(/Step \d+ failed/)).not.toBeInTheDocument();
      expect(failed.getByRole("img", { name: "Not started, 9 steps" })).toBeInTheDocument();

      const stopped = within(row("PC-STOPPED"));
      expect(stopped.getByText("Step 5 failed")).toBeInTheDocument();
      expect(stopped.getByRole("img", { name: "Failed at step 5 of 9" })).toBeInTheDocument();

      expect(
        within(row("PC-DONE")).getByText("Took 20 min 0 s, finished 5 minutes ago"),
      ).toBeInTheDocument();
      expect(within(row("PC-CANCELLED")).getByText("Stopped")).toBeInTheDocument();
      expect(
        within(row("PC-RULE")).getByText("Approved by operator with the sequence a rule chose"),
      ).toBeInTheDocument();
      expect(within(row("PC-NONE")).getByText("No sequence assigned")).toBeInTheDocument();
    });

    it("puts the machines that need someone first", async () => {
      await open([
        machineSummary({ id: "m1", assignedName: "PC-DONE", state: "Done" }),
        waiting({ id: "m2", assignedName: "PC-WAITING" }),
        machineSummary({ id: "m3", assignedName: "PC-FAILED", state: "Failed" }),
      ]);

      await screen.findByRole("link", { name: "PC-DONE" });
      expect(
        screen
          .getAllByRole("row")
          .slice(1)
          .map((each) => within(each).getByRole("link").textContent),
      ).toEqual(["PC-WAITING", "PC-FAILED", "PC-DONE"]);
    });
  });
});
