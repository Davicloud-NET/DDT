// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { useState } from "react";

import { ApiError } from "@/lib/api";

import { uniqueName, upsertSummary, withNewStepIds } from "../sequenceList";
import {
  createSequence,
  SEQUENCE_VERSION,
  sequenceQuery,
  templatesQuery,
  type CreateSequenceRequest,
} from "../sequences";
import { EMPTY_CHOICE, templatesInLanguage } from "./sequenceTemplates";

// The new sequence's template and name, and creating it. The server's answer goes into the list and becomes the
// editor's first copy, so nothing has to be fetched again.
export function useNewSequenceForm(taken: readonly string[], onClose: () => void) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const templates = useQuery(templatesQuery);

  const [choice, setChoice] = useState<string | null>(null);
  // What was typed, once something was. Until then the name follows the chosen template.
  const [typedName, setTypedName] = useState<string | null>(null);

  const create = useMutation({
    mutationFn: (request: CreateSequenceRequest) => createSequence(request),
    onSuccess: (view) => {
      queryClient.setQueryData(sequenceQuery(view.id).queryKey, view);
      upsertSummary(queryClient, view);
      onClose();
      void navigate({ to: "/deployment/sequences/$sequenceId", params: { sequenceId: view.id } });
    },
  });

  const list = templatesInLanguage(templates.data ?? []);
  const chosen = choice ?? list[0]?.key ?? EMPTY_CHOICE;
  const template = list.find((candidate) => candidate.key === chosen) ?? null;
  const name = typedName ?? uniqueName(template?.name ?? t`New sequence`, taken);
  const nameProblem =
    create.error instanceof ApiError ? (create.error.problem?.errors?.name?.[0] ?? null) : null;
  const error = create.isError && nameProblem === null ? create.error.message : null;

  function submit() {
    if (name.trim() === "") {
      return;
    }

    create.mutate({
      name: name.trim(),
      description: template?.description ?? null,
      definition:
        template === null
          ? { version: SEQUENCE_VERSION, steps: [] }
          : withNewStepIds(template.definition),
    });
  }

  return {
    templates,
    create,
    list,
    chosen,
    template,
    name,
    nameProblem,
    error,
    setChoice,
    setTypedName,
    submit,
  };
}
