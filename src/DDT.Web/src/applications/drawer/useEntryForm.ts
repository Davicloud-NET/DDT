// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { ApiError } from "@/lib/api";
import { conflictOf, noFindings, otherRefusal, refusalFindings, unplaced } from "@/rules/refusals";
import type { Findings } from "@/sequences/problems";

interface Entry {
  id: string;
  revision: number;
  updatedBy: string | null;
}

export interface EntryFormOptions<TView extends Entry, TEdit> {
  // Null for a new entry.
  item: TView | null;
  // What the form starts with.
  seed: TEdit;
  editOf: (item: TView) => TEdit;
  // Creates the entry when base is null.
  save: (base: TView | null, revision: number, edit: TEdit) => Promise<TView>;
  put: (queryClient: QueryClient, saved: TView) => void;
  // The fields the form shows for an edit; problems at others go to a notice.
  isShown: (edit: TEdit) => (field: string) => boolean;
  onClose: () => void;
}

export type EntryForm<TView extends Entry, TEdit> = ReturnType<typeof useEntryForm<TView, TEdit>>;

// Holds the edits to an application or a bundle and saves them. A save over a newer revision gets the entry as it is
// now, and the person takes theirs or keeps their own.
export function useEntryForm<TView extends Entry, TEdit>(options: EntryFormOptions<TView, TEdit>) {
  const { item, seed, editOf, save: send, put, isShown, onClose } = options;
  const queryClient = useQueryClient();
  const [base, setBase] = useState<TView | null>(item);
  const [edit, setEdit] = useState<TEdit>(seed);
  const [findings, setFindings] = useState<Findings>(noFindings);
  const [theirs, setTheirs] = useState<TView | null>(null);
  const [gone, setGone] = useState(false);

  const change = (patch: Partial<TEdit>) => {
    setEdit((current) => ({ ...current, ...patch }));
  };

  const save = useMutation({
    mutationFn: (revision: number) => send(base, revision, edit),
    onSuccess: (saved) => {
      put(queryClient, saved);
      onClose();
    },
    onError: (error) => {
      const current = conflictOf(error) as TView | null;

      if (current !== null) {
        put(queryClient, current);
        setTheirs(current);
        return;
      }

      if (error instanceof ApiError && error.status === 404) {
        setGone(true);
        return;
      }

      setFindings(refusalFindings(error) ?? noFindings);
    },
  });

  return {
    base,
    edit,
    findings,
    theirs,
    gone,
    change,
    busy: save.isPending,
    loose: unplaced(findings, isShown(edit)),
    refused: save.isError ? otherRefusal(save.error, gone) : null,
    submit: () => {
      setFindings(noFindings);
      save.mutate(base?.revision ?? 0);
    },
    takeTheirs: () => {
      if (theirs !== null) {
        setBase(theirs);
        setEdit(editOf(theirs));
        setFindings(noFindings);
        setTheirs(null);
        save.reset();
      }
    },
    keepMine: () => {
      if (theirs !== null) {
        setBase(theirs);
        setTheirs(null);
        save.mutate(theirs.revision);
      }
    },
  };
}
