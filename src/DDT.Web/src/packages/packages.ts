// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiGet } from "@/lib/api";
import type { HardwareModel } from "@/machines/machines";

// Drivers go to the machines whose model a target names. Files are unpacked for a Run script step that names
// them.
export type PackageKind = "Drivers" | "Files";

// expandedBytes is what the files take once unpacked.
export interface PackageSummary {
  id: string;
  name: string;
  kind: PackageKind;
  sha256: string;
  sizeBytes: number;
  expandedBytes: number;
  fileCount: number;
  targets: HardwareModel[];
  description: string | null;
  originalFileName: string | null;
  uploadedUtc: string;
  uploadedBy: string | null;
}

export const packagesQuery = queryOptions({
  queryKey: ["packages"],
  queryFn: () => apiGet<PackageSummary[]>("/api/packages"),
});
