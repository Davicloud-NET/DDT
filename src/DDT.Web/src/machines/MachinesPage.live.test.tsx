// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { machineSummary } from "@/test/builders";

import { open, row, waiting } from "./MachinesPage.fixtures";

describe("MachinesPage", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  describe("live updates", () => {
    it("patches a machine the hub pushes into its row, without reading the list again", async () => {
      const { server, hub } = await open([waiting({ assignedName: "PC-042" })]);

      await screen.findByRole("link", { name: "PC-042" });

      act(() => {
        hub?.push("machineChanged", machineSummary({ assignedName: "PC-042", signedInBy: "bob" }));
        hub?.push("machineChanged", machineSummary({ id: "m2", assignedName: "PC-NEW" }));
      });

      expect(await screen.findByRole("link", { name: "PC-NEW" })).toBeInTheDocument();
      expect(within(row("PC-042")).getByText("Ready")).toBeInTheDocument();
      expect(server.count("GET /api/machines")).toBe(1);
    });

    it("flashes a machine whose state the hub changed, and lets one that appears enter", async () => {
      const { hub } = await open([
        waiting({ assignedName: "PC-042" }),
        waiting({ id: "m3", assignedName: "PC-QUIET" }),
      ]);

      await screen.findByRole("link", { name: "PC-042" });
      expect(row("PC-042").className).not.toMatch(/live-/);

      act(() => {
        hub?.push("machineChanged", machineSummary({ assignedName: "PC-042" }));
        hub?.push("machineChanged", machineSummary({ id: "m2", assignedName: "PC-NEW" }));
        hub?.push(
          "machineChanged",
          waiting({ id: "m3", assignedName: "PC-QUIET", signedInBy: "bob" }),
        );
      });

      await screen.findByRole("link", { name: "PC-NEW" });
      // Ready, so it flashes in the quiet tone of that state.
      expect(row("PC-042")).toHaveClass("live-flash", "live-tone-idle");
      expect(row("PC-NEW")).toHaveClass("live-new");
      // Someone signed in at it, but it still waits: its state did not change.
      expect(row("PC-QUIET").className).not.toMatch(/live-/);
    });

    it("marks nothing it read again after a reconnect", async () => {
      const { server, hub } = await open([waiting({ assignedName: "PC-042" })]);

      await screen.findByRole("link", { name: "PC-042" });

      server.routes["GET /api/machines"] = {
        body: [
          machineSummary({ assignedName: "PC-042", state: "Failed" }),
          machineSummary({ id: "m2", assignedName: "PC-NEW" }),
        ],
      };
      act(() => {
        hub?.loseConnection();
        hub?.reconnect();
      });

      await screen.findByRole("link", { name: "PC-NEW" });
      expect(within(row("PC-042")).getByText("Failed")).toBeInTheDocument();
      expect(row("PC-042").className).not.toMatch(/live-/);
      expect(row("PC-NEW").className).not.toMatch(/live-/);
    });

    it("drops the machines the hub says were removed", async () => {
      const { server, hub } = await open(
        [
          waiting({ id: "m1", assignedName: "PC-1" }),
          waiting({ id: "m2", assignedName: "PC-2" }),
          waiting({ id: "m3", assignedName: "PC-3" }),
        ],
        {},
        { path: "/machines?selected=m2" },
      );

      await screen.findByRole("complementary", { name: "PC-2" });

      act(() => {
        hub?.push("machinesRemoved", { machineIds: ["m1", "m2"] });
      });

      await waitFor(() => {
        expect(screen.queryByRole("link", { name: "PC-1" })).not.toBeInTheDocument();
      });
      expect(screen.queryByRole("link", { name: "PC-2" })).not.toBeInTheDocument();
      expect(screen.queryByRole("complementary")).not.toBeInTheDocument();
      expect(screen.getByRole("link", { name: "PC-3" })).toBeInTheDocument();
      expect(server.count("GET /api/machines")).toBe(1);
    });

    it("reads the list every 5 s only while the live connection is down", async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      const { server, hub } = await open([machineSummary()]);
      const reads = () => server.count("GET /api/machines");

      await screen.findByRole("link", { name: "Virtual Machine" });
      await act(() => vi.advanceTimersByTimeAsync(15_000));
      expect(reads()).toBe(1);

      act(() => {
        hub?.loseConnection();
      });
      await act(() => vi.advanceTimersByTimeAsync(5_000));
      expect(reads()).toBe(2);
      await act(() => vi.advanceTimersByTimeAsync(5_000));
      expect(reads()).toBe(3);

      // Back up, the list is read once for what the hub sent meanwhile, and then only pushed.
      await act(async () => {
        hub?.reconnect();
        await vi.advanceTimersByTimeAsync(15_000);
      });
      expect(reads()).toBe(4);
    });

    it("reads the list every 5 s without a live connection", async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      const { server } = await open([machineSummary()], {}, { live: false });

      await screen.findByRole("link", { name: "Virtual Machine" });
      const first = server.count("GET /api/machines");
      await act(() => vi.advanceTimersByTimeAsync(10_000));

      expect(server.count("GET /api/machines")).toBe(first + 2);
    });
  });
});
