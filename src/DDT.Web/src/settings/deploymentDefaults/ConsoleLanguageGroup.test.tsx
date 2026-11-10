// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { fireEvent, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { chooseOption, selectKey, selectOptions } from "@/test/aria";
import { bootImageView, linuxBootView } from "@/test/builders";
import { administrator, json, servePage, type Handler, type Sent } from "@/test/serve";

import { DeploymentDefaultsPage, type DeploymentSettings } from "../DeploymentDefaultsPage";
import type { SettingsSectionUpdate, SettingsSectionView } from "../settings";

const label = "Keyboard layout in the Linux boot environment";

function section(layout: string | null = null): SettingsSectionView<DeploymentSettings> {
  return {
    section: "deployment",
    version: 2,
    updatedUtc: null,
    updatedBy: null,
    values: {
      timeZone: null,
      locale: null,
      keyboard: null,
      consoleLanguage: null,
      consoleKeyboardLayout: layout,
      localAdministrator: { name: "Admin" },
      domain: { name: null, organizationalUnit: null, userName: null, controller: null },
    },
    secrets: {},
    locked: [],
    problems: [],
    warnings: [],
    apply: null,
    reauthenticate: [],
  };
}

function serve(handlers: Record<string, Handler> = {}) {
  return servePage({
    user: administrator,
    path: "/deployment/defaults",
    component: () => (
      <main>
        <DeploymentDefaultsPage />
      </main>
    ),
    handlers: {
      "GET /api/settings/deployment": () => json(section()),
      "GET /api/settings/console-logo": () =>
        json({
          sha256: null,
          size: null,
          width: null,
          height: null,
          uploadedUtc: null,
          uploadedBy: null,
        }),
      "GET /api/boot-image": () => json(bootImageView({ linux: linuxBootView() })),
      ...handlers,
    },
  });
}

function layouts(requests: readonly Sent[]): (string | null | undefined)[] {
  return requests
    .filter((request) => request.method === "PUT" && request.path === "/api/settings/deployment")
    .map(
      (request) =>
        (request.body as SettingsSectionUpdate<DeploymentSettings>).values.consoleKeyboardLayout,
    );
}

describe("ConsoleLanguageGroup", () => {
  it("offers the keyboard layouts the server's Linux boot environment has, and saves the one chosen", async () => {
    const { requests } = serve({
      "PUT /api/settings/deployment": (request) =>
        json({
          ...section(
            (request.body as SettingsSectionUpdate<DeploymentSettings>).values
              .consoleKeyboardLayout,
          ),
          version: 3,
          updatedUtc: new Date().toISOString(),
          updatedBy: "Ada Admin",
        }),
    });
    const page = await screen.findByRole("main");
    await screen.findByRole("heading", { name: "The console at the machine" });

    // The environment's own default stands for no choice.
    expect(selectKey(page, label)).toHaveTextContent("English (United States)");
    expect((await selectOptions(page, label)).map((option) => option.text)).toEqual([
      "English (United States)",
      "English (United Kingdom)",
      "German",
      "German (Switzerland)",
      "French",
      "Italian",
      "Spanish",
    ]);

    await chooseOption(page, label, "German (Switzerland)");
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("Saved now by Ada Admin.")).toBeInTheDocument();
    expect(layouts(requests)).toEqual(["ch"]);
    expect(selectKey(page, label)).toHaveTextContent("German (Switzerland)");
  });

  it("goes back to the default by choosing it", async () => {
    const { requests } = serve({
      "GET /api/settings/deployment": () => json(section("de")),
      "PUT /api/settings/deployment": () =>
        json({
          ...section(),
          version: 3,
          updatedUtc: new Date().toISOString(),
          updatedBy: "Ada Admin",
        }),
    });
    const page = await screen.findByRole("main");

    await screen.findByRole("heading", { name: "The console at the machine" });
    expect(selectKey(page, label)).toHaveTextContent("German");

    await chooseOption(page, label, "English (United States)");
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("Saved now by Ada Admin.")).toBeInTheDocument();
    expect(layouts(requests)).toEqual([null]);
  });

  it("keeps a layout that is set although the server has no keymap for it, or no Linux boot environment", async () => {
    serve({
      "GET /api/settings/deployment": () => json(section("dvorak")),
      "GET /api/boot-image": () => json(bootImageView()),
    });
    const page = await screen.findByRole("main");

    await screen.findByRole("heading", { name: "The console at the machine" });

    expect(selectKey(page, label)).toHaveTextContent("dvorak");
    expect((await selectOptions(page, label)).map((option) => option.text)).toEqual([
      "English (United States)",
      "dvorak",
    ]);
  });
});
