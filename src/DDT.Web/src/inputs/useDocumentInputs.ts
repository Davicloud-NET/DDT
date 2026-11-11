// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";

import { applicationBundlesQuery, applicationsQuery } from "@/applications/applications";
import { libraryEntries } from "@/applications/libraryEntries";
import type { InputDeclaration } from "@/sequences/sequences";

import { webInputs } from "./inputs";

// A sequence document's inputs as the web asks them. The library is read only for an Applications input asked here,
// and known is false until it has been.
export function useDocumentInputs(declarations: readonly InputDeclaration[] | null | undefined) {
  const needed = (declarations ?? []).some(
    (input) => input.kind === "Applications" && input.askAt !== "Machine",
  );
  const applications = useQuery({ ...applicationsQuery, enabled: needed });
  const bundles = useQuery({ ...applicationBundlesQuery, enabled: needed });

  return {
    inputs: webInputs(declarations, libraryEntries(applications.data ?? [], bundles.data ?? [])),
    known: !needed || (!applications.isPending && !bundles.isPending),
  };
}
