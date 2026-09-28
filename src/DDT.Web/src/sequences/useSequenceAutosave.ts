// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { ApiError } from "@/lib/api";
import { useAutosave } from "@/lib/useAutosave";

import { draftOf, sameDraft, saveRequestOf, type SequenceDraft } from "./sequenceDraft";
import { upsertSummary } from "./sequenceList";
import { saveSequence, sequenceQuery, type SequenceView } from "./sequences";

// A 409 comes with the server's copy, so the page doesn't have to fetch it again.
function isView(body: unknown): body is SequenceView {
  return (
    typeof body === "object" &&
    body !== null &&
    "id" in body &&
    "revision" in body &&
    "definition" in body
  );
}

// Saves a sequence's draft in place and puts each saved copy into the cache. ownRevisions are the revisions this
// page's own saves made. They tell a copy the hub brings in apart from someone else's.
export function useSequenceAutosave(initial: SequenceView, onTakenIn: () => void) {
  const queryClient = useQueryClient();
  const id = initial.id;
  const [ownRevisions, setOwnRevisions] = useState<readonly number[]>([]);

  const autosave = useAutosave<SequenceDraft, SequenceView>({
    initial: { value: draftOf(initial), revision: initial.revision },
    save: async (draft, revision, keepalive) => {
      try {
        return await saveSequence(id, saveRequestOf(draft, revision), keepalive);
      } catch (error) {
        // Someone else saved first. Their copy comes with the refusal. It's only fetched if the refusal lacks it.
        if (error instanceof ApiError && error.status === 409) {
          if (isView(error.problem)) {
            queryClient.setQueryData(sequenceQuery(id).queryKey, error.problem);
          } else {
            void queryClient.invalidateQueries({ queryKey: sequenceQuery(id).queryKey });
          }
        }

        throw error;
      }
    },
    savedAs: (view) => ({ value: draftOf(view), revision: view.revision }),
    equals: sameDraft,
    conflicts: true,
    onSaved: (view) => {
      setOwnRevisions((revisions) => [...revisions, view.revision]);
      queryClient.setQueryData(sequenceQuery(id).queryKey, view);
      upsertSummary(queryClient, view);
    },
    onTakenIn,
  });

  return { autosave, ownRevisions };
}
