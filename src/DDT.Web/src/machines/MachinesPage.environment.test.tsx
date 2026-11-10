// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { press } from "@/test/aria";
import {
  deploymentOptions,
  deploymentSummary,
  machineSummary,
  sequenceSummary,
} from "@/test/builders";

import {
  assignable,
  assigning,
  fact,
  installLinuxId,
  label,
  modelRule,
  now,
  open,
  openAssign,
  row,
  waiting,
} from "./MachinesPage.fixtures";

const installLinux = sequenceSummary({
  id: installLinuxId,
  name: "Install Linux",
  stepCount: 2,
  kind: "Linux",
});

const restartsFirst = "The machine restarts into the Linux boot environment first.";

const staysAssigned =
  "Install Linux runs in the Linux boot environment, which this server does not have. It waits until the server has it.";

// A waiting machine in Windows PE whose rule chooses Install Linux, with the approval dialog open.
async function openApproval(bootEnvironments: ("WindowsPE" | "LinuxBoot")[]): Promise<HTMLElement> {
  const machine = waiting({ agentEnvironment: "WindowsPE" });

  await open([machine], {
    [`GET /api/machines/${machine.id}/sequence`]: {
      body: modelRule({ sequenceId: installLinuxId, sequenceName: "Install Linux" }),
    },
    "GET /api/sequences": { body: [installLinux] },
    "GET /api/deployments/options": {
      body: deploymentOptions({ serverUtc: now.toISOString(), bootEnvironments }),
    },
  });
  press(await screen.findByRole("button", { name: "Approve" }));

  return screen.findByRole("dialog", { name: `Approve ${label}?` });
}

describe("MachinesPage", () => {
  describe("boot environments", () => {
    it("says what a machine without a run started, in its row and beside the list", async () => {
      await open([
        machineSummary({ id: "m1", assignedName: "PC-LINUX", agentEnvironment: "LinuxBoot" }),
        machineSummary({ id: "m2", assignedName: "PC-WINDOWS", agentEnvironment: "WindowsPE" }),
        machineSummary({ id: "m3", assignedName: "PC-OLD" }),
      ]);

      await screen.findByRole("link", { name: "PC-LINUX" });
      expect(
        within(row("PC-LINUX")).getByText("Started the Linux boot environment"),
      ).toBeInTheDocument();
      expect(within(row("PC-WINDOWS")).getByText("Started Windows PE")).toBeInTheDocument();
      expect(within(row("PC-OLD")).queryByText(/^Started/)).not.toBeInTheDocument();

      press(within(row("PC-LINUX")).getByText("Ready"));

      const details = await screen.findByRole("complementary", { name: "PC-LINUX" });
      expect(fact(details, "Environment")).toBe("Linux boot environment");
      expect(within(details).queryByText("Restarts into")).not.toBeInTheDocument();
    });

    it("shows a restart into the other boot environment as the hub pushes it, and its end", async () => {
      const machine = machineSummary({ assignedName: "PC-042", agentEnvironment: "WindowsPE" });
      const { server, hub } = await open([machine]);

      await screen.findByRole("link", { name: "PC-042" });
      press(within(row("PC-042")).getByText("Ready"));
      const details = await screen.findByRole("complementary", { name: "PC-042" });

      act(() => {
        hub?.push("machineChanged", { ...machine, nextEnvironment: "LinuxBoot" });
      });

      expect(
        await within(row("PC-042")).findByText("Restarts into the Linux boot environment"),
      ).toBeInTheDocument();
      expect(fact(details, "Environment")).toBe("Windows PE");
      expect(fact(details, "Restarts into")).toBe("Linux boot environment");

      act(() => {
        hub?.push("machineChanged", {
          ...machine,
          agentEnvironment: "LinuxBoot",
          nextEnvironment: null,
        });
      });

      expect(
        await within(row("PC-042")).findByText("Started the Linux boot environment"),
      ).toBeInTheDocument();
      expect(fact(details, "Environment")).toBe("Linux boot environment");
      expect(within(details).queryByText("Restarts into")).not.toBeInTheDocument();
      expect(server.count("GET /api/machines")).toBe(1);
    });

    it("says under a machine's earlier run that it restarts into the other boot environment", async () => {
      await open([
        machineSummary({
          assignedName: "PC-FAILED",
          state: "Failed",
          agentEnvironment: "LinuxBoot",
          nextEnvironment: "WindowsPE",
          deployment: deploymentSummary({ state: "Failed", title: "Install Linux" }),
        }),
      ]);

      await screen.findByRole("link", { name: "PC-FAILED" });
      const failed = within(row("PC-FAILED"));
      expect(failed.getByText("Install Linux")).toBeInTheDocument();
      expect(failed.getByText("Restarts into Windows PE")).toBeInTheDocument();
      expect(failed.queryByText(/^Started/)).not.toBeInTheDocument();
    });

    it("says in the row of a machine the server no longer restarts which boot environment it did not start", async () => {
      const machine = machineSummary({
        assignedName: "PC-BIOS",
        agentEnvironment: "WindowsPE",
        nextEnvironment: "LinuxBoot",
        deployment: deploymentSummary({ state: "Assigned", title: "Install Linux" }),
      });
      const { server, hub } = await open([machine]);

      await screen.findByRole("link", { name: "PC-BIOS" });
      const stuck = within(row("PC-BIOS"));
      expect(stuck.getByText("Restarts into the Linux boot environment")).toBeInTheDocument();
      expect(stuck.queryByText(/after restarting/)).not.toBeInTheDocument();

      act(() => {
        hub?.push("machineChanged", {
          ...machine,
          nextEnvironment: null,
          restartsFailed: "LinuxBoot",
        });
      });

      expect(
        await stuck.findByText("Did not start the Linux boot environment after restarting"),
      ).toBeInTheDocument();
      expect(stuck.queryByText(/^Restarts into/)).not.toBeInTheDocument();
      expect(stuck.getByText("Install Linux")).toBeInTheDocument();
      expect(server.count("GET /api/machines")).toBe(1);
    });

    it("warns before assigning to a machine whose agent is too old to change the boot environment", async () => {
      await open(
        [machineSummary({ agentEnvironment: "WindowsPE", changesBootEnvironment: false })],
        assigning({
          "GET /api/sequences": { body: [installLinux] },
          "GET /api/deployments/options": {
            body: deploymentOptions({
              serverUtc: now.toISOString(),
              bootEnvironments: ["WindowsPE", "LinuxBoot"],
            }),
          },
        }),
      );

      const dialog = await openAssign();
      await assignable(dialog);

      expect(
        await within(dialog).findByText(
          "Install Linux runs in the Linux boot environment, and this machine's agent is too old to restart into it. It starts once the machine is started from the network again.",
        ),
      ).toBeInTheDocument();
      expect(within(dialog).queryByText(restartsFirst)).not.toBeInTheDocument();
    });

    it("tells before assigning that the machine restarts into the other boot environment first", async () => {
      await open(
        [machineSummary({ agentEnvironment: "WindowsPE" })],
        assigning({
          "GET /api/sequences": { body: [installLinux] },
          "GET /api/deployments/options": {
            body: deploymentOptions({
              serverUtc: now.toISOString(),
              bootEnvironments: ["WindowsPE", "LinuxBoot"],
            }),
          },
        }),
      );

      const dialog = await openAssign();

      expect(await within(dialog).findByText(restartsFirst)).toBeInTheDocument();
      expect(within(dialog).queryByText(/this server does not have/)).not.toBeInTheDocument();
      await assignable(dialog);
    });

    it("warns before assigning when the server cannot start that boot environment, and still assigns", async () => {
      await open(
        [machineSummary({ agentEnvironment: "WindowsPE" })],
        assigning({
          "GET /api/sequences": { body: [installLinux] },
          "GET /api/deployments/options": {
            body: deploymentOptions({
              serverUtc: now.toISOString(),
              bootEnvironments: ["WindowsPE"],
            }),
          },
        }),
      );

      const dialog = await openAssign();

      expect(await within(dialog).findByText(staysAssigned)).toBeInTheDocument();
      expect(within(dialog).queryByText(restartsFirst)).not.toBeInTheDocument();
      await assignable(dialog);
    });

    it("tells before an approval that runs a rule's sequence that the machine restarts first", async () => {
      const dialog = await openApproval(["WindowsPE", "LinuxBoot"]);

      expect(within(dialog).getByText(/also runs Install Linux on it/)).toBeInTheDocument();
      expect(await within(dialog).findByText(restartsFirst)).toBeInTheDocument();
      expect(within(dialog).queryByText(staysAssigned)).not.toBeInTheDocument();
    });

    it("warns before such an approval when the server cannot start that boot environment", async () => {
      const dialog = await openApproval(["WindowsPE"]);

      expect(await within(dialog).findByText(staysAssigned)).toBeInTheDocument();
      expect(within(dialog).queryByText(restartsFirst)).not.toBeInTheDocument();
      expect(
        within(dialog).getByRole("button", { name: "Approve and run Install Linux" }),
      ).toBeEnabled();
    });

    it("says nothing about boot environments where the machine waits in the one the sequence starts in", async () => {
      await open(
        [machineSummary({ agentEnvironment: "LinuxBoot" })],
        assigning({
          "GET /api/sequences": { body: [installLinux] },
          "GET /api/deployments/options": {
            body: deploymentOptions({ serverUtc: now.toISOString(), bootEnvironments: [] }),
          },
        }),
      );

      const dialog = await openAssign();

      expect(
        await within(dialog).findByText(/^Install Linux erases all data on the disk/),
      ).toBeInTheDocument();
      await assignable(dialog);
      expect(within(dialog).queryByText(/boot environment/)).not.toBeInTheDocument();
    });
  });
});
