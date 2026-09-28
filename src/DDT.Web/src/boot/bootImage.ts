// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiGet } from "@/lib/api";

export interface BootImageDriver {
  packageId: string;
  name: string;
  sha256: string;
}

// What the boot directory's ddt-boot-image.json says the last build put into boot.wim.
export interface BootImageBuild {
  builtUtc: string;
  driverSetHash: string | null;
  drivers: BootImageDriver[];
  adkVersion: string | null;
  bootManager: string | null;
  agentVersion: string | null;
}

// The driver packages flagged for Windows PE, and the last build. stale says that the build's drivers differ from
// the flagged ones, or that drivers are flagged and nothing was built yet.
export interface BootImageView {
  drivers: (BootImageDriver & { sizeBytes: number })[];
  driverSetHash: string | null;
  build: BootImageBuild | null;
  stale: boolean;
}

export const bootImageQuery = queryOptions({
  queryKey: ["boot-image"],
  queryFn: () => apiGet<BootImageView>("/api/boot-image"),
});

// Build-BootImage.ps1 with this server's address. An API token lets the script download the flagged drivers; the root
// certificate is the one the agent pins.
export function buildCommand(serverUrl: string, withDrivers: boolean): string {
  return [
    ".\\build\\Build-BootImage.ps1",
    "-AgentPath .\\artifacts\\agent\\ddt-agent.exe",
    `-ServerUrl ${serverUrl}`,
    "-RootCertificatePath .\\ddt-root.pem",
    ...(withDrivers ? ["-ApiToken $env:DDT_API_TOKEN"] : []),
  ].join(" ");
}
