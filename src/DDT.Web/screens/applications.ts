// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { applicationView, bundleView } from "@/test/applicationBuilders";

// An office's application library: an MSI with a dependency, a winget package, an MSIX package and a command without
// files, and a bundle that holds three of them.

const runtimeId = "0197a3c0-0000-7000-8000-000000000002";
const officeId = "0197a3c0-0000-7000-8000-0000000000b1";
const sequenceId = "0193a4b2-0000-7000-8000-0000000000e1";

export const applications = [
  applicationView({
    dependsOn: [runtimeId],
    usedBy: [
      { id: officeId, name: "Office basics", kind: "Bundle" },
      { id: sequenceId, name: "Windows 11 office PCs", kind: "Sequence" },
    ],
  }),
  applicationView({
    id: "0197a3c0-0000-7000-8000-000000000003",
    name: "Company Portal",
    publisher: "Microsoft Corporation",
    version: "11.2.1002.0",
    kind: "Msix",
    files: {
      packageId: "0197a3c0-0000-7000-8000-0000000000f3",
      name: "CompanyPortal.msixbundle",
      sizeBytes: 48_234_496,
    },
    command: null,
    restartExitCodes: [],
    detection: null,
  }),
  applicationView({
    id: "0197a3c0-0000-7000-8000-000000000004",
    name: "Firefox",
    description: "Kept current by winget.",
    publisher: "Mozilla",
    version: null,
    kind: "Winget",
    files: null,
    command: null,
    restartExitCodes: [],
    detection: null,
    wingetId: "Mozilla.Firefox",
    usedBy: [{ id: officeId, name: "Office basics", kind: "Bundle" }],
  }),
  applicationView({
    id: "0197a3c0-0000-7000-8000-000000000005",
    name: "Printer for the second floor",
    publisher: null,
    version: null,
    files: null,
    command: "rundll32 printui.dll,PrintUIEntry /ga /n\\\\print01\\floor2",
    detection: null,
  }),
  applicationView({
    id: runtimeId,
    name: "Visual C++ Redistributable",
    publisher: "Microsoft Corporation",
    version: "14.42.34433",
    files: {
      packageId: "0197a3c0-0000-7000-8000-0000000000f2",
      name: "vc_redist.x64.exe",
      sizeBytes: 25_635_568,
    },
    command: "vc_redist.x64.exe /install /quiet /norestart",
    detection: { kind: "uninstallKey", keyName: "{804e7d66-ccc2-4c12-84ba-476da31d103d}" },
    usedBy: [
      { id: "0197a3c0-0000-7000-8000-000000000001", name: "7-Zip", kind: "Application" },
      { id: officeId, name: "Office basics", kind: "Bundle" },
    ],
  }),
];

export const bundles = [
  bundleView({
    id: officeId,
    name: "Office basics",
    description: "Every office PC gets these.",
    members: [
      runtimeId,
      "0197a3c0-0000-7000-8000-000000000001",
      "0197a3c0-0000-7000-8000-000000000004",
    ],
    usedBy: [{ id: sequenceId, name: "Windows 11 office PCs", kind: "Sequence" }],
  }),
];
