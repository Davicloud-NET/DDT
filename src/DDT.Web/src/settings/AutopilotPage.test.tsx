// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { administrator, json, operator, servePage } from "@/test/serve";

import { AutopilotPage } from "./AutopilotPage";
import type { AutopilotSettings } from "./autopilot/AutopilotSettingsForm";
import type { SettingsSectionView } from "./settings";

function section(): SettingsSectionView<AutopilotSettings> {
  return {
    section: "autopilot",
    version: 2,
    updatedUtc: null,
    updatedBy: null,
    values: {
      tenantId: "contoso.onmicrosoft.com",
      clientId: "00001111-aaaa-2222-bbbb-3333cccc4444",
      defaultGroupTag: "",
    },
    secrets: { clientSecret: { isSet: true, unreadable: false, updatedUtc: null } },
    locked: [],
    problems: [],
    warnings: [],
    apply: null,
    reauthenticate: [],
  };
}

function serve(user = administrator) {
  return servePage({
    user,
    path: "/machines/autopilot",
    component: AutopilotPage,
    handlers: { "GET /api/settings/autopilot": () => json(section()) },
  });
}

describe("AutopilotPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows an administrator the app registration, and only that a secret is set", async () => {
    serve();

    expect(await screen.findByRole("textbox", { name: "Tenant" })).toHaveValue(
      "contoso.onmicrosoft.com",
    );
    expect(screen.getByRole("textbox", { name: "Client ID" })).toHaveValue(
      "00001111-aaaa-2222-bbbb-3333cccc4444",
    );
    expect(screen.getByText("Client secret")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Default group tag" })).toHaveValue("");
  });

  it("tells anyone else that only administrators set it up, and asks the server nothing", async () => {
    const { requests } = serve(operator);

    expect(
      await screen.findByText("Only administrators set up Autopilot uploads."),
    ).toBeInTheDocument();
    expect(requests.filter((request) => request.path === "/api/settings/autopilot")).toEqual([]);
  });
});
