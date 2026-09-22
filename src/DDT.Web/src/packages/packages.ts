// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiDelete, apiGet, apiPut } from "@/lib/api";
import { formatBytes, plural } from "@/lib/format";
import type { HardwareModel, HardwareModelCount } from "@/machines/machines";
import type { SequenceView } from "@/sequences/sequences";

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

// The last save wins: the server keeps no revision of a package.
export interface UpdatePackageRequest {
  name: string;
  description: string | null;
  targets: HardwareModel[];
}

export const packagesQuery = queryOptions({
  queryKey: ["packages"],
  queryFn: () => apiGet<PackageSummary[]>("/api/packages"),
});

export function updatePackage(id: string, request: UpdatePackageRequest): Promise<PackageSummary> {
  return apiPut<PackageSummary>(`/api/packages/${id}`, request);
}

// Refused with 409 while a machine is assigned or runs a sequence that uses the package.
export function deletePackage(id: string): Promise<void> {
  return apiDelete(`/api/packages/${id}`);
}

// The server's HardwareModels: trimmed, runs of white space as one, compared in upper case.
function normalized(value: string | null): string | null {
  const cleaned = value?.trim().split(/\s+/).join(" ") ?? "";

  return cleaned === "" ? null : cleaned.toUpperCase();
}

// A null pattern matches anything, and a pattern ending in * every value that starts with the text before it.
export function matchesModel(pattern: string | null, value: string | null): boolean {
  const expected = normalized(pattern);

  if (expected === null) {
    return true;
  }

  const actual = normalized(value);

  if (actual === null) {
    return false;
  }

  return expected.endsWith("*") ? actual.startsWith(expected.slice(0, -1)) : actual === expected;
}

// How many registered machines report a model one of the targets names, for information: the server matches
// again when a sequence is assigned.
export function matchingMachines(
  targets: readonly HardwareModel[],
  models: readonly HardwareModelCount[],
): number {
  return models
    .filter((model) =>
      targets.some(
        (target) =>
          matchesModel(target.manufacturer, model.manufacturer) &&
          matchesModel(target.model, model.model),
      ),
    )
    .reduce((sum, model) => sum + model.machines, 0);
}

// The sequences that give the package to machines: a Run script step that names a Files package, or an Inject
// drivers step, which adds every driver package whose targets match the machine.
export function sequencesUsing(
  item: PackageSummary,
  sequences: readonly SequenceView[],
): SequenceView[] {
  return sequences.filter((sequence) =>
    sequence.definition.steps.some((step) =>
      item.kind === "Files"
        ? step.kind === "runScript" && step.packageId === item.id
        : step.kind === "injectDrivers",
    ),
  );
}

export function describeTarget(target: HardwareModel): string {
  return target.manufacturer === null ? target.model : `${target.manufacturer} ${target.model}`;
}

export function deletionConsequence(item: PackageSummary, users: readonly SequenceView[]): string {
  const names = users.map((sequence) => sequence.name).join(", ");
  const sentences = [`${item.name} (${formatBytes(item.sizeBytes)}) is deleted from the library.`];

  if (item.kind === "Files" && users.length === 0) {
    sentences.push("No sequence names it.");
  } else if (item.kind === "Files" && users.length === 1) {
    sentences.push(
      `The sequence ${names} names it in a Run script step and shows a problem until another package is chosen.`,
    );
  } else if (item.kind === "Files") {
    sentences.push(
      `The ${plural(users.length, "sequence")} ${names} name it in a Run script step and show a problem until another package is chosen.`,
    );
  } else if (item.targets.length > 0) {
    sentences.push(
      `Machines of ${item.targets.map(describeTarget).join(", ")} no longer get these drivers${users.length === 0 ? "" : ` from the Inject drivers step of ${names}`}.`,
    );
  }

  sentences.push("The server refuses while a machine is assigned or runs a sequence that uses it.");

  return sentences.join(" ");
}
