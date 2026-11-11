// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import type { DetectionKind } from "./applicationEdit";
import type {
  ApplicationBundleView,
  ApplicationKind,
  ApplicationUseKind,
  ApplicationView,
} from "./applications";

export const applicationKinds: readonly ApplicationKind[] = ["Command", "Msix", "Winget"];

export function kindLabel(kind: ApplicationKind): string {
  switch (kind) {
    case "Command":
      return t`Command`;
    case "Msix":
      return t`MSIX package`;
    case "Winget":
      return t`winget package`;
  }
}

export const detectionKinds: readonly DetectionKind[] = [
  "none",
  "productCode",
  "uninstallKey",
  "file",
];

export function detectionLabel(kind: DetectionKind): string {
  switch (kind) {
    case "none":
      return t`None`;
    case "productCode":
      return t`MSI product code`;
    case "uninstallKey":
      return t`Uninstall key`;
    case "file":
      return t`File`;
  }
}

export function usageLabel(kind: ApplicationUseKind): string {
  switch (kind) {
    case "Application":
      return t`Application`;
    case "Bundle":
      return t`Bundle`;
    case "Sequence":
      return t`Sequence`;
  }
}

export function deletionConsequence(application: ApplicationView): string {
  const name = application.name;

  return application.files === null
    ? t`${name} is deleted from the library.`
    : t`${name} and its files are deleted from the library.`;
}

export function bundleDeletionConsequence(bundle: ApplicationBundleView): string {
  const name = bundle.name;

  return t`The bundle ${name} is deleted. Its applications stay in the library.`;
}

export function matchesApplication(application: ApplicationView, needle: string): boolean {
  return [
    application.name,
    application.publisher,
    application.version,
    application.description,
    application.wingetId,
    application.files?.name ?? null,
    kindLabel(application.kind),
  ].some((value) => value?.toLowerCase().includes(needle) === true);
}

// A bundle matches by its own name and description, and by the names of its applications.
export function matchesBundle(
  bundle: ApplicationBundleView,
  needle: string,
  nameOf: (id: string) => string | null,
): boolean {
  return [bundle.name, bundle.description, ...bundle.members.map(nameOf)].some(
    (value) => value?.toLowerCase().includes(needle) === true,
  );
}
