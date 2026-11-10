// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { fireEvent, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { chooseOption, fill, press, selectKey } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import { bootImageView, linuxBootView } from "@/test/builders";
import { rowOf } from "@/test/rowOf";
import { administrator, json, servePage, type Handler, type Sent } from "@/test/serve";

import type { PxeSettings } from "../networkBoot";
import { NetworkBootPage } from "../NetworkBootPage";
import type { SettingsSectionUpdate, SettingsSectionView } from "../settings";

function values(overrides: Partial<PxeSettings> = {}): PxeSettings {
  return {
    interfaces: ["Ethernet"],
    enableProxyDhcp: true,
    enableTftp: true,
    tftpSinglePort: false,
    tftpMaxWindowSize: 16,
    maxConcurrentTftpTransfers: 128,
    authorisedRelayAgents: [],
    bootTargets: {},
    leaveDhcpPort: false,
    defaultBootEnvironment: "Auto",
    linuxKernelArguments: "",
    ...overrides,
  };
}

function section(
  overrides: Partial<SettingsSectionView<PxeSettings>> = {},
): SettingsSectionView<PxeSettings> {
  return {
    section: "pxe",
    version: 7,
    updatedUtc: null,
    updatedBy: "admin",
    values: values(),
    secrets: {},
    locked: [],
    problems: [],
    warnings: [],
    apply: [],
    reauthenticate: [],
    ...overrides,
  };
}

function serve(handlers: Record<string, Handler> = {}) {
  return servePage({
    user: administrator,
    path: "/boot/network",
    component: () => (
      <main>
        <NetworkBootPage />
      </main>
    ),
    handlers: {
      "GET /api/settings/pxe": () => json(section()),
      "GET /api/settings/pxe/interfaces": () => json([]),
      "GET /api/settings": () => json({ sections: [], server: [], keyRingReadable: true }),
      "GET /api/boot-image": () => json(bootImageView({ linux: linuxBootView() })),
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
    }),
  );
}

function puts(requests: readonly Sent[]): PxeSettings[] {
  return requests
    .filter((request) => request.method === "PUT" && request.path === "/api/settings/pxe")
    .map((request) => (request.body as SettingsSectionUpdate<PxeSettings>).values);
}

async function group(): Promise<HTMLElement> {
  return rowOf(await screen.findByRole("heading", { name: "Windows PE or Linux" }), "section, div");
}

describe("BootEnvironmentGroup", () => {
  it("says which file Linux machines load, and what a DHCP server that names a boot file does", async () => {
    serve();

    const page = await screen.findByRole("main");
    await group();

    expect(page).toHaveTextContent(
      "Machines that start the Linux boot environment load linux/shimx64.efi over TFTP.",
    );
    expect(page).toHaveTextContent(
      "A DHCP server that names a boot file itself starts the same environment on every machine.",
    );
    expect(selectKey(page, "Machines without a run start")).toHaveTextContent(
      "Automatic: Windows PE once a boot image is built, Linux until then",
    );
    await expectNoAxeViolations();
  });

  it("saves the default, the kernel's extra words and the DHCP port's switch", async () => {
    const { requests } = serve({ "PUT /api/settings/pxe": saved });
    const page = await screen.findByRole("main");
    await group();

    await chooseOption(page, "Machines without a run start", "The Linux boot environment");
    fill(
      within(page).getByRole("textbox", { name: "Extra kernel arguments for Linux" }),
      "console=ttyS0,115200",
    );
    press(
      within(page).getByRole("switch", { name: "Leave UDP 67 to a DHCP server on this computer" }),
    );
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("Saved now by Ada Admin.")).toBeInTheDocument();
    expect(puts(requests)).toEqual([
      values({
        defaultBootEnvironment: "Linux",
        linuxKernelArguments: "console=ttyS0,115200",
        leaveDhcpPort: true,
      }),
    ]);
  });

  it("offers Windows PE as the default too", async () => {
    const { requests } = serve({
      "GET /api/settings/pxe": () =>
        json(section({ values: values({ defaultBootEnvironment: "Linux" }) })),
      "PUT /api/settings/pxe": saved,
    });
    const page = await screen.findByRole("main");
    await group();

    expect(selectKey(page, "Machines without a run start")).toHaveTextContent(
      "The Linux boot environment",
    );
    await chooseOption(page, "Machines without a run start", "Windows PE");
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("Saved now by Ada Admin.")).toBeInTheDocument();
    expect(puts(requests)).toEqual([values({ defaultBootEnvironment: "WindowsPE" })]);
  });

  it("warns in the words of the server's code when Linux is the default and cannot be started", async () => {
    serve({
      "GET /api/settings/pxe": () =>
        json(
          section({
            values: values({ defaultBootEnvironment: "Linux" }),
            warnings: [
              {
                field: "defaultBootEnvironment",
                message: "English from the server.",
                code: "pxe.linuxDefaultUnavailable",
                text: {
                  code: "settings.pxe.linuxDefaultUnavailable",
                  args: {
                    problem: { code: "linuxBoot.missing", args: { folder: "/app/linux-boot" } },
                  },
                },
              },
            ],
          }),
        ),
    });

    expect(
      await screen.findByText(
        "Linux is the default, but this server cannot start the Linux boot environment, so machines without a run are named the Windows PE boot file. This server has no Linux boot environment: /app/linux-boot holds no ddt-linux-boot.json. A release of DDT brings the folder with it.",
      ),
    ).toBeInTheDocument();
  });

  it("shows what the server refused at the kernel arguments", async () => {
    const refusal = "'quiet;' cannot be added to the kernel command line.";
    serve({
      "PUT /api/settings/pxe": () =>
        json(
          {
            title: "One or more validation errors occurred.",
            status: 400,
            errors: { linuxKernelArguments: [refusal] },
          },
          400,
        ),
    });
    const page = await screen.findByRole("main");
    await group();

    fill(
      within(page).getByRole("textbox", { name: "Extra kernel arguments for Linux" }),
      "quiet; reboot",
    );
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText(refusal)).toBeInTheDocument();
  });

  it("names the boot file itself for a server that does not describe its Linux boot environment", async () => {
    serve({ "GET /api/boot-image": () => json(bootImageView()) });

    const page = await screen.findByRole("main");
    await group();

    expect(page).toHaveTextContent(/load linux\/shimx64\.efi over TFTP/);
  });
});
