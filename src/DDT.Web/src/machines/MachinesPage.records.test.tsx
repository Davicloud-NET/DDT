// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { chooseMenuItem, fill, press } from "@/test/aria";
import { machineSummary } from "@/test/builders";
import { json } from "@/test/server";

import { moreFor, open, waiting } from "./MachinesPage.fixtures";

describe("MachinesPage", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  describe("recovery keys", () => {
    it("finds a machine by the start of a recovery key ID", async () => {
      const { server } = await open(
        [
          waiting({ id: "m1", assignedName: "PC-KEY" }),
          waiting({ id: "m2", assignedName: "PC-OTHER" }),
        ],
        { "GET /api/machines/recovery-keys?keyId=8f0a1c2d": { body: ["m1"] } },
      );

      await screen.findByRole("link", { name: "PC-OTHER" });
      fill(screen.getByRole("searchbox", { name: "Find a machine" }), "8F0A1C2D");

      await waitFor(() => {
        expect(server.count("GET /api/machines/recovery-keys?keyId=8f0a1c2d")).toBe(1);
      });
      await waitFor(() => {
        expect(screen.queryByRole("link", { name: "PC-OTHER" })).not.toBeInTheDocument();
      });
      expect(await screen.findByRole("link", { name: "PC-KEY" })).toBeInTheDocument();
      expect(server.count("GET /api/machines/recovery-keys?keyId=8f0a1c2d")).toBe(1);
    });

    it("does not ask the server for text that can't be a key ID", async () => {
      const { server } = await open([waiting({ id: "m1", assignedName: "PC-1" })]);

      await screen.findByRole("link", { name: "PC-1" });
      fill(screen.getByRole("searchbox", { name: "Find a machine" }), "8F0A1C2");

      expect(await screen.findByText("No machine matches")).toBeInTheDocument();
      expect(server.requests.some((request) => request.path.includes("recovery-keys"))).toBe(false);
    });

    it("removes a machine with recovery keys once their count is confirmed", async () => {
      const { server } = await open(
        [machineSummary({ id: "m2", assignedName: "PC-REJECTED", state: "Rejected" })],
        {
          "DELETE /api/machines/m2": () =>
            json(
              {
                title:
                  "The machine has 2 recovery keys, which are deleted with it. Confirm to remove it.",
                code: "machine.removeRecoveryKeys",
                args: { count: 2 },
              },
              409,
            ),
          "DELETE /api/machines/m2?recoveryKeys=2": { status: 204 },
        },
      );

      await screen.findByRole("link", { name: "PC-REJECTED" });
      await chooseMenuItem(moreFor("PC-REJECTED"), "Remove");

      const dialog = await screen.findByRole("dialog", {
        name: "Remove PC-REJECTED and its recovery keys?",
      });
      expect(dialog).toHaveTextContent("DDT holds 2 recovery keys of this machine.");
      expect(screen.queryByText(/Confirm to remove it/)).not.toBeInTheDocument();

      press(within(dialog).getByRole("button", { name: "Remove" }));

      await waitFor(() => {
        expect(screen.queryByRole("link", { name: "PC-REJECTED" })).not.toBeInTheDocument();
      });
      expect(server.changes().map((request) => `${request.method} ${request.path}`)).toEqual([
        "DELETE /api/machines/m2",
        "DELETE /api/machines/m2?recoveryKeys=2",
      ]);
    });
  });
});
