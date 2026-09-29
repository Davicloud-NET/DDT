// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";

import { formatBytes } from "@/lib/format";
import type { UploadOutcome } from "@/uploads/resumableUpload";

import { describeTarget, type PackageSummary } from "./packages";

export function describeResult(file: string, outcome: UploadOutcome, item: PackageSummary): string {
  const name = item.name;

  return outcome === "duplicate"
    ? t`${file} is already in the library as ${name}.`
    : t`Added ${name} from ${file}.`;
}

export function packageContents(item: PackageSummary): string {
  const unpacked = formatBytes(item.expandedBytes);

  const files = item.fileCount;

  return plural(files, {
    one: `# file, ${unpacked} unpacked`,
    other: `# files, ${unpacked} unpacked`,
  });
}

export function matchesPackage(item: PackageSummary, needle: string): boolean {
  return [
    item.name,
    item.description,
    item.originalFileName,
    item.uploadedBy,
    ...item.targets.map(describeTarget),
  ].some((value) => value?.toLowerCase().includes(needle) === true);
}
