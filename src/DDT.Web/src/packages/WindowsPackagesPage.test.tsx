// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { ImportFile, ImportSources, LanguageIsoView } from "@/images/import/imports";
import type { PackageSummary } from "@/packages/packages";
import { chooseMenuItem, fill, nth, press } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import { administrator, packageSummary, viewer } from "@/test/builders";
import { renderPage } from "@/test/renderPage";
import type { Routes } from "@/test/server";

const german = packageSummary({
  id: "0193a4b2-0000-7000-8000-0000000000d1",
  name: "de-DE language pack (26100)",
  kind: "WindowsPackage",
  targets: [],
  fileCount: 6,
  originalFileName: "de-DE language pack (26100).zip",
  windows: {
    content: "Language",
    languages: ["de-DE"],
    architecture: "x64",
    kbNumbers: [],
    builds: [26100],
  },
});

const update = packageSummary({
  id: "0193a4b2-0000-7000-8000-0000000000d2",
  name: "2026-11 cumulative update",
  kind: "WindowsPackage",
  targets: [],
  fileCount: 2,
  windows: {
    content: "Update",
    languages: [],
    architecture: "x64",
    kbNumbers: [5132061, 5043080],
    builds: [],
  },
});

const ISO_PATH = "D:\\var\\lib\\ddt\\import\\26100.1_CLIENT_LOF_PACKAGES_OEM.iso";

const isoFile: ImportFile = {
  path: ISO_PATH,
  name: "26100.1_CLIENT_LOF_PACKAGES_OEM.iso",
  sizeBytes: 6_251_841_536,
  modifiedUtc: "2026-11-14T04:00:00Z",
};

const sources: ImportSources = {
  folders: [{ path: "D:\\var\\lib\\ddt\\import", default: true }],
  files: [
    isoFile,
    { ...isoFile, path: "D:\\var\\lib\\ddt\\import\\install.wim", name: "install.wim" },
  ],
  shares: [],
  job: null,
};

const languages: LanguageIsoView = {
  path: ISO_PATH,
  languages: ["de-DE", "en-GB", "en-US", "es-ES", "fr-FR", "it-IT", "ja-JP", "nl-NL", "pl-PL"],
  suffix: "(26100)",
};

function open(packages: PackageSummary[], routes: Routes = {}, user = administrator) {
  return renderPage({
    path: "/library/windows-packages",
    user,
    routes: {
      "GET /api/packages": { body: [packageSummary(), ...packages] },
      "GET /api/machines/models": { body: [] },
      "GET /api/sequences": { body: [] },
      "GET /api/images/uploads": { body: [] },
      "GET /api/images/import": { body: sources },
      "POST /api/images/import/languages/inspect": { body: languages },
      "POST /api/images/import/languages": { status: 202 },
      ...routes,
    },
  });
}

function selectFile(file: File) {
  const input = screen.getByRole("region", { name: "Upload" }).querySelector('input[type="file"]');

  if (input === null) {
    throw new Error("There is no file to choose.");
  }

  fireEvent.change(input, { target: { files: [file] } });
}

describe("WindowsPackagesPage", () => {
  it("shows what each Windows package holds and leaves the other kinds out", async () => {
    await open([german, update]);

    const language = await screen.findByRole("row", { name: /^de-DE language pack/ });
    expect(within(language).getByText("Language")).toBeInTheDocument();
    expect(within(language).getByText("de-DE · x64 · builds 26100")).toBeInTheDocument();
    const cumulative = screen.getByRole("row", { name: /^2026-11 cumulative update/ });
    expect(within(cumulative).getByText("KB5132061, KB5043080 · x64")).toBeInTheDocument();
    expect(screen.queryByRole("row", { name: /^Latitude drivers/ })).not.toBeInTheDocument();
  });

  it("finds a package by its KB number", async () => {
    await open([german, update]);

    fill(await screen.findByRole("searchbox", { name: "Find a Windows package" }), "kb5043080");

    expect(screen.getByRole("row", { name: /^2026-11 cumulative update/ })).toBeInTheDocument();
    expect(screen.queryByRole("row", { name: /^de-DE language pack/ })).not.toBeInTheDocument();
  });

  it("marks an update for Windows builds", async () => {
    const saved = { ...update, windows: { ...update.windows, builds: [26100, 26200] } };
    const { server } = await open([update], {
      [`PUT /api/packages/${update.id}`]: { body: saved },
    });

    const row = await screen.findByRole("row", { name: /^2026-11 cumulative update/ });
    await chooseMenuItem(
      within(row).getByRole("button", { name: `Actions for ${update.name}` }),
      "Change",
    );
    const dialog = await screen.findByRole("dialog", { name: "Change the package" });
    const builds = within(dialog).getByRole("textbox", { name: "Windows builds" });
    fill(builds, "26100 x");
    expect(within(dialog).getByRole("button", { name: "Save package" })).toBeDisabled();
    fill(builds, "26200, 26100");
    press(within(dialog).getByRole("button", { name: "Save package" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(server.changes().map((request) => request.body)).toEqual([
      {
        name: update.name,
        description: null,
        targets: [],
        builds: [26200, 26100],
      },
    ]);
    expect(
      await within(row).findByText("KB5132061, KB5043080 · x64 · builds 26100, 26200"),
    ).toBeInTheDocument();
  });

  it("shows viewers the list without the upload or the ISO", async () => {
    await open([german], {}, viewer);

    await screen.findByRole("row", { name: /^de-DE language pack/ });
    expect(screen.queryByRole("button", { name: "Choose languages" })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Upload" })).not.toBeInTheDocument();
  });

  it("imports the ticked languages of an ISO with the suggested end of the names", async () => {
    const { server } = await open([]);

    const choose = await screen.findAllByRole("button", { name: "Choose languages" });
    expect(choose).toHaveLength(1);
    press(nth(choose, 0));
    const dialog = await screen.findByRole("dialog", { name: "Import languages" });
    await within(dialog).findByText("ja-JP");
    await expectNoAxeViolations();

    fill(within(dialog).getByRole("searchbox", { name: "Find a language" }), "japan");
    expect(within(dialog).queryByText("de-DE")).not.toBeInTheDocument();
    press(within(dialog).getByRole("checkbox", { name: /ja-JP/ }));
    expect(
      within(dialog).getByText("For example de-DE language pack (26100)."),
    ).toBeInTheDocument();
    press(within(dialog).getByRole("button", { name: "Import" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(server.changes().map((request) => [request.path, request.body])).toEqual([
      ["/api/images/import/languages/inspect", { path: ISO_PATH }],
      ["/api/images/import/languages", { path: ISO_PATH, languages: ["ja-JP"], suffix: "(26100)" }],
    ]);
  });

  it("opens the languages of an ISO once its upload is done", async () => {
    const session = {
      id: "0193a4b2-0000-7000-8000-0000000000c9",
      fileName: "lof.iso",
      length: 4,
      lastModified: 1_000,
      offset: 0,
      chunkBytes: 8,
      kind: "WindowsPackage",
    };
    await open([], {
      "POST /api/images/uploads": { status: 201, body: session },
      [`PATCH /api/images/uploads/${session.id}`]: () =>
        new Response(null, { status: 204, headers: { "Upload-Offset": "4" } }),
      [`POST /api/images/uploads/${session.id}/complete`]: { status: 201, body: isoFile },
    });

    await screen.findByText("No Windows packages yet");
    selectFile(new File(["iso!"], "lof.iso", { lastModified: 1_000 }));

    expect(
      await screen.findByText("lof.iso is on the server. Choose its languages."),
    ).toBeInTheDocument();
    expect(await screen.findByRole("dialog", { name: "Import languages" })).toBeInTheDocument();
  });
});
