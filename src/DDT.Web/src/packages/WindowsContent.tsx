// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { PackageSummary } from "./packages";
import { windowsContentLabel, windowsDetails } from "./windowsPackageView";

// What a Windows package holds, by its file names: the kind, then its languages, KB numbers and architecture.
export function WindowsContent({ item }: { item: PackageSummary }) {
  const facts = item.windows ?? null;

  if (facts === null) {
    return null;
  }

  const details = windowsDetails(facts);

  return (
    <span className="flex min-w-0 flex-col">
      <span className="text-ink">{windowsContentLabel(facts.content)}</span>
      {details === "" ? null : <span className="truncate text-muted">{details}</span>}
    </span>
  );
}
