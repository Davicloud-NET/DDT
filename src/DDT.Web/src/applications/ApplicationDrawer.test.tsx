// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { ApplicationView, SaveApplicationRequest } from "@/applications/applications";
import { chooseOption, fill, press } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import { applicationUploadOf, applicationView } from "@/test/applicationBuilders";
import { administrator } from "@/test/builders";
import { renderPage, type RenderedPage } from "@/test/renderPage";
import { json, type Routes } from "@/test/server";

const sevenZip = applicationView({ id: "a1" });
const runtime = applicationView({
  id: "a2",
  name: "VC++ runtime",
  publisher: "Microsoft",
  files: null,
  command: "vc_redist.x64.exe /install /quiet /norestart",
  detection: null,
});
const notepad = applicationView({ id: "a3", name: "Notepad++", files: null, detection: null });

function open(applications: ApplicationView[], routes: Routes = {}): Promise<RenderedPage> {
  return renderPage({
    path: "/library/applications",
    user: administrator,
    routes: {
      "GET /api/applications": { body: applications },
      "GET /api/application-bundles": { body: [] },
      "GET /api/images/uploads": { body: [] },
      ...routes,
    },
  });
}

// The server's answer to a save: the request as the view it becomes.
function saved(id: string, body: SaveApplicationRequest, revision = 1): ApplicationView {
  return applicationView({
    ...body,
    id,
    revision,
    files: body.packageId === null ? null : { packageId: body.packageId, name: "x", sizeBytes: 4 },
  });
}

async function drawerOf(name: string): Promise<HTMLElement> {
  press(await screen.findByRole("row", { name: new RegExp(`^${name}`) }));

  return screen.findByRole("dialog", { name: `Application ${name}` });
}

function selectFile(scope: HTMLElement, file: File) {
  const input = within(scope)
    .getByRole("region", { name: "Upload" })
    .querySelector('input[type="file"]');

  if (input === null) {
    throw new Error("There is no file to choose.");
  }

  fireEvent.change(input, { target: { files: [file] } });
}

describe("ApplicationDrawer", () => {
  it("opens a new application filled from the installer it was uploaded with", async () => {
    const posts: SaveApplicationRequest[] = [];
    const { server } = await open([], {
      ...applicationUploadOf("7z2409-x64.msi", {
        files: { packageId: "p9", name: "7z2409-x64.msi", sizeBytes: 4 },
        suggestion: {
          name: "7-Zip 24.09 (x64 edition)",
          publisher: "Igor Pavlov",
          version: "24.09.00.0",
          command: 'msiexec /i "7z2409-x64.msi" /qn /norestart',
          successExitCodes: [0],
          restartExitCodes: [3010, 1641],
          detection: { kind: "productCode", productCode: "{23170F69-40C1-2702-2409-000001000000}" },
        },
      }),
      "POST /api/applications": (request) => {
        posts.push(request.body as SaveApplicationRequest);
        return json(saved("a9", request.body as SaveApplicationRequest), 201);
      },
    });

    press(await screen.findByRole("button", { name: "Add application" }));
    const adding = await screen.findByRole("dialog", { name: "Add application" });
    selectFile(adding, new File(["msi!"], "7z2409-x64.msi", { lastModified: 1_000 }));

    const drawer = await screen.findByRole("dialog", { name: "Application New application" });
    expect(within(drawer).getByRole("textbox", { name: "Name" })).toHaveValue(
      "7-Zip 24.09 (x64 edition)",
    );
    expect(within(drawer).getByRole("textbox", { name: "Publisher" })).toHaveValue("Igor Pavlov");
    expect(within(drawer).getByRole("textbox", { name: "Command" })).toHaveValue(
      'msiexec /i "7z2409-x64.msi" /qn /norestart',
    );
    expect(
      within(drawer).getByRole("textbox", { name: "Exit codes that ask for a restart" }),
    ).toHaveValue("3010, 1641");
    expect(within(drawer).getByRole("textbox", { name: "Product code" })).toHaveValue(
      "{23170F69-40C1-2702-2409-000001000000}",
    );
    expect(within(drawer).getByText("7z2409-x64.msi")).toBeInTheDocument();
    expect(
      server.requests.find(
        (request) => request.method === "POST" && request.path === "/api/images/uploads",
      )?.body,
    ).toMatchObject({ kind: "Application" });

    press(within(drawer).getByRole("button", { name: "Save application" }));

    expect(await screen.findByRole("row", { name: /^7-Zip 24.09/ })).toBeInTheDocument();
    expect(posts).toEqual([
      expect.objectContaining({
        kind: "Command",
        packageId: "p9",
        version: "24.09.00.0",
        restartExitCodes: [3010, 1641],
        wingetId: null,
      }),
    ]);
  });

  it("adds a winget package with only its ID and version", async () => {
    const posts: SaveApplicationRequest[] = [];
    await open([sevenZip], {
      "POST /api/applications": (request) => {
        posts.push(request.body as SaveApplicationRequest);
        return json(saved("a9", request.body as SaveApplicationRequest), 201);
      },
    });

    press(await screen.findByRole("button", { name: "Add application" }));
    press(await screen.findByRole("button", { name: "winget package" }));
    const drawer = await screen.findByRole("dialog", { name: "Application New application" });

    expect(within(drawer).queryByRole("group", { name: "Files" })).not.toBeInTheDocument();
    expect(within(drawer).queryByRole("textbox", { name: "Command" })).not.toBeInTheDocument();
    fill(within(drawer).getByRole("textbox", { name: "Name" }), "Firefox");
    fill(within(drawer).getByRole("textbox", { name: "winget ID" }), " Mozilla.Firefox ");
    press(within(drawer).getByRole("button", { name: "Save application" }));

    await waitFor(() => {
      expect(posts).toHaveLength(1);
    });
    expect(posts[0]).toMatchObject({
      name: "Firefox",
      kind: "Winget",
      packageId: null,
      command: null,
      wingetId: "Mozilla.Firefox",
      wingetVersion: null,
      restartExitCodes: [],
    });
  });

  it("switches an application's kind and keeps what the other kind had", async () => {
    const puts: SaveApplicationRequest[] = [];
    await open([sevenZip], {
      "PUT /api/applications/a1": (request) => {
        puts.push(request.body as SaveApplicationRequest);
        return json(saved("a1", request.body as SaveApplicationRequest, 2));
      },
    });

    const drawer = await drawerOf("7-Zip");
    await chooseOption(drawer, "Kind", "winget package");

    expect(within(drawer).queryByRole("textbox", { name: "Command" })).not.toBeInTheDocument();
    fill(within(drawer).getByRole("textbox", { name: "winget ID" }), "7zip.7zip");

    await chooseOption(drawer, "Kind", "Command");
    expect(within(drawer).getByRole("textbox", { name: "Command" })).toHaveValue(
      "msiexec /i 7z2409-x64.msi /qn /norestart",
    );

    await chooseOption(drawer, "Kind", "winget package");
    expect(within(drawer).getByRole("textbox", { name: "winget ID" })).toHaveValue("7zip.7zip");
    press(within(drawer).getByRole("button", { name: "Save application" }));

    await waitFor(() => {
      expect(puts).toHaveLength(1);
    });
    expect(puts[0]).toMatchObject({
      revision: 1,
      kind: "Winget",
      packageId: null,
      command: null,
      wingetId: "7zip.7zip",
    });
  });

  it("shows the server's problems at their fields, and the others above the form", async () => {
    await open([sevenZip, runtime], {
      "PUT /api/applications/a1": () =>
        json(
          {
            title: "One or more validation errors occurred.",
            errors: {
              command: ["Enter the command that installs the application."],
              "detection.productCode": [
                "Enter the product code as a GUID, such as {23170F69-40C1-2702-2501-000001000000}.",
              ],
              "dependsOn[0]": ["This application is no longer in the library."],
              timeoutMinutes: ["Enter a timeout from 1 to 1440 minutes."],
              wingetId: ["Only a winget package has a winget ID and version. Leave both empty."],
            },
          },
          400,
        ),
    });

    const drawer = await drawerOf("7-Zip");
    await chooseOption(drawer, "Add a dependency", "VC++ runtime");
    press(within(drawer).getByRole("button", { name: "Save application" }));

    expect(
      await within(drawer).findByRole("textbox", { name: "Command" }),
    ).toHaveAccessibleDescription("Enter the command that installs the application.");
    await waitFor(() => {
      expect(
        within(drawer).getByRole("textbox", { name: "Product code" }),
      ).toHaveAccessibleDescription(
        "Enter the product code as a GUID, such as {23170F69-40C1-2702-2501-000001000000}.",
      );
    });
    const dependency = within(drawer).getByRole("group", { name: "Depends on" });
    expect(
      within(dependency).getByText("This application is no longer in the library."),
    ).toBeInTheDocument();
    expect(
      within(drawer).getByRole("textbox", { name: "Timeout in minutes" }),
    ).toHaveAccessibleDescription("Enter a timeout from 1 to 1440 minutes.");
    expect(
      within(drawer).getByText(
        "Only a winget package has a winget ID and version. Leave both empty.",
      ),
    ).toBeInTheDocument();
  });

  it("puts dependencies in order and saves them so", async () => {
    const puts: SaveApplicationRequest[] = [];
    await open([sevenZip, runtime, notepad], {
      "PUT /api/applications/a1": (request) => {
        puts.push(request.body as SaveApplicationRequest);
        return json(saved("a1", request.body as SaveApplicationRequest, 2));
      },
    });

    const drawer = await drawerOf("7-Zip");
    await chooseOption(drawer, "Add a dependency", "VC++ runtime");
    await chooseOption(drawer, "Add a dependency", "Notepad++");
    press(within(drawer).getByRole("button", { name: "Move Notepad++ up" }));
    press(within(drawer).getByRole("button", { name: "Save application" }));

    await waitFor(() => {
      expect(puts).toHaveLength(1);
    });
    expect(puts[0]?.dependsOn).toEqual(["a3", "a2"]);
  });

  it("offers their copy or keeps this one when someone else saved the application meanwhile", async () => {
    const theirs = applicationView({ id: "a1", version: "24.10", revision: 2, updatedBy: "anna" });
    const puts: SaveApplicationRequest[] = [];
    await open([sevenZip], {
      "PUT /api/applications/a1": (request) => {
        const body = request.body as SaveApplicationRequest;
        puts.push(body);

        return body.revision === 1 ? json(theirs, 409) : json(saved("a1", body, 3));
      },
    });

    const drawer = await drawerOf("7-Zip");
    fill(within(drawer).getByRole("textbox", { name: "Description" }), "Packs and unpacks");
    press(within(drawer).getByRole("button", { name: "Save application" }));

    expect(
      await within(drawer).findByText("anna saved this application while you were editing it."),
    ).toBeInTheDocument();
    press(within(drawer).getByRole("button", { name: "Keep mine" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(puts.map((body) => [body.revision, body.description])).toEqual([
      [1, "Packs and unpacks"],
      [2, "Packs and unpacks"],
    ]);
  });

  it("takes their copy into the form when asked", async () => {
    const theirs = applicationView({ id: "a1", version: "24.10", revision: 2, updatedBy: "anna" });
    await open([sevenZip], {
      "PUT /api/applications/a1": () => json(theirs, 409),
    });

    const drawer = await drawerOf("7-Zip");
    fill(within(drawer).getByRole("textbox", { name: "Version" }), "25.00");
    press(within(drawer).getByRole("button", { name: "Save application" }));
    press(await within(drawer).findByRole("button", { name: "Use theirs" }));

    expect(within(drawer).getByRole("textbox", { name: "Version" })).toHaveValue("24.10");
    expect(
      within(drawer).queryByText("anna saved this application while you were editing it."),
    ).not.toBeInTheDocument();
  });

  it("says why the server refused to replace files machines are installing", async () => {
    await open([sevenZip], {
      "PUT /api/applications/a1": () =>
        json(
          {
            title:
              "Machines are installing these files or are waiting to. Cancel those runs or let them finish first.",
            code: "application.filesInUse",
          },
          409,
        ),
    });

    const drawer = await drawerOf("7-Zip");
    press(within(drawer).getByRole("button", { name: "Remove" }));
    press(within(drawer).getByRole("button", { name: "Save application" }));

    expect(
      await within(drawer).findByText(
        "Machines are installing these files or are waiting to. Cancel those runs or let them finish first.",
      ),
    ).toBeInTheDocument();
    expect(within(drawer).queryByRole("button", { name: "Keep mine" })).not.toBeInTheDocument();
  });

  it("has no violations with an application's drawer open", async () => {
    await open([sevenZip]);

    await drawerOf("7-Zip");
    await expectNoAxeViolations();
  });
});
