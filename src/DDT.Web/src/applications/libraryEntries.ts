// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ApplicationReference } from "@/sequences/sequences";

import type { ApplicationBundleView, ApplicationView } from "./applications";

// An application or a bundle of the library, as a step or an input picks it.
export interface LibraryEntry extends ApplicationReference {
  name: string;
}

// Bundles first, then applications, each in the server's order.
export function libraryEntries(
  applications: readonly ApplicationView[],
  bundles: readonly ApplicationBundleView[],
): LibraryEntry[] {
  return [
    ...bundles.map((bundle) => ({ id: bundle.id, kind: "Bundle" as const, name: bundle.name })),
    ...applications.map((application) => ({
      id: application.id,
      kind: "Application" as const,
      name: application.name,
    })),
  ];
}

// The entry a reference names, if it is still in the library as that kind.
export function entryOf(
  reference: ApplicationReference,
  entries: readonly LibraryEntry[],
): LibraryEntry | null {
  return (
    entries.find((entry) => entry.id === reference.id && entry.kind === reference.kind) ?? null
  );
}
