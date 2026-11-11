// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { apiDelete, apiGet, apiPost, apiPut } from "@/lib/api";
import { removeByIds, upsertById } from "@/lib/listCache";

// Files and a silent command, an MSIX or Appx package provisioned for all users, or a package winget installs.
export type ApplicationKind = "Command" | "Msix" | "Winget";

// How the agent tells that an application is installed already. Null on the wire installs every time.
export type ApplicationDetection =
  | { kind: "productCode"; productCode: string }
  | { kind: "uninstallKey"; keyName: string }
  | { kind: "file"; path: string; minimumVersion?: string | null };

// The uploaded files, kept as a package the Packages page does not list.
export interface ApplicationFiles {
  packageId: string;
  name: string;
  sizeBytes: number;
}

export type ApplicationUseKind = "Application" | "Bundle" | "Sequence";

// Something that names an application or a bundle, so it cannot be deleted.
export interface ApplicationUse {
  id: string;
  name: string;
  kind: ApplicationUseKind;
}

export interface ApplicationView {
  id: string;
  name: string;
  description: string | null;
  publisher: string | null;
  version: string | null;
  kind: ApplicationKind;
  files: ApplicationFiles | null;
  command: string | null;
  successExitCodes: number[];
  restartExitCodes: number[];
  detection: ApplicationDetection | null;
  // Installed first, in this order.
  dependsOn: string[];
  wingetId: string | null;
  // Null is the latest.
  wingetVersion: string | null;
  timeoutMinutes: number;
  continueOnFailure: boolean;
  usedBy: ApplicationUse[];
  revision: number;
  updatedUtc: string;
  updatedBy: string | null;
}

// revision is the one the page last read; the server ignores it for a new application.
export interface SaveApplicationRequest {
  revision: number;
  name: string;
  description: string | null;
  publisher: string | null;
  version: string | null;
  kind: ApplicationKind;
  packageId: string | null;
  command: string | null;
  successExitCodes: number[];
  restartExitCodes: number[];
  detection: ApplicationDetection | null;
  dependsOn: string[];
  wingetId: string | null;
  wingetVersion: string | null;
  timeoutMinutes: number;
  continueOnFailure: boolean;
}

// A named list of applications that installs in its order.
export interface ApplicationBundleView {
  id: string;
  name: string;
  description: string | null;
  members: string[];
  usedBy: ApplicationUse[];
  revision: number;
  updatedUtc: string;
  updatedBy: string | null;
}

export interface SaveApplicationBundleRequest {
  revision: number;
  name: string;
  description: string | null;
  members: string[];
}

// What an uploaded MSI or known EXE installer suggests. detection is a product code, an uninstall key or null.
export interface ApplicationSuggestion {
  name: string | null;
  publisher: string | null;
  version: string | null;
  command: string;
  successExitCodes: number[];
  restartExitCodes: number[];
  detection: ApplicationDetection | null;
}

// The answer to a completed upload of the kind Application. Its files are swept after 24 hours unless saved.
export interface ApplicationUpload {
  files: ApplicationFiles;
  suggestion?: ApplicationSuggestion | null;
}

export interface ApplicationsRemoved {
  applicationIds: string[];
}

export interface ApplicationBundlesRemoved {
  bundleIds: string[];
}

// The server's ApplicationLimits that the form uses.
export const MIN_TIMEOUT_MINUTES = 1;
export const MAX_TIMEOUT_MINUTES = 24 * 60;
export const DEFAULT_TIMEOUT_MINUTES = 60;

export const applicationsQuery = queryOptions({
  queryKey: ["applications"],
  queryFn: () => apiGet<ApplicationView[]>("/api/applications"),
});

export const applicationBundlesQuery = queryOptions({
  queryKey: ["applicationBundles"],
  queryFn: () => apiGet<ApplicationBundleView[]>("/api/application-bundles"),
});

export function createApplication(request: SaveApplicationRequest): Promise<ApplicationView> {
  return apiPost<ApplicationView>("/api/applications", request);
}

// Answers 409 with the current application on an older revision, or a problem when its old files are in use.
export function saveApplication(
  id: string,
  request: SaveApplicationRequest,
): Promise<ApplicationView> {
  return apiPut<ApplicationView>(`/api/applications/${id}`, request);
}

// Refused with 409 while a bundle or another application names it, or a machine installs its files.
export function deleteApplication(id: string): Promise<void> {
  return apiDelete(`/api/applications/${id}`);
}

export function createBundle(
  request: SaveApplicationBundleRequest,
): Promise<ApplicationBundleView> {
  return apiPost<ApplicationBundleView>("/api/application-bundles", request);
}

export function saveBundle(
  id: string,
  request: SaveApplicationBundleRequest,
): Promise<ApplicationBundleView> {
  return apiPut<ApplicationBundleView>(`/api/application-bundles/${id}`, request);
}

export function deleteBundle(id: string): Promise<void> {
  return apiDelete(`/api/application-bundles/${id}`);
}

// The server's order: by name, ignoring case.
function byName(a: { name: string }, b: { name: string }): number {
  return a.name.localeCompare(b.name, undefined, { sensitivity: "base" });
}

export function upsertApplication(queryClient: QueryClient, application: ApplicationView): void {
  queryClient.setQueryData(applicationsQuery.queryKey, (list) =>
    upsertById(list, application, byName),
  );
}

export function removeApplications(queryClient: QueryClient, ids: readonly string[]): void {
  queryClient.setQueryData(applicationsQuery.queryKey, (list) => removeByIds(list, ids));
}

export function upsertBundle(queryClient: QueryClient, bundle: ApplicationBundleView): void {
  queryClient.setQueryData(applicationBundlesQuery.queryKey, (list) =>
    upsertById(list, bundle, byName),
  );
}

export function removeBundles(queryClient: QueryClient, ids: readonly string[]): void {
  queryClient.setQueryData(applicationBundlesQuery.queryKey, (list) => removeByIds(list, ids));
}
