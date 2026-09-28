// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { Form } from "react-aria-components";

import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";
import { Notice } from "@/ui/Notice";
import { TextField } from "@/ui/TextField";

import { TemplateSelect } from "./list/TemplateSelect";
import { useNewSequenceForm } from "./list/useNewSequenceForm";

// Creates a sequence, empty or from a template, and opens it in the editor, where it's edited in place.
export function NewSequenceDialog({
  taken,
  onClose,
}: {
  // The names already in use, so the new sequence gets a unique one.
  taken: readonly string[];
  onClose: () => void;
}) {
  const form = useNewSequenceForm(taken, onClose);
  const { create, templates, name, error } = form;
  const formId = "new-sequence";

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
          form.submit();
        }}
      >
        <TemplateSelect
          templates={form.list}
          chosen={form.chosen}
          template={form.template}
          isDisabled={templates.isPending}
          onChange={form.setChoice}
        />

        <TextField
          label={<Trans>Name</Trans>}
          value={name}
          onChange={(text) => {
            form.setTypedName(text);
            create.reset();
          }}
          isRequired
          autoComplete="off"
          isInvalid={form.nameProblem !== null}
          errorMessage={form.nameProblem}
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
