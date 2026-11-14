// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import type { PackageSummary, WindowsPackageContent, WindowsPackageFacts } from "./packages";

export function windowsContentLabel(content: WindowsPackageContent): string {
  switch (content) {
    case "Update":
      return t`Update`;
    case "Language":
      return t`Language`;
    case "Features":
      return t`Features on demand`;
    case "Mixed":
      return t`Updates and features`;
  }
}

// "de-DE · KB5132061, KB5043080 · x64", leaving out what the package has none of.
export function windowsDetails(facts: WindowsPackageFacts): string {
  return [
    facts.languages.join(", "),
    facts.kbNumbers.map((kb) => `KB${String(kb)}`).join(", "),
    facts.architecture ?? "",
  ]
    .filter((part) => part !== "")
    .join(" · ");
}

// The words a search finds a Windows package by.
export function windowsSearchTerms(item: PackageSummary): string[] {
  const facts = item.windows ?? null;

  return facts === null
    ? []
    : [
        windowsContentLabel(facts.content),
        ...facts.languages,
        ...facts.kbNumbers.map((kb) => `KB${String(kb)}`),
      ];
}
