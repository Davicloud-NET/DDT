// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { press } from "@/test/aria";
import { administrator, json, servePage, type Handler } from "@/test/serve";

import { proof, typePassword } from "../../serverTesting";

import type { DhcpScope, NetbootNeighbours } from "./neighbours";
import { NeighboursGroup } from "./NeighboursGroup";

function neighbours(overrides: Partial<NetbootNeighbours> = {}): NetbootNeighbours {
  return {
    ports: [
      { port: 67, owners: [{ processId: 900, process: "svchost", service: "DHCP" }] },
      {
        port: 69,
        owners: [
          { processId: 1200, process: "DDT.Host", service: "DDT" },
          { processId: 1500, process: "svchost", service: "WDS" },
        ],
      },
      { port: 4011, owners: [] },
    ],
    dhcp: { installed: true, running: true },
    wds: { installed: true, running: true },
    helper: true,
    bootServer: "deploy01.contoso.local",
    bootFile: "x64/bootmgfw.efi",
    leavesDhcpPort: true,
    dhcpSendsPxe: false,
    ...overrides,
  };
}

const lab: DhcpScope = {
  scopeId: "10.0.100.0",
  name: "Lab",
  active: true,
  bootServer: null,
  bootFile: null,
};

function open(view: NetbootNeighbours, handlers: Record<string, Handler> = {}) {
  return servePage({
    user: administrator,
    path: "/boot/network",
    component: NeighboursGroup,
    handlers: {
      "GET /api/netboot": () => json(view),
      "POST /api/settings/reauthenticate": () => proof("netboot-proof"),
      ...handlers,
    },
  });
}

// The proof of a password lasts a few minutes, so only the first change in this file is asked for it.
async function passwordIfAsked(): Promise<void> {
  if (screen.queryByRole("dialog", { name: "Confirm it is you" }) !== null) {
    await typePassword();
  }
}

describe("NeighboursGroup", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("names who holds each port and sets options 66 and 67 for the chosen scopes after the password", async () => {
    const { requests } = open(neighbours(), {
      "GET /api/netboot/dhcp-scopes": () => json([lab]),
      "POST /api/netboot/dhcp-options": () =>
        json([{ ...lab, bootServer: "deploy01.contoso.local", bootFile: "x64/bootmgfw.efi" }]),
    });

    expect(
      await screen.findByText("Microsoft's DHCP server (svchost, process 900)"),
    ).toBeInTheDocument();
    expect(
      screen.getByText("DDT, Windows Deployment Services (svchost, process 1500)"),
    ).toBeInTheDocument();
    expect(screen.getByText("Nobody")).toBeInTheDocument();
    expect(screen.getByText("deploy01.contoso.local")).toBeInTheDocument();
    // Next to a running WDS, option 60 is WDS's own
    expect(screen.getByText(/DDT answers on port 4011 alone/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /option 60/ })).not.toBeInTheDocument();

    press(screen.getByRole("button", { name: "Set them on this DHCP server" }));
    const dialog = await screen.findByRole("dialog", {
      name: "Options 66 and 67 on this DHCP server",
    });
    const set = within(dialog).getByRole("button", { name: "Set for the chosen scopes" });
    expect(set).toBeDisabled();

    press(
      await within(dialog).findByRole("checkbox", {
        name: /10\.0\.100\.0 Lab.*Sets neither option yet/,
      }),
    );
    press(set);
    await passwordIfAsked();

    expect(
      await screen.findByRole("checkbox", {
        name: /Sends machines to deploy01\.contoso\.local for x64\/bootmgfw\.efi now/,
      }),
    ).not.toBeChecked();

    const sent = requests.filter((request) => request.path === "/api/netboot/dhcp-options");
    expect(sent.map((request) => request.body)).toEqual([{ scopes: ["10.0.100.0"] }]);
    expect(sent[0]?.headers["x-ddt-reauthentication"]).toBe("netboot-proof");
  });

  it("sets option 60 on the DHCP server of this computer after the password, and takes it off again", async () => {
    const alone = { wds: { installed: false, running: false } };
    const { requests } = open(neighbours(alone), {
      "POST /api/netboot/dhcp-pxe": () => json(neighbours({ ...alone, dhcpSendsPxe: true })),
    });

    expect(
      await screen.findByText(/find it there once the DHCP server sends option/),
    ).toBeVisible();
    press(screen.getByRole("button", { name: "Set option 60 on this DHCP server" }));
    await passwordIfAsked();

    expect(await screen.findByRole("button", { name: "Stop sending option 60" })).toBeVisible();
    expect(screen.getByText(/which brings machines that netboot there/)).toBeVisible();

    const sent = requests.filter((request) => request.path === "/api/netboot/dhcp-pxe");
    expect(sent.map((request) => request.body)).toEqual([{ send: true }]);
    expect(sent[0]?.headers["x-ddt-reauthentication"]).toBe("netboot-proof");

    press(screen.getByRole("button", { name: "Stop sending option 60" }));
    await waitFor(() => {
      expect(requests.filter((request) => request.path === "/api/netboot/dhcp-pxe")).toHaveLength(
        2,
      );
    });
    expect(requests.at(-1)?.body).toEqual({ send: false });
  });

  it("puts DDT into the WDS boot menu, or stops WDS after asking and binds the ports again", async () => {
    const stopped = neighbours({ wds: { installed: true, running: false } });
    const { requests } = open(neighbours(), {
      "POST /api/netboot/wds/boot-image": () => json(neighbours()),
      "POST /api/netboot/wds/replace": () => json(stopped),
      "POST /api/settings/pxe/rescan": () => json({ section: "pxe", version: 8, values: {} }),
    });

    press(await screen.findByRole("button", { name: "Add DDT to the WDS boot menu" }));
    await passwordIfAsked();
    expect(await screen.findByText(/The WDS boot menu offers DDT now/)).toBeInTheDocument();

    press(screen.getByRole("button", { name: "Use DDT in place of WDS" }));
    const confirm = await screen.findByRole("dialog", { name: "Use DDT in place of WDS?" });
    expect(confirm).toHaveTextContent("MDT's LiteTouch no longer netboots from this server");
    expect(requests.some((request) => request.path === "/api/netboot/wds/replace")).toBe(false);

    press(within(confirm).getByRole("button", { name: "Stop WDS" }));

    expect(await screen.findByRole("button", { name: "Start WDS again" })).toBeInTheDocument();
    await waitFor(() => {
      expect(requests.map((request) => request.path)).toContain("/api/settings/pxe/rescan");
    });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("gives the two options and nothing to press on a server that is not Windows", async () => {
    open(
      neighbours({
        ports: null,
        dhcp: { installed: false, running: false },
        wds: { installed: false, running: false },
        helper: false,
        leavesDhcpPort: false,
        dhcpSendsPxe: null,
      }),
    );

    expect(await screen.findByText("x64/bootmgfw.efi")).toBeInTheDocument();
    expect(screen.getByText(/Where ProxyDHCP does not reach the machines/)).toBeInTheDocument();
    expect(screen.getAllByRole("button").map((button) => button.textContent)).toEqual([
      "Copy the PowerShell line for a Microsoft DHCP server",
    ]);
  });
});
