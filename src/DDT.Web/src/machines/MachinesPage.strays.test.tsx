// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { chooseMenuItem, menuItems } from "@/test/aria";
import { deploymentSummary, machineSummary } from "@/test/builders";

import { moreFor, open, waiting } from "./MachinesPage.fixtures";

describe("MachinesPage", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  describe("strays", () => {
    it("offers to remove machines nobody approved, and all of them from one address once", async () => {
      await open([
        waiting({ id: "m1", assignedName: "PC-1", firstSeenAddress: "10.0.0.9" }),
        waiting({ id: "m2", assignedName: "PC-2", firstSeenAddress: "10.0.0.9" }),
        waiting({ id: "m3", assignedName: "PC-3", firstSeenAddress: "10.0.0.5" }),
        waiting({
          id: "m4",
          assignedName: "PC-4",
          firstSeenAddress: "10.0.0.9",
          everApproved: true,
        }),
      ]);

      await screen.findByRole("link", { name: "PC-1" });
      const offers: [string, boolean, string[]][] = [];

      for (const name of ["PC-1", "PC-2", "PC-3", "PC-4"]) {
        const items = await menuItems(moreFor(name));

        offers.push([
          name,
          items.includes("Remove"),
          items.filter((item) => item.startsWith("Remove all")),
        ]);
      }

      // The first of the strays from an address offers to remove them all.
      expect(offers).toEqual([
        ["PC-1", true, ["Remove all 2 waiting from 10.0.0.9"]],
        ["PC-2", true, []],
        ["PC-3", true, []],
        ["PC-4", false, []],
      ]);
    });

    it("removes every stray from one address without reading the list again", async () => {
      const { server } = await open(
        [
          waiting({ id: "m1", assignedName: "PC-1", firstSeenAddress: "10.0.0.9" }),
          waiting({ id: "m2", assignedName: "PC-2", firstSeenAddress: "10.0.0.9" }),
          waiting({ id: "m3", assignedName: "PC-3", firstSeenAddress: "10.0.0.5" }),
        ],
        { "DELETE /api/machines?waitingFrom=10.0.0.9": { status: 204 } },
      );

      await screen.findByRole("link", { name: "PC-1" });
      await chooseMenuItem(moreFor("PC-1"), "Remove all 2 waiting from 10.0.0.9");

      await waitFor(() => {
        expect(screen.queryByRole("link", { name: "PC-1" })).not.toBeInTheDocument();
      });
      expect(screen.queryByRole("link", { name: "PC-2" })).not.toBeInTheDocument();
      expect(screen.getByRole("link", { name: "PC-3" })).toBeInTheDocument();
      expect(server.count("GET /api/machines")).toBe(1);
    });

    it("offers to remove a rejected machine, but does not count it with the strays", async () => {
      const { server } = await open(
        [
          waiting({ id: "m1", assignedName: "PC-WAITING", firstSeenAddress: "10.0.0.7" }),
          machineSummary({
            id: "m2",
            assignedName: "PC-REJECTED",
            state: "Rejected",
            firstSeenAddress: "10.0.0.7",
          }),
        ],
        { "DELETE /api/machines/m2": { status: 204 } },
      );

      await screen.findByRole("link", { name: "PC-REJECTED" });
      expect(await menuItems(moreFor("PC-WAITING"))).not.toContainEqual(
        expect.stringMatching(/^Remove all/),
      );

      await chooseMenuItem(moreFor("PC-REJECTED"), "Remove");

      await waitFor(() => {
        expect(screen.queryByRole("link", { name: "PC-REJECTED" })).not.toBeInTheDocument();
      });
      expect(server.changes().map((request) => `${request.method} ${request.path}`)).toEqual([
        "DELETE /api/machines/m2",
      ]);
      expect(server.count("GET /api/machines")).toBe(1);
    });

    it("does not offer to remove a waiting machine with an assigned sequence, nor count it with the strays", async () => {
      await open([
        waiting({ id: "m1", assignedName: "PC-1", firstSeenAddress: "10.0.0.5" }),
        waiting({ id: "m2", assignedName: "PC-2", firstSeenAddress: "10.0.0.5" }),
        waiting({
          id: "m3",
          assignedName: "PC-ASSIGNED",
          firstSeenAddress: "10.0.0.5",
          deployment: deploymentSummary({ state: "Assigned" }),
        }),
      ]);

      await screen.findByRole("link", { name: "PC-ASSIGNED" });
      expect(await menuItems(moreFor("PC-ASSIGNED"))).not.toContainEqual(
        expect.stringMatching(/^Remove/),
      );

      const offers = [
        ...(await menuItems(moreFor("PC-1"))),
        ...(await menuItems(moreFor("PC-2"))),
      ].filter((item) => item.startsWith("Remove all"));
      expect(offers).toEqual(["Remove all 2 waiting from 10.0.0.5"]);
    });
  });
});
