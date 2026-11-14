// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { packageSummary } from "@/test/builders";

// A month's cumulative update with its checkpoint, two languages from the ISO and a feature on demand, and the ISO in
// the import folder.

const isoPath =
  "D:\\var\\lib\\ddt\\import\\26100.1.240331-1435.ge_release_amd64fre_CLIENT_LOF_PACKAGES_OEM.iso";

const windowsPackages = [
  packageSummary({
    id: "0193a4b2-0000-7000-8000-0000000000d1",
    name: "2026-11 cumulative update for 25H2",
    kind: "WindowsPackage",
    targets: [],
    sizeBytes: 5_264_574_311,
    expandedBytes: 5_264_574_311,
    fileCount: 2,
    originalFileName: "2026-11 cumulative update.zip",
    uploadedUtc: "2026-11-13T09:12:00Z",
    windows: {
      content: "Update",
      languages: [],
      architecture: "x64",
      kbNumbers: [5132061, 5043080],
    },
  }),
  packageSummary({
    id: "0193a4b2-0000-7000-8000-0000000000d2",
    name: "de-DE language pack (26100)",
    kind: "WindowsPackage",
    targets: [],
    sizeBytes: 219_427_712,
    expandedBytes: 219_427_712,
    fileCount: 6,
    description: "From 26100.1.240331-1435.ge_release_amd64fre_CLIENT_LOF_PACKAGES_OEM.iso.",
    uploadedUtc: "2026-11-14T04:40:00Z",
    windows: { content: "Language", languages: ["de-DE"], architecture: "x64", kbNumbers: [] },
  }),
  packageSummary({
    id: "0193a4b2-0000-7000-8000-0000000000d3",
    name: "ja-JP language pack (26100)",
    kind: "WindowsPackage",
    targets: [],
    sizeBytes: 301_118_208,
    expandedBytes: 301_118_208,
    fileCount: 7,
    description: "From 26100.1.240331-1435.ge_release_amd64fre_CLIENT_LOF_PACKAGES_OEM.iso.",
    uploadedUtc: "2026-11-14T04:41:00Z",
    windows: { content: "Language", languages: ["ja-JP"], architecture: "x64", kbNumbers: [] },
  }),
  packageSummary({
    id: "0193a4b2-0000-7000-8000-0000000000d4",
    name: "WMIC",
    kind: "WindowsPackage",
    targets: [],
    sizeBytes: 1_904_640,
    expandedBytes: 1_904_640,
    fileCount: 2,
    originalFileName: "wmic.zip",
    uploadedUtc: "2026-11-10T15:02:00Z",
    windows: { content: "Features", languages: ["en-US"], architecture: "x64", kbNumbers: [] },
  }),
];

export const windowsPackageAnswers = {
  "GET /api/packages": windowsPackages,
  "GET /api/machines/models": [],
  "GET /api/sequences": [],
  "GET /api/images/uploads": [],
  "GET /api/images/import": {
    folders: [{ path: "D:\\var\\lib\\ddt\\import", default: true }],
    files: [
      {
        path: isoPath,
        name: "26100.1.240331-1435.ge_release_amd64fre_CLIENT_LOF_PACKAGES_OEM.iso",
        sizeBytes: 6_251_841_536,
        modifiedUtc: "2026-11-14T04:20:00Z",
      },
    ],
    shares: [],
    job: null,
  },
};
