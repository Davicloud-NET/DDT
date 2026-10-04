// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { fill, nth, press, selectKey } from "@/test/aria";
import { json, operator, reads } from "@/test/serve";

import type { LoggingSettings } from "./logging/logLevels";
import type { ProxySettings } from "./ProxiesPanel";
import {
  expectAccessible,
  hoursAgo,
  overview,
  passwordAgain,
  proof,
  sectionView,
  sent,
  serveServer,
  typePassword,
} from "./serverTesting";
import type { SettingsSectionUpdate } from "./settings";

function proxies(values: Partial<ProxySettings> = {}) {
  return sectionView<ProxySettings>(
    "proxies",
    { knownProxies: [], knownNetworks: [], ...values },
    {
      apply: [{ host: "ddt-01", version: 3, state: "Applied", message: null, updatedUtc: null }],
      reauthenticate: ["knownProxies", "knownNetworks"],
    },
  );
}

const defaults: Record<string, string> = {
  Default: "Information",
  "Microsoft.AspNetCore": "Warning",
  "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
  "Microsoft.EntityFrameworkCore.Infrastructure": "Warning",
};

// The category of the row added last.
function lastCategory(levels: HTMLElement): HTMLElement {
  const fields = within(levels).getAllByRole("textbox", { name: "Category" });

  return nth(fields, fields.length - 1);
}

function logging(logLevel: Record<string, string> = defaults) {
  return sectionView<LoggingSettings>("logging", { logLevel });
}

describe("ServerPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("tells anyone but an administrator that only administrators see it, and reads nothing", async () => {
    const { requests } = serveServer({}, { user: operator });

    expect(
      await screen.findByText("Only administrators see and change the server's settings."),
    ).toBeInTheDocument();
    expect(screen.queryByRole("tab")).not.toBeInTheDocument();
    expect(requests.filter((request) => request.path.startsWith("/api/settings"))).toEqual([]);
  });

  it("shows what configuration decides and how each section stands, and warns about an unreadable key ring", async () => {
    serveServer({
      "GET /api/settings": () => json(overview({ keyRingReadable: false })),
    });

    expect(await screen.findByText("This server saves no settings")).toBeInTheDocument();
    expect(
      screen.getByText(/cannot read the key ring the stored secrets were encrypted with/),
    ).toBeInTheDocument();

    const configuration = screen.getByRole("list", { name: "Configuration values" });
    const store = within(configuration).getByText("DDT:StorePath").closest("li");
    expect(store).toHaveTextContent("/var/lib/ddt");
    expect(store).toHaveTextContent("default");
    expect(
      within(configuration).getByText("DDT:Https:SubjectAlternativeNames").closest("li"),
    ).toHaveTextContent(/Set\s*from an environment variable/);
    expect(
      within(configuration).getByText("Kestrel:Certificates:Default:Password").closest("li"),
    ).toHaveTextContent("Not set");

    const sections = screen.getByRole("list", { name: "Settings sections" });
    expect(within(sections).getByRole("link", { name: "Deployment defaults" })).toHaveAttribute(
      "href",
      "/deployment/defaults",
    );
    expect(within(sections).getByRole("link", { name: "Network boot" })).toHaveAttribute(
      "href",
      "/boot/network",
    );
    expect(within(sections).getByRole("link", { name: "Proxies" })).toHaveAttribute(
      "href",
      "/admin/server?tab=proxies",
    );
    expect(within(sections).getByText("1 set in configuration")).toBeInTheDocument();
    expect(within(sections).getByText("2 problems")).toBeInTheDocument();
    expect(within(sections).getByText("Saved 2 hours ago by admin")).toBeInTheDocument();
    expect(within(sections).getByText("Port 67 is in use.")).toBeInTheDocument();

    await expectAccessible();
  });

  it("opens the tab of a section this page edits from the overview", async () => {
    const { router } = serveServer({
      "GET /api/settings/proxies": () => json(proxies()),
    });

    const sections = await screen.findByRole("list", { name: "Settings sections" });
    press(within(sections).getByRole("link", { name: "Proxies" }));

    expect(await screen.findByRole("textbox", { name: "Proxy addresses" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Proxies" })).toHaveAttribute("aria-selected", "true");
    expect(router.state.location.search).toEqual({ tab: "proxies" });
  });

  it("saves the proxies with the password again, and shows the answer without reading them again", async () => {
    const { requests } = serveServer(
      {
        "GET /api/settings/proxies": () => json(proxies()),
        "POST /api/settings/reauthenticate": () => proof("proxies-proof"),
        "PUT /api/settings/proxies": (request) => {
          const update = request.body as SettingsSectionUpdate<ProxySettings>;

          return request.headers["x-ddt-reauthentication"] === "proxies-proof"
            ? json(
                sectionView("proxies", update.values, {
                  version: 4,
                  updatedUtc: new Date().toISOString(),
                  updatedBy: "Ada Admin",
                }),
              )
            : passwordAgain("knownProxies");
        },
      },
      { tab: "proxies" },
    );

    const addresses = await screen.findByRole("textbox", { name: "Proxy addresses" });
    fill(addresses, "10.0.0.2\n 10.0.0.3 ");
    fill(screen.getByRole("textbox", { name: "Proxy networks" }), "10.0.1.0/24");
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await typePassword();

    expect(await screen.findByText("Saved now by Ada Admin.")).toBeInTheDocument();
    const puts = sent(requests, "PUT", "/api/settings/proxies");
    expect(
      puts.map((request) => request.headers["x-ddt-reauthentication"] === "proxies-proof"),
    ).toEqual([false, true]);
    expect((puts[1]?.body as SettingsSectionUpdate<ProxySettings>).values).toEqual({
      knownProxies: ["10.0.0.2", "10.0.0.3"],
      knownNetworks: ["10.0.1.0/24"],
    });
    expect(reads(requests, "/api/settings/proxies")).toBe(1);
  });

  it("lists the log files of a server that writes them, to download", async () => {
    serveServer(
      {
        "GET /api/settings/logging": () => json(logging()),
        "GET /api/server/logs": () =>
          json([
            { name: "ddt-20261001.log", size: 2048, writtenUtc: new Date().toISOString() },
            { name: "ddt-20260930.log", size: 1_048_576, writtenUtc: hoursAgo(24) },
          ]),
      },
      { tab: "logging" },
    );

    const today = await screen.findByRole("link", { name: "ddt-20261001.log" });
    expect(today).toHaveAttribute("href", "/api/server/logs/ddt-20261001.log");
    expect(today).toHaveAttribute("download", "ddt-20261001.log");
    expect(screen.getByRole("link", { name: "ddt-20260930.log" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Log files" })).toBeInTheDocument();

    await expectAccessible();
  });

  it("shows no log files where the server logs to its console", async () => {
    serveServer({ "GET /api/settings/logging": () => json(logging()) }, { tab: "logging" });

    await screen.findByRole("list", { name: "Log levels" });
    expect(screen.queryByRole("heading", { name: "Log files" })).not.toBeInTheDocument();
  });

  it("shows the log levels with their defaults, and saves a category added at a level", async () => {
    const { requests } = serveServer(
      {
        "GET /api/settings/logging": () => json(logging()),
        "PUT /api/settings/logging": (request) =>
          json(
            sectionView(
              "logging",
              (request.body as SettingsSectionUpdate<LoggingSettings>).values,
              {
                version: 4,
                updatedUtc: new Date().toISOString(),
                updatedBy: "Ada Admin",
              },
            ),
          ),
      },
      { tab: "logging" },
    );

    const levels = await screen.findByRole("list", { name: "Log levels" });
    expect(
      within(levels)
        .getAllByRole("textbox", { name: "Category" })
        .map((field) => (field as HTMLInputElement).value),
    ).toEqual(Object.keys(defaults));
    expect(screen.getByRole("button", { name: "Use the defaults" })).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Remove Default" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Add a category" }));
    const added = lastCategory(levels);
    expect(added).toHaveValue("");
    fill(added, "DDT.Pxe");
    // A level's option reads its name and what it means.
    press(selectKey(levels, "Level of DDT.Pxe"));
    press(within(await screen.findByRole("listbox")).getByRole("option", { name: /^Trace/ }));
    await waitFor(() => {
      expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    });
    fireEvent.click(
      within(levels).getByRole("button", {
        name: "Remove Microsoft.EntityFrameworkCore.Infrastructure",
      }),
    );
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("Saved now by Ada Admin.")).toBeInTheDocument();
    expect(
      sent(requests, "PUT", "/api/settings/logging").map(
        (request) => (request.body as SettingsSectionUpdate<LoggingSettings>).values,
      ),
    ).toEqual([
      {
        logLevel: {
          Default: "Information",
          "Microsoft.AspNetCore": "Warning",
          "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
          "DDT.Pxe": "Trace",
        },
      },
    ]);
    expect(reads(requests, "/api/settings/logging")).toBe(1);
    expect(screen.getByRole("button", { name: "Use the defaults" })).toBeEnabled();
  });

  it("marks the row the server refused, and saves nothing", async () => {
    serveServer(
      {
        "GET /api/settings/logging": () => json(logging()),
        "PUT /api/settings/logging": () =>
          json(
            {
              title: "One or more validation errors occurred.",
              status: 400,
              errors: {
                "logLevel[DDT.Pxe]": [
                  "'Verbose' is not a log level. Use Trace, Debug, Information, Warning, Error, Critical or None.",
                ],
              },
            },
            400,
          ),
      },
      { tab: "logging" },
    );

    const levels = await screen.findByRole("list", { name: "Log levels" });
    fireEvent.click(screen.getByRole("button", { name: "Add a category" }));
    fill(lastCategory(levels), "DDT.Pxe");
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(
      await screen.findByText("Nothing was saved. The fields marked above say why."),
    ).toBeInTheDocument();
    const refused = within(levels).getByDisplayValue("DDT.Pxe");
    expect(refused).toHaveAttribute("aria-invalid", "true");
    expect(within(levels).getByText(/'Verbose' is not a log level/)).toBeInTheDocument();
  });

  it("says when a category is listed twice", async () => {
    serveServer(
      { "GET /api/settings/logging": () => json(logging({ Default: "Information" })) },
      { tab: "logging" },
    );

    const levels = await screen.findByRole("list", { name: "Log levels" });
    fireEvent.click(screen.getByRole("button", { name: "Add a category" }));
    fill(lastCategory(levels), "ddt.pxe");
    fireEvent.click(screen.getByRole("button", { name: "Add a category" }));
    fill(lastCategory(levels), "DDT.Pxe");

    await waitFor(() => {
      expect(
        within(levels).getByText("ddt.pxe is listed again below, whose level applies."),
      ).toBeInTheDocument();
    });
    await expectAccessible();
  });
});
