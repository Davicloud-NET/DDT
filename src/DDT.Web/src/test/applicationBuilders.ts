// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type {
  ApplicationBundleView,
  ApplicationUpload,
  ApplicationView,
} from "@/applications/applications";

import type { Routes } from "./server";

// 7-Zip from its MSI, which nothing names.
export function applicationView(overrides: Partial<ApplicationView> = {}): ApplicationView {
  return {
    id: "0197a3c0-0000-7000-8000-000000000001",
    name: "7-Zip",
    description: null,
    publisher: "Igor Pavlov",
    version: "24.09",
    kind: "Command",
    files: {
      packageId: "0197a3c0-0000-7000-8000-0000000000f1",
      name: "7z2409-x64.msi",
      sizeBytes: 1_617_920,
    },
    command: "msiexec /i 7z2409-x64.msi /qn /norestart",
    successExitCodes: [0],
    restartExitCodes: [3010],
    detection: { kind: "productCode", productCode: "{23170F69-40C1-2702-2409-000001000000}" },
    dependsOn: [],
    wingetId: null,
    wingetVersion: null,
    timeoutMinutes: 60,
    continueOnFailure: false,
    usedBy: [],
    revision: 1,
    updatedUtc: "2026-11-11T10:00:00Z",
    updatedBy: "admin",
    ...overrides,
  };
}

export function bundleView(overrides: Partial<ApplicationBundleView> = {}): ApplicationBundleView {
  return {
    id: "0197a3c0-0000-7000-8000-0000000000b1",
    name: "Office basics",
    description: null,
    members: [],
    usedBy: [],
    revision: 1,
    updatedUtc: "2026-11-11T10:00:00Z",
    updatedBy: "admin",
    ...overrides,
  };
}

// A server that takes one small file in one slice and answers with the upload it became.
export function applicationUploadOf(file: string, upload: ApplicationUpload): Routes {
  const session = {
    id: "0197a3c0-0000-7000-8000-0000000000c9",
    fileName: file,
    length: 4,
    lastModified: 1_000,
    offset: 0,
    chunkBytes: 8,
    kind: "Application",
  };

  return {
    "POST /api/images/uploads": { status: 201, body: session },
    [`PATCH /api/images/uploads/${session.id}`]: () =>
      new Response(null, { status: 204, headers: { "Upload-Offset": "4" } }),
    [`POST /api/images/uploads/${session.id}/complete`]: { status: 201, body: upload },
  };
}
