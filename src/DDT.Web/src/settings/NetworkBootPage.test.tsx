// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { fill, press, selectKey } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import {
  administrator,
  json,
  operator,
  reads,
  servePage,
  type Handler,
  type Sent,
} from "@/test/serve";

import type { BootTargetSettings, PxeHostInterfaces, PxeSettings } from "./networkBoot";
import { NetworkBootPage } from "./NetworkBootPage";
import {
  putSection,
  type SettingsOverview,
  type SettingsSectionUpdate,
  type SettingsSectionView,
} from "./settings";

const hoursAgo = (hours: number) => new Date(Date.now() - hours * 3_600_000).toISOString();

const tftpTarget: BootTargetSettings = {
  method: "Tftp",
  bootFile: "x64/bootmgfw.efi",
  serverAddress: null,
  serverHostName: null,
  advertiseBootServerDiscovery: false,
};

function values(overrides: Partial<PxeSettings> = {}): PxeSettings {
  return {
    interfaces: [],
    enableProxyDhcp: true,
    enableTftp: true,
    tftpSinglePort: false,
    tftpMaxWindowSize: 16,
    maxConcurrentTftpTransfers: 128,
    authorisedRelayAgents: [],
    bootTargets: { X64Uefi: tftpTarget },
    ...overrides,
  };
}

function section(
  overrides: Partial<SettingsSectionView<PxeSettings>> = {},
): SettingsSectionView<PxeSettings> {
  return {
    section: "pxe",
    version: 7,
    updatedUtc: hoursAgo(2),
    updatedBy: "admin",
    values: values(),
    secrets: {},
    locked: [],
    problems: [],
    warnings: [],
    apply: [
      { host: "ddt-01", version: 7, state: "Applied", message: null, updatedUtc: hoursAgo(2) },
    ],
    reauthenticate: [],
    ...overrides,
  };
}

const hosts: PxeHostInterfaces[] = [
  {
    host: "ddt-01",
    updatedUtc: hoursAgo(1),
    interfaces: [
      { name: "Ethernet", addresses: ["192.168.1.10"], served: false },
      { name: "vEthernet (Default Switch)", addresses: ["172.20.16.1"], served: false },
    ],
    unmatched: [],
  },
  {
    host: "ddt-02",
    updatedUtc: hoursAgo(1),
    interfaces: [{ name: "eth0", addresses: ["10.20.0.5"], served: true }],
    unmatched: [],
  },
];

const overview: SettingsOverview = {
  sections: [],
  server: [
    { key: "DDT:Pxe:HttpBootPort", value: "8080", isSet: false, source: null, secret: false },
    { key: "DDT:Pxe:BootDirectory", value: "boot", isSet: false, source: null, secret: false },
  ],
  keyRingReadable: true,
};

function serve(handlers: Record<string, Handler> = {}, user = administrator) {
  return servePage({
    user,
    path: "/boot/network",
    // Inside the main landmark, like the shell renders it.
    component: () => (
      <main>
        <NetworkBootPage />
      </main>
    ),
    handlers: {
      "GET /api/settings/pxe": () => json(section()),
      "GET /api/settings/pxe/interfaces": () => json(hosts),
      "GET /api/settings": () => json(overview),
      ...handlers,
    },
  });
}

// Answers a save with the section as sent, saved as version 8.
function saved(request: Sent): Response {
  return json(
    section({
      version: 8,
      updatedUtc: new Date().toISOString(),
      updatedBy: "Ada Admin",
      values: (request.body as SettingsSectionUpdate<PxeSettings>).values,
      apply: [{ host: "ddt-01", version: 8, state: "Applied", message: null, updatedUtc: null }],
    }),
  );
}

function puts(requests: readonly Sent[]): SettingsSectionUpdate<PxeSettings>[] {
  return requests
    .filter((request) => request.method === "PUT" && request.path === "/api/settings/pxe")
    .map((request) => request.body as SettingsSectionUpdate<PxeSettings>);
}

function target(architecture: string): HTMLElement {
  return screen.getByRole("group", { name: architecture });
}

function save(): void {
  fireEvent.click(screen.getByRole("button", { name: "Save" }));
}

describe("NetworkBootPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("tells anyone but an administrator that only administrators change it, and reads nothing", async () => {
    const { requests } = serve({}, operator);

    expect(
      await screen.findByText(/Only administrators see and change network boot/),
    ).toBeInTheDocument();
    expect(requests.filter((request) => request.path.startsWith("/api/settings"))).toEqual([]);
  });

  it("says that nothing is served without an interface, and saves the one picked from a host", async () => {
    const { requests } = serve({ "PUT /api/settings/pxe": saved });

    expect(await screen.findByText(/No interface is listed/)).toBeInTheDocument();
    const first = await screen.findByRole("list", { name: "Interfaces on ddt-01" });
    expect(
      within(screen.getByRole("list", { name: "Interfaces on ddt-02" })).getByText("Serving"),
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/Saving restarts the network boot listeners/),
    ).not.toBeInTheDocument();

    press(within(first).getByRole("checkbox", { name: "Ethernet" }));

    expect(within(first).getByRole("checkbox", { name: "Ethernet" })).toBeChecked();
    expect(screen.queryByText(/No interface is listed/)).not.toBeInTheDocument();
    expect(
      screen.getByText(
        "Saving restarts the network boot listeners on every host that runs them, which ends the TFTP transfers in progress. Those machines start their download again.",
      ),
    ).toBeInTheDocument();

    save();

    expect(await screen.findByText("Saved now by Ada Admin.")).toBeInTheDocument();
    expect(puts(requests)).toEqual([
      { version: 7, values: values({ interfaces: ["Ethernet"] }), secrets: {}, confirm: [] },
    ]);
    expect(reads(requests, "/api/settings/pxe")).toBe(1);
    expect(reads(requests, "/api/settings/pxe/interfaces")).toBe(1);
    expect(
      screen.queryByText(/Saving restarts the network boot listeners/),
    ).not.toBeInTheDocument();
  });

  it("keeps typed entries beside the reported interfaces, and shows the hosts' warnings", async () => {
    const missing = "'eth9' names no interface on ddt-02, which serves nothing for it.";
    const { requests } = serve({
      "GET /api/settings/pxe": () =>
        json(
          section({
            values: values({ interfaces: ["Ethernet", "eth9"] }),
            warnings: [{ field: "interfaces", message: missing, code: "pxe.interfaceNotFound" }],
          }),
        ),
      "PUT /api/settings/pxe": saved,
    });

    expect(await screen.findByText(missing)).toBeInTheDocument();
    const others = screen.getByRole("list", { name: "Other entries" });
    expect(within(others).getByText("eth9")).toBeInTheDocument();
    expect(
      within(await screen.findByRole("list", { name: "Interfaces on ddt-01" })).getByRole(
        "checkbox",
        { name: "Ethernet" },
      ),
    ).toBeChecked();

    fireEvent.click(within(others).getByRole("button", { name: "Remove eth9" }));
    fill(
      screen.getByRole("textbox", { name: "Another interface name or address" }),
      "10.20.0.5, ethernet ",
    );
    fireEvent.click(screen.getByRole("button", { name: "Add" }));

    // 10.20.0.5 is eth0's address on ddt-02, so eth0 counts as listed. "ethernet" is already listed as Ethernet, so it
    // isn't added again.
    expect(
      within(screen.getByRole("list", { name: "Interfaces on ddt-02" })).getByRole("checkbox", {
        name: "eth0",
      }),
    ).toBeChecked();
    expect(screen.getByRole("textbox", { name: "Another interface name or address" })).toHaveValue(
      "",
    );

    save();

    await screen.findByText("Saved now by Ada Admin.");
    expect(puts(requests).map((update) => update.values.interfaces)).toEqual([
      ["Ethernet", "10.20.0.5"],
    ]);
  });

  it("adds a boot target for an architecture not used yet, removes one, and saves both", async () => {
    const { requests } = serve({ "PUT /api/settings/pxe": saved });

    await screen.findByText("8080");
    const x64 = target("X64Uefi");
    expect(within(x64).getByRole("combobox", { name: "Boot file" })).toHaveValue(
      "x64/bootmgfw.efi",
    );
    expect(
      within(x64).getByText("Its firmware loads the boot file over TFTP."),
    ).toBeInTheDocument();

    press(selectKey(document.body, "Architecture"));
    const offered = await screen.findByRole("listbox");
    expect(within(offered).queryByRole("option", { name: /^X64Uefi(?!Http)/ })).toBeNull();
    press(within(offered).getByRole("option", { name: /^X64UefiHttp/ }));
    await waitFor(() => {
      expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    });
    fireEvent.click(screen.getByRole("button", { name: "Add boot target" }));

    const http = target("X64UefiHttp");
    expect(
      within(http).getByText("Its firmware loads the boot file over HTTP, from a URL."),
    ).toBeInTheDocument();
    expect(within(http).getByRole("combobox", { name: "Boot file" })).toHaveValue(
      "http://localhost:8080/boot/x64/bootmgfw.efi",
    );
    fill(within(http).getByRole("textbox", { name: "Server address" }), "192.168.1.10");

    fireEvent.click(
      within(x64).getByRole("button", { name: "Remove the boot target for X64Uefi" }),
    );
    expect(screen.queryByRole("group", { name: "X64Uefi" })).not.toBeInTheDocument();

    save();

    await screen.findByText("Saved now by Ada Admin.");
    expect(puts(requests).map((update) => update.values.bootTargets)).toEqual([
      {
        X64UefiHttp: {
          method: "Http",
          bootFile: "http://localhost:8080/boot/x64/bootmgfw.efi",
          serverAddress: "192.168.1.10",
          serverHostName: null,
          advertiseBootServerDiscovery: false,
        },
      },
    ]);
  });

  it("offers the 2023 boot manager, and takes a path typed instead", async () => {
    const { requests } = serve({ "PUT /api/settings/pxe": saved });

    const file = within(await screen.findByRole("group", { name: "X64Uefi" })).getByRole(
      "combobox",
      { name: "Boot file" },
    );
    press(within(target("X64Uefi")).getByRole("button", { name: /Boot file/ }));
    const listbox = await screen.findByRole("listbox");
    expect(
      within(listbox)
        .getAllByRole("option")
        .map((option) => option.textContent),
    ).toEqual([
      "x64/bootmgfw.efiMicrosoft 2011 CA. The default, which most machines trust.",
      "x64/bootmgfw_ex.efiWindows UEFI CA 2023.",
    ]);
    press(within(listbox).getByRole("option", { name: /^x64\/bootmgfw_ex\.efi/ }));
    await waitFor(() => {
      expect(file).toHaveValue("x64/bootmgfw_ex.efi");
    });

    fill(file, "x64/custom/bootmgfw.efi");
    save();

    await screen.findByText("Saved now by Ada Admin.");
    expect(puts(requests).map((update) => update.values.bootTargets.X64Uefi?.bootFile)).toEqual([
      "x64/custom/bootmgfw.efi",
    ]);
  });

  it("marks the boot target field the server refused, and saves nothing", async () => {
    serve({
      "GET /api/settings/pxe": () =>
        json(
          section({
            values: values({
              bootTargets: {
                X64Uefi: tftpTarget,
                Arm64Uefi: { ...tftpTarget, bootFile: "arm64/bootmgfw.efi" },
              },
            }),
          }),
        ),
      "PUT /api/settings/pxe": () =>
        json(
          {
            title: "One or more validation errors occurred.",
            status: 400,
            errors: { "bootTargets[X64Uefi].serverAddress": ["'10.0.0' is not an IPv4 address."] },
          },
          400,
        ),
    });

    const address = within(await screen.findByRole("group", { name: "X64Uefi" })).getByRole(
      "textbox",
      { name: "Server address" },
    );
    fill(address, "10.0.0");
    save();

    expect(
      await screen.findByText("Nothing was saved. The fields marked above say why."),
    ).toBeInTheDocument();
    expect(
      within(target("X64Uefi")).getByText("'10.0.0' is not an IPv4 address."),
    ).toBeInTheDocument();
    expect(address).toHaveAttribute("aria-invalid", "true");
    expect(
      within(target("Arm64Uefi")).getByRole("textbox", { name: "Server address" }),
    ).not.toHaveAttribute("aria-invalid");
  });

  it("shows how each host applied a change saved elsewhere, as the hub delivers it", async () => {
    const { queryClient } = serve();

    await screen.findByText("Saved 2 hours ago by admin.");
    act(() => {
      putSection(
        queryClient,
        section({
          version: 8,
          updatedBy: "b.bauer",
          updatedUtc: new Date().toISOString(),
          values: values({ interfaces: ["eth0"] }),
          apply: [
            { host: "ddt-01", version: 8, state: "Applied", message: null, updatedUtc: null },
            {
              host: "ddt-02",
              version: 8,
              state: "Failed",
              message:
                "Could not bind UDP 67: another service holds it. The previous listeners run again.",
              updatedUtc: null,
            },
          ],
        }),
      );
    });

    expect(await screen.findByText("Saved now by b.bauer.")).toBeInTheDocument();
    const applied = screen.getByRole("list", { name: "Where it applies" });
    expect(within(applied).getByText("Failed")).toBeInTheDocument();
    expect(
      within(applied).getByText(
        "Could not bind UDP 67: another service holds it. The previous listeners run again.",
      ),
    ).toBeInTheDocument();
    expect(
      within(screen.getByRole("list", { name: "Interfaces on ddt-02" })).getByRole("checkbox", {
        name: "eth0",
      }),
    ).toBeChecked();
  });

  it("shows the stored problems on their fields and says nothing is served meanwhile", async () => {
    serve({
      "GET /api/settings/pxe": () =>
        json(
          section({
            values: values({ bootTargets: { X64Uefi: { ...tftpTarget, bootFile: null } } }),
            problems: [
              { field: "bootTargets[X64Uefi].bootFile", message: "Must be set.", code: null },
            ],
            apply: [
              {
                host: "ddt-01",
                version: 7,
                state: "Failed",
                message:
                  "The pxe settings have problems, so nothing is served until they are fixed: DDT:Pxe:BootTargets:X64Uefi:BootFile: Must be set.",
                updatedUtc: hoursAgo(1),
              },
            ],
          }),
        ),
    });

    expect(
      await screen.findByText(/These settings have problems, so no host serves network boot/),
    ).toBeInTheDocument();
    expect(within(target("X64Uefi")).getByText("Must be set.")).toBeInTheDocument();
    expect(within(target("X64Uefi")).getByRole("combobox", { name: "Boot file" })).toHaveAttribute(
      "aria-invalid",
      "true",
    );

    const applied = screen.getByRole("list", { name: "Where it applies" });
    expect(within(applied).getByText("Failed")).toBeInTheDocument();
    expect(within(applied).getByText(/nothing is served until they are fixed/)).toBeInTheDocument();
  });

  it("scans the interfaces again after asking, and shows the hosts applying it without reading again", async () => {
    const { requests } = serve({
      "POST /api/settings/pxe/rescan": () =>
        json(
          section({
            version: 8,
            updatedUtc: new Date().toISOString(),
            updatedBy: "Ada Admin",
            apply: [
              { host: "ddt-01", version: 8, state: "Applied", message: null, updatedUtc: null },
              { host: "ddt-02", version: 7, state: "Pending", message: null, updatedUtc: null },
            ],
          }),
        ),
    });

    fireEvent.click(await screen.findByRole("button", { name: "Scan interfaces again" }));
    const dialog = await screen.findByRole("dialog", { name: "Scan the interfaces again?" });
    expect(within(dialog).getByText(/TFTP transfers in progress end/)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Scan again" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    const applied = screen.getByRole("list", { name: "Where it applies" });
    expect(within(applied).getByText("ddt-02")).toBeInTheDocument();
    expect(within(applied).getByText("Pending")).toBeInTheDocument();
    expect(screen.getByText("Saved now by Ada Admin.")).toBeInTheDocument();
    expect(requests.filter((request) => request.method === "POST").map((r) => r.path)).toEqual([
      "/api/settings/pxe/rescan",
    ]);
    expect(reads(requests, "/api/settings/pxe")).toBe(1);
    expect(reads(requests, "/api/settings/pxe/interfaces")).toBe(1);
  });

  it("does not scan again while changes are unsaved", async () => {
    serve();

    press(
      within(await screen.findByRole("list", { name: "Interfaces on ddt-01" })).getByRole(
        "checkbox",
        { name: "Ethernet" },
      ),
    );

    expect(screen.getByRole("button", { name: "Scan interfaces again" })).toBeDisabled();
    expect(screen.getByText("Save or discard your changes first.")).toBeInTheDocument();
  });

  it("shows boot targets that configuration sets as read-only, with where to change them", async () => {
    serve({
      "GET /api/settings/pxe": () =>
        json(
          section({
            locked: [
              {
                field: "bootTargets",
                configurationKey: "DDT:Pxe:BootTargets",
                environmentVariable: "DDT__Pxe__BootTargets",
                source: "Environment variables",
                storedDiffers: false,
              },
            ],
          }),
        ),
    });

    expect(
      await screen.findByText(
        "Set in configuration as DDT:Pxe:BootTargets (DDT__Pxe__BootTargets). Remove it there to change it on this page.",
      ),
    ).toBeInTheDocument();
    expect(
      within(target("X64Uefi")).getByRole("textbox", { name: "Server address" }),
    ).toHaveAttribute("readonly");
    expect(
      screen.queryByRole("button", { name: /Remove the boot target/ }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add boot target" })).not.toBeInTheDocument();
    expect(screen.getAllByText(/Set in configuration as/)).toHaveLength(1);
  });

  describe("accessibility", () => {
    it("has no violations with interfaces, boot targets and apply states", async () => {
      serve({
        "GET /api/settings/pxe": () =>
          json(
            section({
              values: values({
                interfaces: ["Ethernet", "eth9"],
                bootTargets: {
                  X64Uefi: tftpTarget,
                  X64UefiHttp: {
                    ...tftpTarget,
                    method: "Http",
                    bootFile: "http://ddt.example:8080/boot/x64/bootmgfw_ex.efi",
                  },
                },
              }),
              apply: [
                { host: "ddt-01", version: 7, state: "Applied", message: null, updatedUtc: null },
                {
                  host: "ddt-02",
                  version: 7,
                  state: "Failed",
                  message: "UDP 67 is taken by another service.",
                  updatedUtc: null,
                },
              ],
            }),
          ),
      });

      await screen.findByRole("list", { name: "Interfaces on ddt-02" });
      await screen.findByText("8080");
      await expectNoAxeViolations();
    });

    it("has no violations with the rescan dialog open", async () => {
      serve();

      fireEvent.click(await screen.findByRole("button", { name: "Scan interfaces again" }));
      await screen.findByRole("dialog");
      await expectNoAxeViolations();
    });
  });
});
