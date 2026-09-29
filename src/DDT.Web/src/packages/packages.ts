// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";
import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { apiDelete, apiGet, apiPut } from "@/lib/api";
import { formatBytes } from "@/lib/format";
import { removeByIds, upsertById } from "@/lib/listCache";
import type { HardwareModel, HardwareModelCount } from "@/machines/machines";
import type { SequenceView } from "@/sequences/sequences";

// Drivers go to machines whose model a target names. Files are unpacked for a Run script step naming them.
export type PackageKind = "Drivers" | "Files";

// expandedBytes is the size of the files once unpacked.
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
  // Drivers only: added to the Windows PE boot image at its next build.
  bootImage: boolean;
}

// The last save wins, because the server keeps no revision of a package.
export interface UpdatePackageRequest {
  name: string;
  description: string | null;
  targets: HardwareModel[];
  // Null leaves it as it is. Only drivers may go into the boot image.
  bootImage?: boolean | null;
}

export interface PackagesRemoved {
  packageIds: string[];
}

function byName(a: PackageSummary, b: PackageSummary): number {
  return a.name.localeCompare(b.name, undefined, { sensitivity: "base" });
}

export function upsertPackage(queryClient: QueryClient, item: PackageSummary): void {
  queryClient.setQueryData(packagesQuery.queryKey, (list) => upsertById(list, item, byName));
}

export function removePackages(queryClient: QueryClient, packageIds: readonly string[]): void {
  queryClient.setQueryData(packagesQuery.queryKey, (list) => removeByIds(list, packageIds));
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

// Works like the server's HardwareModels: values are trimmed, runs of white space become one space, and
// they're compared in upper case.
function normalized(value: string | null): string | null {
  const cleaned = value?.trim().split(/\s+/).join(" ") ?? "";

  return cleaned === "" ? null : cleaned.toUpperCase();
}

// A null pattern matches anything. A pattern ending in * matches values that start with the part before it.
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

// How many registered machines report a model that one of the targets names. It's only for information,
// because the server matches again when a sequence is assigned.
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

// The sequences that give the package to machines. A Files package needs a Run script step that names it.
// Drivers need any Inject drivers step, which adds every driver package whose targets match the machine.
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

// users is null until every sequence has been read.
export function deletionConsequence(
  item: PackageSummary,
  users: readonly SequenceView[] | null,
): string {
  const known = users ?? [];
  const names = known.map((sequence) => sequence.name).join(", ");
  const count = known.length;
  const name = item.name;
  const size = formatBytes(item.sizeBytes);
  const sentences = [t`${name} (${size}) is deleted from the library.`];

  if (item.kind === "Files" && users === null) {
    sentences.push(
      t`Which sequences name it is not known, because not every sequence could be read; those that do show a problem until another package is chosen.`,
    );
  } else if (item.kind === "Files" && count === 0) {
    sentences.push(t`No sequence names it.`);
  } else if (item.kind === "Files") {
    sentences.push(
      plural(count, {
        one: `The sequence ${names} names it in a Run script step and shows a problem until another package is chosen.`,
        other: `The sequences ${names} name it in a Run script step and show a problem until another package is chosen.`,
      }),
    );
  } else if (item.targets.length > 0) {
    const targets = item.targets.map(describeTarget).join(", ");

    sentences.push(
      count === 0
        ? t`Machines of ${targets} no longer get these drivers.`
        : t`Machines of ${targets} no longer get these drivers from the Inject drivers step of ${names}.`,
    );
  }

  sentences.push(
    t`The server refuses while a machine is assigned or runs a sequence that uses it.`,
  );

  return sentences.join(" ");
}
