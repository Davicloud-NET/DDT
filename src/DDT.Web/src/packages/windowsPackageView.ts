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

// "de-DE · KB5132061, KB5043080 · x64 · builds 26100, 26200", leaving out what the package has none of.
export function windowsDetails(facts: WindowsPackageFacts): string {
  const builds = facts.builds.join(", ");

  return [
    facts.languages.join(", "),
    facts.kbNumbers.map((kb) => `KB${String(kb)}`).join(", "),
    facts.architecture ?? "",
    builds === "" ? "" : t`builds ${builds}`,
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
        ...facts.builds.map(String),
      ];
}

// "26100, 26200 22631" as the builds 26100, 26200 and 22631. Null when a part is no number.
export function parseBuilds(text: string): number[] | null {
  const parts = text.split(/[\s,;]+/).filter((part) => part !== "");

  return parts.every((part) => /^\d{1,6}$/.test(part)) ? parts.map(Number) : null;
}
