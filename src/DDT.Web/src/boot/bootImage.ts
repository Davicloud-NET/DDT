// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiGet, apiPost } from "@/lib/api";

import type { BootImageJob } from "./bootImageJob";

export interface BootImageDriver {
  packageId: string;
  name: string;
  sha256: string;
}

// What ddt-boot-image.json says the served build put into boot.wim, and what it was built for. A build script that
// recorded none of the last four leaves them null.
export interface BootImageBuild {
  builtUtc: string;
  driverSetHash: string | null;
  drivers: BootImageDriver[];
  adkVersion: string | null;
  bootManager: string | null;
  agentVersion: string | null;
  serverUrl: string | null;
  rootSha256: string | null;
  keyboardLayout: string | null;
  powerShell: boolean | null;
}

// Why the boot image has to be built again.
export type StaleReason = "drivers" | "serverAddress" | "root" | "adk";

// The Windows ADK on the server. supported is false for one older than the boot image needs.
export interface BootImageAdk {
  installed: boolean;
  version: string | null;
  supported: boolean;
}

// Whether the server builds the image itself, which takes the DDT Helper service of a Windows install. serverUrl is the
// address a build puts into the image. adk is null on a server that is not Windows.
export interface BootImageBuilder {
  available: boolean;
  serverUrl: string;
  adk: BootImageAdk | null;
}

// A build in the boot directory. name is null for the files in the boot directory itself, as a build by hand leaves
// them.
export interface BootImageStoredBuild {
  name: string | null;
  builtUtc: string | null;
  current: boolean;
}

export interface BootImageView {
  drivers: (BootImageDriver & { sizeBytes: number })[];
  driverSetHash: string | null;
  build: BootImageBuild | null;
  stale: boolean;
  staleReasons: StaleReason[];
  builder: BootImageBuilder;
  // The job that runs, or the last one since the server started.
  job: BootImageJob | null;
  // Newest first.
  builds: BootImageStoredBuild[];
}

export const bootImageQuery = queryOptions({
  queryKey: ["boot-image"],
  queryFn: () => apiGet<BootImageView>("/api/boot-image"),
});

// keyboardLayout is null for the server's own.
export interface BuildRequest {
  keyboardLayout: string | null;
  skipPowerShell: boolean;
}

// Both answer at once. The job follows in the view and in the output the hub pushes.
export function startBuild(request: BuildRequest): Promise<void> {
  return apiPost("/api/boot-image/build", request);
}

export function installAdk(): Promise<void> {
  return apiPost("/api/boot-image/adk");
}

export function serveBuild(name: string | null): Promise<BootImageView> {
  return apiPost<BootImageView>("/api/boot-image/current", { name });
}

// The Build-BootImage.ps1 command line for a Windows PC, when the server cannot build. An API token lets the script
// download the flagged drivers. The root certificate is the one the agent pins.
export function buildCommand(serverUrl: string, withDrivers: boolean): string {
  return [
    ".\\build\\Build-BootImage.ps1",
    "-AgentPath .\\artifacts\\agent\\ddt-agent.exe",
    `-ServerUrl ${serverUrl}`,
    "-RootCertificatePath .\\ddt-root.pem",
    ...(withDrivers ? ["-ApiToken $env:DDT_API_TOKEN"] : []),
  ].join(" ");
}
