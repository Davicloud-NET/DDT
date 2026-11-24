// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import type { ImportFile } from "@/images/import/imports";
import type { UploadOutcome } from "@/uploads/resumableUpload";

import type { PackageSummary } from "./packages";
import { describeResult } from "./packageView";

export function isLanguageIso(result: PackageSummary | ImportFile): result is ImportFile {
  return "path" in result;
}

// An uploaded ISO is not a package yet: its languages are chosen next.
export function describeWindowsResult(
  file: string,
  outcome: UploadOutcome,
  result: PackageSummary | ImportFile,
): string {
  return isLanguageIso(result)
    ? t`${file} is on the server. Choose its languages.`
    : describeResult(file, outcome, result);
}
