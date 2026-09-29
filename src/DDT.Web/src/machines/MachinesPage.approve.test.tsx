// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { chooseMenuItem, press } from "@/test/aria";
import { deploymentSummary, sequenceResolution, sequenceSummary } from "@/test/builders";

import {
  installLinuxId,
  installWindows,
  installWindowsId,
  label,
  linux,
  modelRule,
  open,
  row,
  waiting,
} from "./MachinesPage.fixtures";

describe("MachinesPage", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  describe("approving", () => {
    it("approves a waiting machine at once when no rule chooses its sequence", async () => {
      const machine = waiting();
      const { server } = await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: { body: sequenceResolution() },
        [`POST /api/machines/${machine.id}/approve`]: {
          body: { ...machine, state: "Approved", everApproved: true },
        },
      });

      press(await screen.findByRole("button", { name: "Approve" }));

      expect(await within(row("Virtual Machine")).findByText("Ready")).toBeInTheDocument();
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(server.changes().map((request) => [request.path, request.body])).toEqual([
        [`/api/machines/${machine.id}/approve`, null],
      ]);
      expect(server.count("GET /api/machines")).toBe(1);
    });

    it("confirms an approval that runs the sequence a rule chose, and names that sequence to the server", async () => {
      const machine = waiting();
      const { server } = await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: { body: modelRule() },
        "GET /api/sequences": { body: [installWindows] },
        [`POST /api/machines/${machine.id}/approve`]: {
          body: {
            ...machine,
            state: "Approved",
            everApproved: true,
            deployment: deploymentSummary({ source: "Rule" }),
          },
        },
      });

      press(await screen.findByRole("button", { name: "Approve" }));
      const dialog = await screen.findByRole("dialog", { name: `Approve ${label}?` });

      expect(
        within(dialog).getByText(
          "Approving Virtual Machine (00:15:5D:01:02:03) also runs Install Windows on it, which a rule for its model chose. All data on its disk is erased.",
        ),
      ).toBeInTheDocument();
      expect(server.changes()).toEqual([]);

      press(within(dialog).getByRole("button", { name: "Approve and run Install Windows" }));

      expect(
        await screen.findByText("Approved by operator with the sequence a rule chose"),
      ).toBeInTheDocument();
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(server.changes().map((request) => request.body)).toEqual([
        { expectedSequenceId: installWindowsId },
      ]);
    });

    it("approves with a rule's raw disk image not signed for Secure Boot only once allowed", async () => {
      const machine = waiting({ secureBootEnabled: true });
      const { server } = await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: {
          body: modelRule({ sequenceId: installLinuxId, sequenceName: "Install Linux" }),
        },
        "GET /api/sequences": { body: [linux] },
        [`POST /api/machines/${machine.id}/approve`]: {
          body: { ...machine, state: "Approved", everApproved: true },
        },
      });

      press(await screen.findByRole("button", { name: "Approve" }));
      const dialog = await screen.findByRole("dialog", { name: `Approve ${label}?` });
      const confirm = within(dialog).getByRole("button", { name: "Approve and run Install Linux" });

      // The warning is part of what the dialog says as it opens.
      expect(dialog).toHaveAccessibleDescription(
        /which a rule for its model chose\. All data on its disk is erased\. noble is not signed for Secure Boot, and this machine has Secure Boot on\./,
      );
      expect(confirm).toBeDisabled();

      press(within(dialog).getByRole("checkbox", { name: "Write noble anyway" }));
      press(confirm);

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      expect(server.changes().map((request) => request.body)).toEqual([
        { expectedSequenceId: installLinuxId, allowSecureBootMismatch: true },
      ]);
    });

    it("reads what the rules choose afresh before an approval, however recently it was read", async () => {
      const machine = waiting();
      const { server, queryClient } = await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: { body: modelRule() },
        "GET /api/sequences": { body: [installWindows] },
      });

      // As the application keeps reads, with copies that no longer hold.
      queryClient.setDefaultOptions({ queries: { retry: false, staleTime: 30_000 } });
      queryClient.setQueryData(["machine-sequence", machine.id], sequenceResolution());
      queryClient.setQueryData(["sequences"], [sequenceSummary({ erasesDisk: false })]);

      press(await screen.findByRole("button", { name: "Approve" }));

      const dialog = await screen.findByRole("dialog");
      expect(
        within(dialog).getByText(
          "Approving Virtual Machine (00:15:5D:01:02:03) also runs Install Windows on it, which a rule for its model chose. All data on its disk is erased.",
        ),
      ).toBeInTheDocument();
      expect(server.changes()).toEqual([]);
    });

    it("closes the approval confirmation once someone else decided", async () => {
      const machine = waiting();
      const { hub } = await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: { body: modelRule() },
        "GET /api/sequences": { body: [installWindows] },
      });

      press(await screen.findByRole("button", { name: "Approve" }));
      await screen.findByRole("dialog");

      act(() => {
        hub?.push("machineChanged", { ...machine, state: "Rejected" });
      });

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
    });

    it("shows in the confirmation why the rules no longer choose that sequence", async () => {
      const machine = waiting();
      await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: { body: modelRule() },
        "GET /api/sequences": { body: [installWindows] },
        [`POST /api/machines/${machine.id}/approve`]: {
          status: 409,
          body: { title: "The rules no longer choose that sequence for this machine." },
        },
      });

      press(await screen.findByRole("button", { name: "Approve" }));
      const dialog = await screen.findByRole("dialog");
      press(within(dialog).getByRole("button", { name: "Approve and run Install Windows" }));

      expect(await within(dialog).findByRole("alert")).toHaveTextContent(
        "The rules no longer choose that sequence for this machine.",
      );
    });

    it("approves without running a rule's sequence that has problems, after saying so", async () => {
      const machine = waiting();
      const { server } = await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: { body: modelRule({ problemCount: 2 }) },
        "GET /api/sequences": { body: [sequenceSummary({ problemCount: 2 })] },
        [`POST /api/machines/${machine.id}/approve`]: {
          body: { ...machine, state: "Approved", everApproved: true },
        },
      });

      press(await screen.findByRole("button", { name: "Approve" }));
      const dialog = await screen.findByRole("dialog");

      expect(
        within(dialog).getByText(
          "A rule for its model chooses Install Windows, but it has 2 problems and cannot run until they are fixed. Approving authorizes Virtual Machine (00:15:5D:01:02:03) without running anything.",
        ),
      ).toBeInTheDocument();

      press(within(dialog).getByRole("button", { name: "Approve without a sequence" }));

      await waitFor(() => {
        expect(server.changes().map((request) => [request.path, request.body])).toEqual([
          [`/api/machines/${machine.id}/approve`, null],
        ]);
      });
    });

    it("does not approve when what the rules choose cannot be read", async () => {
      const machine = waiting();
      const { server } = await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: {
          status: 500,
          body: { title: "The server failed." },
        },
      });

      press(await screen.findByRole("button", { name: "Approve" }));

      expect(await screen.findByRole("alert")).toHaveTextContent("The server failed.");
      expect(server.changes()).toEqual([]);
      expect(within(row("Virtual Machine")).getByText("Waiting")).toBeInTheDocument();
    });

    it("rejects a machine from its menu", async () => {
      const machine = waiting();
      const { server } = await open([machine], {
        [`POST /api/machines/${machine.id}/reject`]: { body: { ...machine, state: "Rejected" } },
      });

      await chooseMenuItem(
        await screen.findByRole("button", { name: `More for ${label}` }),
        "Reject",
      );

      expect(await within(row("Virtual Machine")).findByText("Rejected")).toBeInTheDocument();
      expect(server.changes().map((request) => request.path)).toEqual([
        `/api/machines/${machine.id}/reject`,
      ]);
    });
  });
});
