// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { press } from "@/test/aria";
import { administrator, viewer } from "@/test/builders";
import { renderPage } from "@/test/renderPage";
import { rowOf } from "@/test/rowOf";
import type { Routes } from "@/test/server";

import type { ImportSources, ImportStatus, MdtShareView } from "./imports";

const sources: ImportSources = {
  folders: [
    { path: "C:\\ProgramData\\DDT\\import", default: true },
    { path: "D:\\DeploymentShare", default: false },
  ],
  files: [
    {
      path: "C:\\ProgramData\\DDT\\import\\Win11_24H2.iso",
      name: "Win11_24H2.iso",
      sizeBytes: 6 * 1024 ** 3,
      modifiedUtc: "2026-10-01T10:00:00Z",
    },
  ],
  shares: ["D:\\DeploymentShare"],
  job: null,
};

const share: MdtShareView = {
  path: "D:\\DeploymentShare",
  imageFiles: [
    {
      file: ".\\Operating Systems\\W11\\sources\\install.wim",
      sizeBytes: 5 * 1024 ** 3,
      found: true,
      names: ["Windows 11 Pro", "Windows 11 Enterprise"],
    },
    {
      file: ".\\Operating Systems\\W10\\sources\\install.wim",
      sizeBytes: 0,
      found: false,
      names: ["Windows 10"],
    },
  ],
  driverGroups: [
    {
      id: "{g1}",
      name: "Out-of-Box Drivers\\Dell Inc.\\Latitude 7440",
      drivers: 12,
      sizeBytes: 300 * 1024 ** 2,
      manufacturer: "Dell Inc.",
      model: "Latitude 7440",
    },
    {
      id: "{g2}",
      name: "Out-of-Box Drivers\\WinPE x64",
      drivers: 1,
      sizeBytes: 1024 ** 2,
      manufacturer: null,
      model: null,
    },
  ],
  notImported: {
    applications: ["7-Zip"],
    taskSequences: ["Deploy Windows 11"],
    rules: ["Default"],
  },
};

function status(overrides: Partial<ImportStatus> = {}): ImportStatus {
  return {
    startedUtc: "2026-10-02T08:00:00Z",
    startedBy: "admin",
    state: "Running",
    finishedUtc: null,
    item: "Win11_24H2.iso",
    doneBytes: 1024 ** 3,
    totalBytes: 4 * 1024 ** 3,
    items: 1,
    results: [],
    ...overrides,
  };
}

function open(routes: Routes = {}, user = administrator) {
  return renderPage({
    path: "/library/images",
    user,
    routes: {
      "GET /api/images": { body: [] },
      "GET /api/images/uploads": { body: [] },
      "GET /api/images/import": { body: sources },
      ...routes,
    },
  });
}

describe("ImportPanel", () => {
  it("imports a file that is on the server already and follows it over the hub", async () => {
    const { server, hub } = await open({ "POST /api/images/import": { status: 202 } });

    const panel = rowOf(
      await screen.findByRole("heading", { name: "Import from the server" }),
      "section",
    );
    const row = rowOf(within(panel).getByText("Win11_24H2.iso"), "li");
    expect(row).toHaveTextContent("6 GB");

    press(within(row).getByRole("button", { name: "Import" }));
    await waitFor(() => {
      expect(server.changes().map((request) => request.body)).toEqual([
        { paths: ["C:\\ProgramData\\DDT\\import\\Win11_24H2.iso"] },
      ]);
    });

    act(() => {
      hub?.push("importChanged", status());
    });
    const bar = await within(panel).findByRole("progressbar", {
      name: "Reading Win11_24H2.iso, 1 of 1",
    });
    expect(bar).toHaveAttribute("aria-valuenow", "25");
    expect(within(row).getByRole("button", { name: "Import" })).toBeDisabled();

    act(() => {
      hub?.push(
        "importChanged",
        status({
          state: "Finished",
          item: null,
          results: [
            { name: "Win11_24H2.iso", outcome: "Added", reason: null, reasonText: null },
            {
              name: "other.iso",
              outcome: "Refused",
              reason: { code: "no.such.code", args: {} },
              reasonText: "This ISO holds no Windows image.",
            },
          ],
        }),
      );
    });

    expect(
      await within(panel).findByText("Win11_24H2.iso is in the library now."),
    ).toBeInTheDocument();
    expect(
      within(panel).getByText("other.iso was not imported. This ISO holds no Windows image."),
    ).toBeInTheDocument();
    expect(server.count("GET /api/images/import")).toBe(1);
  });

  it("shows what an MDT deployment share holds and imports what stays ticked", async () => {
    const { server } = await open({
      "POST /api/images/import/mdt/inspect": { body: share },
      "POST /api/images/import/mdt": { status: 202 },
    });

    press(await screen.findByRole("button", { name: "Choose what to import" }));
    const dialog = await screen.findByRole("dialog", {
      name: "Import from an MDT deployment share",
    });

    expect(
      await within(dialog).findByRole("checkbox", {
        name: /Windows 11 Pro, Windows 11 Enterprise/,
      }),
    ).toBeChecked();
    expect(within(dialog).getByRole("checkbox", { name: /Windows 10/ })).toBeDisabled();
    expect(
      within(dialog).getByRole("checkbox", {
        name: /Latitude 7440.*For Dell Inc\. Latitude 7440\./,
      }),
    ).toBeChecked();
    expect(dialog).toHaveTextContent("Task sequences: Deploy Windows 11.");
    expect(dialog).toHaveTextContent("Applications: 7-Zip.");

    press(within(dialog).getByRole("checkbox", { name: /WinPE x64/ }));
    press(within(dialog).getByRole("button", { name: "Import" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(server.changes().at(-1)?.body).toEqual({
      path: "D:\\DeploymentShare",
      imageFiles: [".\\Operating Systems\\W11\\sources\\install.wim"],
      driverGroups: ["{g1}"],
    });
  });

  it("says where to put files when the server has none, and asks nothing for a viewer", async () => {
    await open({ "GET /api/images/import": { body: { ...sources, files: [], shares: [] } } });

    expect(await screen.findByText(/copied into/)).toHaveTextContent(
      "C:\\ProgramData\\DDT\\import",
    );
  });

  it("reads nothing for someone who cannot import", async () => {
    const { server } = await open({}, viewer);

    expect(await screen.findByRole("heading", { name: "OS images" })).toBeInTheDocument();
    expect(server.count("GET /api/images/import")).toBe(0);
    expect(
      screen.queryByRole("heading", { name: "Import from the server" }),
    ).not.toBeInTheDocument();
  });
});
