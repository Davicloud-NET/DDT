// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { QueryClient } from "@tanstack/react-query";
import { describe, expect, it } from "vitest";

import { applicationView, bundleView } from "@/test/applicationBuilders";

import {
  applicationEditOf,
  applicationRequestOf,
  isShownFor,
  uploadedApplicationEdit,
} from "./applicationEdit";
import {
  applicationBundlesQuery,
  applicationsQuery,
  removeApplications,
  removeBundles,
  upsertApplication,
  upsertBundle,
  type ApplicationView,
} from "./applications";

const files = { packageId: "p1", name: "7z2409-x64.msi", sizeBytes: 1_617_920 };

function names(list: readonly { name: string }[] | undefined): string[] | undefined {
  return list?.map((item) => item.name);
}

describe("the application cache", () => {
  it("puts a saved application in its place by name and replaces its older copy", () => {
    const queryClient = new QueryClient();
    queryClient.setQueryData(applicationsQuery.queryKey, [
      applicationView({ id: "a1", name: "7-Zip" }),
      applicationView({ id: "a2", name: "Notepad++" }),
    ]);

    upsertApplication(queryClient, applicationView({ id: "a3", name: "firefox" }));
    upsertApplication(queryClient, applicationView({ id: "a1", name: "Zoom" }));

    expect(names(queryClient.getQueryData(applicationsQuery.queryKey))).toEqual([
      "firefox",
      "Notepad++",
      "Zoom",
    ]);
  });

  it("drops removed applications and bundles, and leaves a list never read alone", () => {
    const queryClient = new QueryClient();
    queryClient.setQueryData(applicationBundlesQuery.queryKey, [
      bundleView({ id: "b1", name: "Office" }),
      bundleView({ id: "b2", name: "Lab" }),
    ]);

    removeBundles(queryClient, ["b1"]);
    removeApplications(queryClient, ["a1"]);
    upsertBundle(queryClient, bundleView({ id: "b3", name: "Kiosk" }));

    expect(names(queryClient.getQueryData(applicationBundlesQuery.queryKey))).toEqual([
      "Kiosk",
      "Lab",
    ]);
    expect(queryClient.getQueryData(applicationsQuery.queryKey)).toBeUndefined();
  });
});

describe("the application form", () => {
  it("fills a new application from what an uploaded MSI suggests", () => {
    const edit = uploadedApplicationEdit({
      files,
      suggestion: {
        kind: "Command",
        name: "7-Zip 24.09 (x64)",
        publisher: "Igor Pavlov",
        version: "24.09.00.0",
        command: 'msiexec /i "7z2409-x64.msi" /qn /norestart',
        successExitCodes: [0, 1641],
        restartExitCodes: [3010],
        detection: { kind: "productCode", productCode: "{23170F69-40C1-2702-2409-000001000000}" },
      },
    });

    expect(applicationRequestOf(0, edit)).toEqual({
      revision: 0,
      name: "7-Zip 24.09 (x64)",
      description: null,
      publisher: "Igor Pavlov",
      version: "24.09.00.0",
      kind: "Command",
      packageId: "p1",
      command: 'msiexec /i "7z2409-x64.msi" /qn /norestart',
      successExitCodes: [0, 1641],
      restartExitCodes: [3010],
      detection: { kind: "productCode", productCode: "{23170F69-40C1-2702-2409-000001000000}" },
      dependsOn: [],
      wingetId: null,
      wingetVersion: null,
      timeoutMinutes: 60,
      continueOnFailure: false,
    });
  });

  it("takes the kind the server read in the upload, not the file's name", () => {
    const msix = uploadedApplicationEdit({
      files: { ...files, name: "contoso.zip" },
      suggestion: {
        kind: "Msix",
        name: "Contoso App",
        publisher: null,
        version: "1.2.3.0",
        command: null,
        successExitCodes: [0],
        restartExitCodes: [3010],
        detection: {
          kind: "packageFamily",
          familyName: "Contoso.App_8wekyb3d8bbwe",
          minimumVersion: "1.2.3.0",
        },
      },
    });
    const unknown = uploadedApplicationEdit({
      files: { ...files, name: "app.msix" },
      suggestion: null,
    });

    expect(applicationRequestOf(0, msix)).toMatchObject({
      kind: "Msix",
      packageId: "p1",
      command: null,
      restartExitCodes: [3010],
      detection: {
        kind: "packageFamily",
        familyName: "Contoso.App_8wekyb3d8bbwe",
        minimumVersion: "1.2.3.0",
      },
    });
    expect(unknown.kind).toBe("Command");
    expect(unknown.files?.packageId).toBe("p1");
    expect(unknown.name).toBe("");
  });

  it("sends only what the chosen kind uses", () => {
    const edit = {
      ...applicationEditOf(applicationView()),
      wingetId: " Mozilla.Firefox ",
      wingetVersion: "",
    };

    expect(applicationRequestOf(1, { ...edit, kind: "Winget" })).toMatchObject({
      kind: "Winget",
      packageId: null,
      command: null,
      wingetId: "Mozilla.Firefox",
      wingetVersion: null,
    });
    expect(applicationRequestOf(1, { ...edit, kind: "Msix" })).toMatchObject({
      kind: "Msix",
      packageId: "0197a3c0-0000-7000-8000-0000000000f1",
      command: null,
      wingetId: null,
    });
  });

  it("keeps every detection's text while another is chosen, and sends only the chosen one", () => {
    const application: ApplicationView = applicationView({
      detection: { kind: "file", path: "%ProgramFiles%\\7-Zip\\7z.exe", minimumVersion: null },
    });
    const edit = applicationEditOf(application);
    const typed = {
      ...edit,
      detection: { ...edit.detection, kind: "uninstallKey" as const, keyName: " 7-Zip " },
    };

    expect(applicationRequestOf(1, typed).detection).toEqual({
      kind: "uninstallKey",
      keyName: "7-Zip",
    });
    expect(
      applicationRequestOf(1, { ...typed, detection: { ...typed.detection, kind: "file" } })
        .detection,
    ).toEqual({ kind: "file", path: "%ProgramFiles%\\7-Zip\\7z.exe", minimumVersion: null });
    expect(
      applicationRequestOf(1, { ...typed, detection: { ...typed.detection, kind: "none" } })
        .detection,
    ).toBeNull();
    expect(
      applicationRequestOf(1, {
        ...typed,
        detection: {
          ...typed.detection,
          kind: "packageFamily",
          familyName: " Contoso.App_8wekyb3d8bbwe ",
        },
      }).detection,
    ).toEqual({
      kind: "packageFamily",
      familyName: "Contoso.App_8wekyb3d8bbwe",
      minimumVersion: null,
    });
  });

  it("shows a kind's own fields and puts the others' problems in the notice", () => {
    const winget = isShownFor("Winget");
    const command = isShownFor("Command");

    expect(["wingetId", "detection.path", "dependsOn[2]"].every(winget)).toBe(true);
    expect(winget("command")).toBe(false);
    expect(winget("packageId")).toBe(false);
    expect(command("command")).toBe(true);
    expect(command("wingetVersion")).toBe(false);
  });
});
