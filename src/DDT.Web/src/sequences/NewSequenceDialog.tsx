// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { useState } from "react";
import { Form } from "react-aria-components";

import { ApiError } from "@/lib/api";
import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";
import { Notice } from "@/ui/Notice";
import { ListBoxItem, Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";

import { uniqueName, upsertSummary, withNewStepIds } from "./sequenceList";
import {
  createSequence,
  SEQUENCE_VERSION,
  sequenceQuery,
  templatesQuery,
  type CreateSequenceRequest,
} from "./sequences";

// The choice of an empty sequence; template keys are words such as "install-windows".
const EMPTY = "empty";

// Creates a sequence from a template or empty, and opens it in the editor, where it is changed in place. The server's
// answer goes into the list and becomes the editor's first copy, so nothing is read again.
export function NewSequenceDialog({
  taken,
  onClose,
}: {
  // The names in use, so the new sequence gets one of its own.
  taken: readonly string[];
  onClose: () => void;
}) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const templates = useQuery(templatesQuery);

  const [choice, setChoice] = useState<string | null>(null);
  // What was typed, once something was; until then the name follows the choice.
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

  const list = templates.data ?? [];
  const chosen = choice ?? list[0]?.key ?? EMPTY;
  const template = list.find((candidate) => candidate.key === chosen) ?? null;
  const name = typedName ?? uniqueName(template?.name ?? t`New sequence`, taken);
  const nameProblem =
    create.error instanceof ApiError ? (create.error.problem?.errors?.name?.[0] ?? null) : null;
  const error = create.isError && nameProblem === null ? create.error.message : null;
  const formId = "new-sequence";

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

  return (
    <Dialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={<Trans>New task sequence</Trans>}
      isBusy={create.isPending}
      footer={
        <>
          <Button variant="secondary" isDisabled={create.isPending} onPress={onClose}>
            <Trans>Cancel</Trans>
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="primary"
            isDisabled={create.isPending || name.trim() === "" || templates.isPending}
          >
            <Trans>Create and open</Trans>
          </Button>
        </>
      }
    >
      <Form
        id={formId}
        className="flex flex-col gap-4"
        validationBehavior="aria"
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }}
      >
        <Select
          label={<Trans>Start from</Trans>}
          value={chosen}
          isDisabled={templates.isPending}
          onChange={(key) => {
            setChoice(key === null ? null : String(key));
          }}
          hint={
            template?.description ?? (
              <Trans>An empty sequence, to which you add the steps in the editor.</Trans>
            )
          }
        >
          {list.map((candidate) => (
            <ListBoxItem
              key={candidate.key}
              id={candidate.key}
              textValue={candidate.name}
              description={candidate.description}
            >
              {candidate.name}
            </ListBoxItem>
          ))}
          <ListBoxItem id={EMPTY} textValue={t`Empty sequence`}>
            {t`Empty sequence`}
          </ListBoxItem>
        </Select>

        <TextField
          label={<Trans>Name</Trans>}
          value={name}
          onChange={(text) => {
            setTypedName(text);
            create.reset();
          }}
          isRequired
          autoComplete="off"
          isInvalid={nameProblem !== null}
          errorMessage={nameProblem}
          hint={<Trans>You can change it later in the editor.</Trans>}
        />

        {templates.isError ? (
          <Notice tone="attention">
            <Trans>
              The templates could not be loaded. You can still start with an empty sequence.
            </Trans>
          </Notice>
        ) : null}
        {error !== null ? (
          <Notice tone="fail">
            <Trans>The sequence was not created. {error}</Trans>
          </Notice>
        ) : null}
      </Form>
    </Dialog>
  );
}
