// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { press } from "@/test/aria";
import { deploymentOptions, machineSummary, sequenceSummary } from "@/test/builders";

import {
  assignable,
  assigning,
  fact,
  installLinuxId,
  now,
  open,
  openAssign,
  row,
} from "./MachinesPage.fixtures";

const installLinux = sequenceSummary({
  id: installLinuxId,
  name: "Install Linux",
  stepCount: 2,
  kind: "Linux",
});

const restartsFirst =
  "Install Linux runs in the Linux boot environment, and this machine started Windows PE. The machine restarts into the Linux boot environment first.";

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

      expect(
        await within(dialog).findByText(
          "Install Linux runs in the Linux boot environment, which this server does not have. The run stays assigned and does not start until the server has it.",
        ),
      ).toBeInTheDocument();
      expect(within(dialog).queryByText(restartsFirst)).not.toBeInTheDocument();
      await assignable(dialog);
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
