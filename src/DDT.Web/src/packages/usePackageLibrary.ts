// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQueries, useQuery } from "@tanstack/react-query";

import { modelsQuery } from "@/machines/machines";
import { sequenceQuery, sequencesQuery, type SequenceView } from "@/sequences/sequences";

import { matchingMachines, packagesQuery, sequencesUsing, type PackageSummary } from "./packages";

export type PackageLibrary = ReturnType<typeof usePackageLibrary>;

// The packages and what uses them: the models machines reported, and every sequence's steps. The sequence
// list doesn't carry the steps, but a library only holds a few sequences, so each one is read.
export function usePackageLibrary() {
  const packages = useQuery(packagesQuery);
  const models = useQuery(modelsQuery);
  const sequences = useQuery(sequencesQuery);
  const documents = useQueries({
    queries: (sequences.data ?? []).map((sequence) => sequenceQuery(sequence.id)),
  });

  const views = documents
    .map((document) => document.data)
    .filter((view): view is SequenceView => view !== undefined);
  const allRead = sequences.isSuccess && documents.every((document) => document.isSuccess);
  const modelList = models.data ?? [];

  return {
    packages,
    models: modelList,
    usersOf: (item: PackageSummary): SequenceView[] | null =>
      allRead ? sequencesUsing(item, views) : null,
    matchesOf: (item: PackageSummary) => matchingMachines(item.targets, modelList),
  };
}
