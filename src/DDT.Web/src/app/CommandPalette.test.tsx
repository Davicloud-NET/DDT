// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { fill, press } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import { administrator, imageSummary, machineSummary, sequenceSummary } from "@/test/builders";
import { renderPage } from "@/test/renderPage";

const machines = [
  machineSummary({ id: "m1", assignedName: "PC-042" }),
  machineSummary({
    id: "m2",
    assignedName: "LAB-07",
    primaryMac: "00155D0A0B0C",
    macAddresses: ["00155D0A0B0C"],
    serialNumber: "5CG1234XYZ",
  }),
];

function open() {
  return renderPage({
    path: "/account",
    user: administrator,
    palette: true,
    routes: {
      "GET /api/machines": { body: machines },
      "GET /api/sequences": { body: [sequenceSummary({ id: "s1", name: "Install Windows" })] },
      "GET /api/images": { body: [imageSummary()] },
    },
  });
}

function openWithKeys(): Promise<HTMLElement> {
  fireEvent.keyDown(window, { key: "k", ctrlKey: true });

  return screen.findByRole("dialog", { name: "Go to a page, machine, sequence or image" });
}

function results(dialog: HTMLElement): string[] {
  return within(within(dialog).getByRole("menu", { name: "Results" }))
    .queryAllByRole("menuitem")
    .map((item) => item.textContent);
}

describe("CommandPalette", () => {
  it("opens with Ctrl K and lists pages, machines, sequences and images", async () => {
    await open();
    await screen.findByRole("heading", { level: 1, name: "Account and security" });

    const dialog = await openWithKeys();

    expect(within(dialog).getByRole("searchbox", { name: "Search" })).toHaveFocus();
    await waitFor(() => {
      expect(results(dialog)).toContain("PC-042Virtual Machine, serial 1234");
    });
    expect(results(dialog)).toEqual(
      expect.arrayContaining([
        "All machinesMachines",
        "Assignment rulesDeployment",
        "Install Windows",
        "Windows 11 Proinstall.wim",
      ]),
    );
  });

  it("opens from its key in the frame and closes with Escape", async () => {
    await open();

    press(await screen.findByRole("button", { name: /^Go to…/ }));
    const dialog = await screen.findByRole("dialog", {
      name: "Go to a page, machine, sequence or image",
    });

    fireEvent.keyDown(within(dialog).getByRole("searchbox", { name: "Search" }), {
      key: "Escape",
    });

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
  });

  it("finds a machine by its MAC with or without separators and goes to it", async () => {
    const { router } = await open();
    await screen.findByRole("heading", { level: 1, name: "Account and security" });

    const dialog = await openWithKeys();
    await waitFor(() => {
      expect(results(dialog)).toContain("LAB-07Virtual Machine, serial 5CG1234XYZ");
    });
    fill(within(dialog).getByRole("searchbox", { name: "Search" }), "00-15-5d-0a-0b-0c");

    await waitFor(() => {
      expect(results(dialog)).toEqual(["LAB-07Virtual Machine, serial 5CG1234XYZ"]);
    });

    press(within(dialog).getByRole("menuitem", { name: /^LAB-07/ }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe("/machines/m2");
    });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(await screen.findByRole("heading", { level: 1, name: "LAB-07" })).toBeInTheDocument();
  });

  it("goes to a page found by its name", async () => {
    const { router } = await open();
    await screen.findByRole("heading", { level: 1, name: "Account and security" });

    const dialog = await openWithKeys();
    fill(within(dialog).getByRole("searchbox", { name: "Search" }), "rules");

    await waitFor(() => {
      expect(results(dialog)).toEqual(["Assignment rulesDeployment"]);
    });
    press(within(dialog).getByRole("menuitem", { name: /^Assignment rules/ }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe("/deployment/rules");
    });
  });

  it("says when nothing matches", async () => {
    await open();
    await screen.findByRole("heading", { level: 1, name: "Account and security" });

    const dialog = await openWithKeys();
    fill(within(dialog).getByRole("searchbox", { name: "Search" }), "zzzz");

    expect(await within(dialog).findByText("Nothing matches.")).toBeInTheDocument();
    expect(results(dialog).filter((text) => text !== "Nothing matches.")).toEqual([]);
  });

  it("has no axe violations while open", async () => {
    await open();
    await screen.findByRole("heading", { level: 1, name: "Account and security" });

    const dialog = await openWithKeys();
    await waitFor(() => {
      expect(results(dialog)).toContain("PC-042Virtual Machine, serial 1234");
    });
    await expectNoAxeViolations();
  });
});
