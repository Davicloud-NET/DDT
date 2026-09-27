// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { administrator, json, operator, servePage, type Handler, type Sent } from "@/test/serve";

import { ApprovalPage, type MachineSettings } from "./ApprovalPage";
import { putSection, type SettingsSectionUpdate, type SettingsSectionView } from "./settings";

function section(
  overrides: Partial<SettingsSectionView<MachineSettings>> = {},
): SettingsSectionView<MachineSettings> {
  return {
    section: "machines",
    version: 3,
    updatedUtc: new Date(Date.now() - 2 * 3_600_000).toISOString(),
    updatedBy: "admin",
    values: {
      requireWebApproval: false,
      maxWaitingPerAddress: 10,
      maxWaiting: 200,
      zeroTouchNetworks: [],
    },
    secrets: {},
    locked: [],
    problems: [],
    warnings: [],
    apply: null,
    reauthenticate: ["zeroTouchNetworks", "requireWebApproval"],
    ...overrides,
  };
}

function serve(handlers: Record<string, Handler>, user = administrator) {
  return servePage({
    user,
    path: "/machines/approval",
    component: ApprovalPage,
    handlers: { "GET /api/settings/machines": () => json(section()), ...handlers },
  });
}

function puts(requests: readonly Sent[]) {
  return requests
    .filter((request) => request.method === "PUT" && request.path === "/api/settings/machines")
    .map((request) => ({
      update: request.body as SettingsSectionUpdate<MachineSettings>,
      headers: request.headers,
    }));
}

function networks(): HTMLElement {
  return screen.getByRole("textbox", { name: "Zero touch networks" });
}

describe("ApprovalPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows an operator the settings to read, with nothing to change or save", async () => {
    serve({}, operator);

    expect(
      await screen.findByRole("switch", { name: /Also approve every machine/ }),
    ).toHaveAttribute("aria-readonly", "true");
    expect(networks()).toHaveAttribute("readonly");
    expect(screen.queryByRole("button", { name: "Save" })).not.toBeInTheDocument();
    expect(screen.getByText("Saved 2 hours ago by admin.")).toBeInTheDocument();
  });

  it("saves a change against the version it started from, and shows the answer without reading it again", async () => {
    let reads = 0;
    const { requests } = serve({
      "GET /api/settings/machines": () => {
        reads += 1;
        return json(section());
      },
      "PUT /api/settings/machines": (request) =>
        json(
          section({
            version: 4,
            updatedUtc: new Date().toISOString(),
            updatedBy: "Ada Admin",
            values: (request.body as SettingsSectionUpdate<MachineSettings>).values,
          }),
        ),
    });

    const perAddress = await screen.findByRole("textbox", { name: "Waiting from one address" });
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();

    fireEvent.change(perAddress, { target: { value: "4" } });
    fireEvent.blur(perAddress);
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("Saved now by Ada Admin.")).toBeInTheDocument();
    expect(puts(requests).map((sent) => sent.update)).toEqual([
      {
        version: 3,
        values: {
          requireWebApproval: false,
          maxWaitingPerAddress: 4,
          maxWaiting: 200,
          zeroTouchNetworks: [],
        },
        secrets: {},
        confirm: [],
      },
    ]);
    expect(reads).toBe(1);
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  });

  it("asks to confirm a warning, then for the password, and sends the confirmation with the proof", async () => {
    const wide = {
      field: "zeroTouchNetworks",
      message: "10.0.0.0/8 holds more than 65,536 addresses.",
      code: "machines.wideNetwork",
    };
    const { requests } = serve({
      "POST /api/settings/reauthenticate": () =>
        json({ token: "proof", expiresUtc: new Date(Date.now() + 300_000).toISOString() }),
      "PUT /api/settings/machines": (request) => {
        const update = request.body as SettingsSectionUpdate<MachineSettings>;

        if (!update.confirm.includes(wide.code)) {
          return json(
            {
              title: "One or more validation errors occurred.",
              status: 400,
              errors: { confirm: [`${wide.code}: ${wide.message}`] },
              confirm: [wide],
            },
            400,
          );
        }

        if (request.headers["x-ddt-reauthentication"] !== "proof") {
          return json(
            {
              title: "Enter your password again to change zeroTouchNetworks.",
              status: 403,
              fields: ["zeroTouchNetworks"],
            },
            403,
          );
        }

        return json(section({ version: 4, values: update.values }));
      },
    });

    await screen.findByText("Saved 2 hours ago by admin.");
    fireEvent.change(networks(), { target: { value: "10.0.0.0/8\n\n 10.20.0.0/24 " } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    const warning = await screen.findByRole("dialog", { name: "Save anyway?" });
    expect(within(warning).getByText(wide.message)).toBeInTheDocument();
    fireEvent.click(within(warning).getByRole("button", { name: "Save anyway" }));

    const proof = await screen.findByRole("dialog", { name: "Confirm it is you" });
    fireEvent.change(within(proof).getByLabelText("Password"), { target: { value: "secret" } });
    fireEvent.click(within(proof).getByRole("button", { name: "Confirm and save" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(
      requests.find((request) => request.path === "/api/settings/reauthenticate")?.body,
    ).toEqual({ password: "secret", code: null });

    const sent = puts(requests);
    expect(sent.map((one) => one.update.confirm)).toEqual([[], [wide.code], [wide.code]]);
    expect(sent.map((one) => one.headers["x-ddt-reauthentication"] ?? null)).toEqual([
      null,
      null,
      "proof",
    ]);
    expect(sent[2]?.update.values.zeroTouchNetworks).toEqual(["10.0.0.0/8", "10.20.0.0/24"]);
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  });

  it("marks the fields the server refused, and saves nothing", async () => {
    serve({
      "PUT /api/settings/machines": () =>
        json(
          {
            title: "One or more validation errors occurred.",
            status: 400,
            errors: { zeroTouchNetworks: ["10.20.0.300/24 is not a network."] },
          },
          400,
        ),
    });

    await screen.findByText("Saved 2 hours ago by admin.");
    fireEvent.change(networks(), { target: { value: "10.20.0.300/24" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(
      await screen.findByText("Nothing was saved. The fields marked below say why."),
    ).toBeInTheDocument();
    expect(screen.getByText("10.20.0.300/24 is not a network.")).toBeInTheDocument();
    expect(networks()).toHaveAttribute("aria-invalid", "true");
  });

  it("takes a change saved elsewhere while nothing is typed, and announces it while something is", async () => {
    const { queryClient } = serve({});

    await screen.findByText("Saved 2 hours ago by admin.");

    act(() => {
      putSection(
        queryClient,
        section({
          version: 4,
          updatedBy: "b.bauer",
          updatedUtc: new Date().toISOString(),
          values: { ...section().values, maxWaiting: 50 },
        }),
      );
    });
    expect(await screen.findByText("Saved now by b.bauer.")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Waiting in all" })).toHaveValue("50");

    fireEvent.change(networks(), { target: { value: "10.20.0.0/24" } });
    act(() => {
      putSection(
        queryClient,
        section({ version: 5, values: { ...section().values, maxWaiting: 60 } }),
      );
    });

    expect(
      await screen.findByText("Someone else saved these settings while you were changing them."),
    ).toBeInTheDocument();
    expect(networks()).toHaveValue("10.20.0.0/24");

    fireEvent.click(screen.getByRole("button", { name: "Show theirs" }));
    await waitFor(() => {
      expect(networks()).toHaveValue("");
    });
    expect(screen.getByRole("textbox", { name: "Waiting in all" })).toHaveValue("60");
  });

  it("shows a field that configuration sets as read-only, with where to change it", async () => {
    serve({
      "GET /api/settings/machines": () =>
        json(
          section({
            locked: [
              {
                field: "zeroTouchNetworks",
                configurationKey: "DDT:Machines:ZeroTouchNetworks",
                environmentVariable: "DDT__Machines__ZeroTouchNetworks",
                source: "appsettings.json",
                storedDiffers: false,
              },
            ],
          }),
        ),
    });

    expect(
      await screen.findByText(
        "Set in configuration as DDT:Machines:ZeroTouchNetworks (DDT__Machines__ZeroTouchNetworks). Remove it there to change it on this page.",
      ),
    ).toBeInTheDocument();
    expect(networks()).toHaveAttribute("readonly");
  });
});
