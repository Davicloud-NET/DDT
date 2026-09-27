// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { HardwareModelCount } from "@/machines/machines";
import type { PackageSummary, UpdatePackageRequest } from "@/packages/packages";
import type { RunScriptStep } from "@/sequences/sequences";
import { newStep } from "@/sequences/steps";
import { chooseMenuItem, fill, nth, press } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import {
  administrator,
  packageSummary,
  sequenceSummary,
  sequenceView,
  viewer,
} from "@/test/builders";
import { renderPage } from "@/test/renderPage";
import { json, type Answer, type Routes } from "@/test/server";

const drivers = packageSummary();

const scripts = packageSummary({
  id: "0193a4b2-0000-7000-8000-0000000000b2",
  name: "Lab scripts",
  kind: "Files",
  targets: [],
  fileCount: 2,
  originalFileName: "scripts.zip",
});

const models: HardwareModelCount[] = [
  { manufacturer: "Dell Inc.", model: "Latitude 7440", machines: 3 },
  { manufacturer: "Dell Inc.", model: "Latitude 5440", machines: 2 },
  { manufacturer: "Microsoft Corporation", model: "Virtual Machine", machines: 4 },
];

const install = sequenceSummary({ id: "s1", name: "Install Windows" });
const lab = sequenceSummary({ id: "s2", name: "Lab setup" });

function open(
  kind: "drivers" | "files",
  packages: PackageSummary[] | Answer,
  routes: Routes = {},
  user = administrator,
) {
  return renderPage({
    path: `/library/${kind}`,
    user,
    routes: {
      "GET /api/packages": Array.isArray(packages) ? { body: packages } : packages,
      "GET /api/machines/models": { body: models },
      "GET /api/sequences": { body: [install, lab] },
      "GET /api/sequences/s1": { body: sequenceView(install, [newStep("injectDrivers", "d")]) },
      "GET /api/sequences/s2": {
        body: sequenceView(lab, [
          { ...(newStep("runScript", "r") as RunScriptStep), packageId: scripts.id },
        ]),
      },
      "GET /api/images/uploads": { body: [] },
      ...routes,
    },
  });
}

function row(name: string): HTMLElement {
  return screen.getByRole("row", { name: new RegExp(`^${name}`) });
}

function actionsFor(name: string): HTMLElement {
  return screen.getByRole("button", { name: `Actions for ${name}` });
}

async function change(name: string): Promise<HTMLElement> {
  await chooseMenuItem(
    await screen.findByRole("button", { name: `Actions for ${name}` }),
    "Change",
  );

  return screen.findByRole("dialog", { name: "Change the package" });
}

function selectFile(file: File) {
  const input = screen.getByRole("region", { name: "Upload" }).querySelector('input[type="file"]');

  if (input === null) {
    throw new Error("There is no file to choose.");
  }

  fireEvent.change(input, { target: { files: [file] } });
}

// A server that takes one small zip in one slice and answers with the package it became.
function uploadOf(file: string, added: PackageSummary, kind: "Drivers" | "Files"): Routes {
  const session = {
    id: "0193a4b2-0000-7000-8000-0000000000c9",
    fileName: file,
    length: 4,
    lastModified: 1_000,
    offset: 0,
    chunkBytes: 8,
    kind,
  };

  return {
    "POST /api/images/uploads": { status: 201, body: session },
    [`PATCH /api/images/uploads/${session.id}`]: () =>
      new Response(null, { status: 204, headers: { "Upload-Offset": "4" } }),
    [`POST /api/images/uploads/${session.id}/complete`]: { status: 201, body: added },
  };
}

describe("PackagesPage", () => {
  describe("the lists", () => {
    it("explains what driver packages are for when there is none", async () => {
      await open("drivers", []);

      expect(await screen.findByRole("heading", { level: 1, name: "Drivers" })).toBeInTheDocument();
      expect(await screen.findByText("No driver packages yet")).toBeInTheDocument();
      expect(
        screen.getByText(/^Upload a zip of drivers and choose the models it is for\./),
      ).toBeInTheDocument();
    });

    it("explains what file packages are for when there is none", async () => {
      await open("files", [drivers]);

      expect(await screen.findByText("No file packages yet")).toBeInTheDocument();
      expect(
        screen.getByText(
          "Upload a zip of files, then choose it in a Run script step of a task sequence.",
        ),
      ).toBeInTheDocument();
    });

    it("shows what a driver package holds and the machines it matches", async () => {
      await open("drivers", [drivers, scripts]);

      await screen.findByRole("grid", { name: "Driver packages" });
      const driverRow = within(row("Latitude drivers"));

      expect(driverRow.getByText("latitude.zip")).toBeInTheDocument();
      expect(driverRow.getByText("Dell Inc. Latitude*")).toBeInTheDocument();
      expect(driverRow.getByText("Matches 5 registered machines.")).toBeInTheDocument();
      expect(driverRow.getByText("300 MB")).toBeInTheDocument();
      expect(driverRow.getByText("14 files, 900 MB unpacked")).toBeInTheDocument();
      expect(screen.queryByText("Lab scripts")).not.toBeInTheDocument();
    });

    it("shows the sequences that use a file package", async () => {
      await open("files", [drivers, scripts]);

      await screen.findByRole("grid", { name: "File packages" });

      expect(
        await within(row("Lab scripts")).findByRole("link", { name: "Lab setup" }),
      ).toHaveAttribute("href", "/deployment/sequences/s2");
      expect(
        within(row("Lab scripts")).queryByRole("link", { name: "Install Windows" }),
      ).toBeNull();
    });

    it("warns about a driver package that targets no model", async () => {
      await open("drivers", [packageSummary({ targets: [] })]);

      expect(
        await within(await screen.findByRole("grid", { name: "Driver packages" })).findByText(
          "No model yet: no machine gets these drivers.",
        ),
      ).toBeInTheDocument();
    });

    it("finds a package by its model", async () => {
      await open("drivers", [
        drivers,
        packageSummary({
          id: "0193a4b2-0000-7000-8000-0000000000b3",
          name: "Hyper-V drivers",
          targets: [{ manufacturer: "Microsoft Corporation", model: "Virtual Machine" }],
        }),
      ]);

      await screen.findByRole("grid", { name: "Driver packages" });
      fill(screen.getByRole("searchbox", { name: "Find a driver package" }), "virtual");

      await waitFor(() => {
        expect(screen.queryByRole("row", { name: /^Latitude drivers/ })).not.toBeInTheDocument();
      });
      expect(row("Hyper-V drivers")).toBeInTheDocument();
    });

    it("takes a package from the hub as it changes, without reading the list again", async () => {
      const { hub, server } = await open("files", [scripts]);

      await screen.findByRole("grid", { name: "File packages" });

      act(() => {
        hub?.push("packageChanged", { ...scripts, description: "Wallpapers and printers" });
      });

      expect(await screen.findByText("Wallpapers and printers")).toBeInTheDocument();
      expect(server.count("GET /api/packages")).toBe(1);
    });

    it("shows a viewer the library without changes", async () => {
      await open("drivers", [drivers], {}, viewer);

      await screen.findByRole("grid", { name: "Driver packages" });
      expect(screen.queryByRole("region", { name: "Upload" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /^Actions for/ })).not.toBeInTheDocument();
    });
  });

  describe("uploads", () => {
    it("uploads a zip as a file package and names the package it became", async () => {
      const { server } = await open("files", [], uploadOf("scripts.zip", scripts, "Files"));

      await screen.findByText("No file packages yet");
      selectFile(new File(["zip!"], "scripts.zip", { lastModified: 1_000 }));

      expect(await screen.findByText("Added Lab scripts from scripts.zip.")).toBeInTheDocument();
      expect(await screen.findByRole("row", { name: /^Lab scripts/ })).toBeInTheDocument();
      expect(
        server.requests.find(
          (request) => request.method === "POST" && request.path === "/api/images/uploads",
        )?.body,
      ).toEqual({ fileName: "scripts.zip", length: 4, lastModified: 1_000, kind: "Files" });
      expect(server.count("GET /api/packages")).toBe(1);
    });

    it("opens a new driver package at once to choose its models", async () => {
      const added = packageSummary({ targets: [] });
      const { server } = await open("drivers", [], uploadOf("latitude.zip", added, "Drivers"));

      await screen.findByText("No driver packages yet");
      expect(
        screen.getByRole("region", { name: "Upload" }).querySelector('input[type="file"]'),
      ).toHaveAttribute("accept", ".zip");
      selectFile(new File(["zip!"], "latitude.zip", { lastModified: 1_000 }));

      const dialog = await screen.findByRole("dialog", { name: "Change the package" });
      expect(
        within(dialog).getByText("No model yet: no machine gets these drivers."),
      ).toBeInTheDocument();
      expect(
        server.requests.find(
          (request) => request.method === "POST" && request.path === "/api/images/uploads",
        )?.body,
      ).toEqual({ fileName: "latitude.zip", length: 4, lastModified: 1_000, kind: "Drivers" });
    });
  });

  describe("changing a package", () => {
    it("saves changed models and shows the server's answer without reading the list again", async () => {
      const { server } = await open("drivers", [drivers], {
        [`PUT /api/packages/${drivers.id}`]: (request) =>
          json({ ...drivers, ...(request.body as UpdatePackageRequest) }),
      });

      const dialog = await change("Latitude drivers");
      const model = nth(within(dialog).getAllByRole("combobox", { name: "Model" }), 0);

      expect(within(dialog).getByRole("textbox", { name: "Name" })).toHaveValue("Latitude drivers");
      expect(model).toHaveValue("Latitude*");
      expect(within(dialog).getByText("Matches 5 registered machines.")).toBeInTheDocument();

      fill(model, "Latitude 7440");
      expect(within(dialog).getByText("Matches 3 registered machines.")).toBeInTheDocument();
      expect(server.changes()).toEqual([]);

      press(within(dialog).getByRole("button", { name: "Save package" }));

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      expect(server.changes().map((request) => request.body)).toEqual([
        {
          name: "Latitude drivers",
          description: null,
          targets: [{ manufacturer: "Dell Inc.", model: "Latitude 7440" }],
          bootImage: false,
        },
      ]);
      expect(
        within(row("Latitude drivers")).getByText("Dell Inc. Latitude 7440"),
      ).toBeInTheDocument();
      expect(
        within(row("Latitude drivers")).getByText("Matches 3 registered machines."),
      ).toBeInTheDocument();
      expect(server.count("GET /api/packages")).toBe(1);
    });

    it("adds and removes models", async () => {
      const { server } = await open("drivers", [drivers], {
        [`PUT /api/packages/${drivers.id}`]: (request) =>
          json({ ...drivers, ...(request.body as UpdatePackageRequest) }),
      });

      const dialog = await change("Latitude drivers");
      press(within(dialog).getByRole("button", { name: "Add a model" }));
      const manufacturers = within(dialog).getAllByRole("combobox", { name: "Manufacturer" });
      const modelFields = within(dialog).getAllByRole("combobox", { name: "Model" });
      fill(nth(manufacturers, 1), "Microsoft Corporation");
      fill(nth(modelFields, 1), "Virtual Machine");
      press(within(dialog).getByRole("button", { name: "Remove model 1" }));

      expect(within(dialog).getByText("Matches 4 registered machines.")).toBeInTheDocument();
      press(within(dialog).getByRole("button", { name: "Save package" }));

      await waitFor(() => {
        expect(server.changes().map((request) => request.body)).toEqual([
          {
            name: "Latitude drivers",
            description: null,
            targets: [{ manufacturer: "Microsoft Corporation", model: "Virtual Machine" }],
            bootImage: false,
          },
        ]);
      });
    });

    it("sends nothing when the dialog is cancelled", async () => {
      const { server } = await open("drivers", [drivers]);

      const dialog = await change("Latitude drivers");
      fill(within(dialog).getByRole("textbox", { name: "Name" }), "Latitude 7440 drivers");
      press(within(dialog).getByRole("button", { name: "Cancel" }));

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      expect(server.changes()).toEqual([]);
      expect(row("Latitude drivers")).toBeInTheDocument();
    });

    it("keeps an open dialog's edit when another administrator changes the library", async () => {
      const { hub } = await open("files", [drivers, scripts]);

      const dialog = await change("Lab scripts");
      fill(within(dialog).getByRole("textbox", { name: "Name" }), "Lab scripts 2");

      act(() => {
        hub?.push("packageChanged", { ...scripts, description: "Wallpapers and printers" });
      });

      expect(await screen.findByText("Wallpapers and printers")).toBeInTheDocument();
      expect(within(dialog).getByRole("textbox", { name: "Name" })).toHaveValue("Lab scripts 2");
    });

    it("shows the server's refusal of the models in the dialog", async () => {
      await open("drivers", [drivers], {
        [`PUT /api/packages/${drivers.id}`]: {
          status: 400,
          body: { title: "Invalid", errors: { targets: ["Put at least 3 characters before *."] } },
        },
      });

      const dialog = await change("Latitude drivers");
      const model = nth(within(dialog).getAllByRole("combobox", { name: "Model" }), 0);
      fill(model, "L*");
      press(within(dialog).getByRole("button", { name: "Save package" }));

      expect(
        await within(dialog).findByText("Put at least 3 characters before *."),
      ).toBeInTheDocument();
      expect(screen.getByRole("dialog", { name: "Change the package" })).toBeInTheDocument();
    });

    it("shows the server's refusal of the name at the field, and saves once it is fixed", async () => {
      let saves = 0;
      const { server } = await open("drivers", [drivers], {
        [`PUT /api/packages/${drivers.id}`]: (request) => {
          saves++;

          return saves === 1
            ? json({ title: "Invalid", errors: { name: ["Another package has this name."] } }, 400)
            : json({ ...drivers, ...(request.body as UpdatePackageRequest) });
        },
      });

      const dialog = await change("Latitude drivers");
      const name = within(dialog).getByRole("textbox", { name: "Name" });
      fill(name, "Lab scripts");
      press(within(dialog).getByRole("button", { name: "Save package" }));

      await waitFor(() => {
        expect(name).toHaveAttribute("aria-invalid", "true");
      });
      expect(name).toHaveAccessibleDescription("Another package has this name.");

      fill(name, "Latitude 7440 drivers");
      press(within(dialog).getByRole("button", { name: "Save package" }));

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      expect(
        server.changes().map((request) => (request.body as UpdatePackageRequest).name),
      ).toEqual(["Lab scripts", "Latitude 7440 drivers"]);
    });
  });

  describe("deleting", () => {
    it("names the sequences a deletion affects and shows the server's refusal in place", async () => {
      await open("files", [drivers, scripts], {
        [`DELETE /api/packages/${scripts.id}`]: {
          status: 409,
          body: {
            title:
              "Machines are waiting to install this package or are installing it. Cancel those runs or let them finish, then delete it.",
          },
        },
      });

      await within(await screen.findByRole("grid", { name: "File packages" })).findByRole("link", {
        name: "Lab setup",
      });
      await chooseMenuItem(actionsFor("Lab scripts"), "Delete");

      const dialog = await screen.findByRole("dialog", { name: "Delete Lab scripts?" });
      expect(dialog).toHaveAccessibleDescription(
        "Lab scripts (300 MB) is deleted from the library. The sequence Lab setup names it in a Run script step and shows a problem until another package is chosen. The server refuses while a machine is assigned or runs a sequence that uses it.",
      );

      press(within(dialog).getByRole("button", { name: "Delete package" }));

      expect(await within(dialog).findByRole("alert")).toHaveTextContent(
        "Machines are waiting to install this package or are installing it.",
      );
    });

    it("names the sequences that inject a driver package", async () => {
      const { server } = await open("drivers", [drivers], {
        [`DELETE /api/packages/${drivers.id}`]: { status: 204 },
      });

      await screen.findByRole("grid", { name: "Driver packages" });
      await chooseMenuItem(actionsFor("Latitude drivers"), "Delete");

      const dialog = await screen.findByRole("dialog", { name: "Delete Latitude drivers?" });
      await waitFor(() => {
        expect(dialog).toHaveAccessibleDescription(
          "Latitude drivers (300 MB) is deleted from the library. Machines of Dell Inc. Latitude* no longer get these drivers from the Inject drivers step of Install Windows. The server refuses while a machine is assigned or runs a sequence that uses it.",
        );
      });

      press(within(dialog).getByRole("button", { name: "Delete package" }));

      expect(await screen.findByText("No driver packages yet")).toBeInTheDocument();
      expect(server.count("GET /api/packages")).toBe(1);
    });

    it("does not claim no sequence names a package while a sequence could not be read", async () => {
      await open("files", [drivers, scripts], {
        "GET /api/sequences/s1": { status: 500, body: { title: "The server failed." } },
      });

      await screen.findByRole("grid", { name: "File packages" });
      await chooseMenuItem(actionsFor("Lab scripts"), "Delete");

      const dialog = await screen.findByRole("dialog", { name: "Delete Lab scripts?" });
      expect(dialog).toHaveAccessibleDescription(
        "Lab scripts (300 MB) is deleted from the library. Which sequences name it is not known, because not every sequence could be read; those that do show a problem until another package is chosen. The server refuses while a machine is assigned or runs a sequence that uses it.",
      );
      expect(dialog).not.toHaveTextContent("No sequence names it.");
    });
  });

  describe("accessibility", () => {
    it("has no violations with driver packages listed", async () => {
      await open("drivers", [drivers, packageSummary({ id: "b9", name: "Other", targets: [] })]);

      await screen.findByRole("grid", { name: "Driver packages" });
      await expectNoAxeViolations();
    });

    it("has no violations with file packages listed", async () => {
      await open("files", [scripts]);

      await within(await screen.findByRole("grid", { name: "File packages" })).findByRole("link", {
        name: "Lab setup",
      });
      await expectNoAxeViolations();
    });

    it("has no violations without packages", async () => {
      await open("files", [], {}, viewer);

      await screen.findByText("No file packages yet");
      await expectNoAxeViolations();
    });

    it("has no violations with the change dialog open", async () => {
      await open("drivers", [drivers]);

      await change("Latitude drivers");
      await expectNoAxeViolations();
    });

    it("has no violations with the delete dialog open", async () => {
      await open("files", [scripts]);

      await screen.findByRole("grid", { name: "File packages" });
      await chooseMenuItem(actionsFor("Lab scripts"), "Delete");
      await screen.findByRole("dialog");
      await expectNoAxeViolations();
    });
  });
});
