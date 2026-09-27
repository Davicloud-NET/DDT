// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { fireEvent, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { administrator, json, operator, servePage, type Handler } from "@/test/serve";

import type { ConsoleLogoView } from "./consoleLogo";
import { expectAccessible } from "./serverTesting";
import { DeploymentDefaultsPage, type DeploymentSettings } from "./DeploymentDefaultsPage";
import type { SettingsSectionView } from "./settings";

const sha256 = "5d41402abc4b2a76b9719d911017c5925d41402abc4b2a76b9719d911017c592";

function section(): SettingsSectionView<DeploymentSettings> {
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

function logo(overrides: Partial<ConsoleLogoView> = {}): ConsoleLogoView {
  return {
    sha256: null,
    size: null,
    width: null,
    height: null,
    uploadedUtc: null,
    uploadedBy: null,
    ...overrides,
  };
}

const uploaded = logo({
  sha256,
  size: 6_144,
  width: 240,
  height: 64,
  uploadedUtc: new Date().toISOString(),
  uploadedBy: "admin",
});

function serve(handlers: Record<string, Handler>, user = administrator) {
  return servePage({
    user,
    path: "/deployment/defaults",
    component: DeploymentDefaultsPage,
    handlers: {
      "GET /api/settings/deployment": () => json(section()),
      "GET /api/settings/console-logo": () => json(logo()),
      ...handlers,
    },
  });
}

async function choose(file: File): Promise<void> {
  // The drop zone comes once the logo is read.
  await screen.findByRole("button", { name: "Choose a file" });

  const input = (await screen.findByRole("heading", { name: "Logo on the console" }))
    .closest("section")
    ?.querySelector('input[type="file"]');

  if (!input) {
    throw new Error("The logo panel has no file input.");
  }

  fireEvent.change(input, { target: { files: [file] } });
}

function png(name = "contoso.png"): File {
  return new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], name, { type: "image/png" });
}

describe("ConsoleLogoPanel", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("uploads a PNG as soon as it is chosen and shows it on the console's header", async () => {
    const { requests } = serve({ "PUT /api/settings/console-logo": () => json(uploaded) });

    expect(await screen.findByText("No logo")).toBeInTheDocument();

    await choose(png());

    expect(
      await screen.findByText("Uploaded. Machines show the logo when they next register."),
    ).toBeInTheDocument();
    expect(screen.getByText("PNG, 240 by 64 pixels")).toBeInTheDocument();
    expect(screen.getByText("now by admin")).toBeInTheDocument();
    expect(
      within(screen.getByRole("img", { name: "The console's header with the logo" })).getByRole(
        "presentation",
        { hidden: true },
      ),
    ).toHaveAttribute("src", `/api/settings/console-logo/image?v=${sha256}`);
    expect(
      requests
        .filter((request) => request.method === "PUT")
        .map((request) => [request.path, request.headers["content-type"]]),
    ).toEqual([["/api/settings/console-logo", "image/png"]]);
  });

  it("refuses what is not a PNG without sending it", async () => {
    const { requests } = serve({});

    await choose(new File(["GIF89a"], "logo.gif", { type: "image/gif" }));

    expect(
      await screen.findByText("logo.gif is not a PNG image. Upload the logo as a PNG file."),
    ).toBeInTheDocument();
    expect(requests.filter((request) => request.method === "PUT")).toEqual([]);
  });

  it("removes the logo once that is confirmed", async () => {
    const { requests } = serve({
      "GET /api/settings/console-logo": () => json(uploaded),
      "DELETE /api/settings/console-logo": () => json(logo()),
    });

    fireEvent.click(await screen.findByRole("button", { name: "Remove the logo" }));
    fireEvent.click(
      within(await screen.findByRole("dialog", { name: "Remove the logo?" })).getByRole("button", {
        name: "Remove logo",
      }),
    );

    expect(
      await screen.findByText("Removed. Machines show no logo when they next register."),
    ).toBeInTheDocument();
    expect(screen.getByText("No logo")).toBeInTheDocument();
    expect(requests.filter((request) => request.method === "DELETE")).toHaveLength(1);
  });

  it("shows an operator the logo, with nothing to change", async () => {
    serve({ "GET /api/settings/console-logo": () => json(uploaded) }, operator);

    expect(await screen.findByText("PNG, 240 by 64 pixels")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Remove the logo" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Choose a file" })).not.toBeInTheDocument();

    await expectAccessible();
  });
});
