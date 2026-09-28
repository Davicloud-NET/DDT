// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useId, type ReactNode } from "react";
import { Form } from "react-aria-components";

import { ApiError } from "@/lib/api";
import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";
import { Notice } from "@/ui/Notice";
import type { ResolvedValue } from "@/values/values";

import { answerErrors, hasAnswerErrors, type AskedInput, type InputAnswer } from "./inputs";
import { InputsForm } from "./InputsForm";
import { useAnswers } from "./useAnswers";

// Asks a sequence's inputs before something runs, or for a run that waits for them. A refused answer shows under its
// field, any other refusal under the form, and the passwords typed are forgotten once the dialog closes.
export function InputsDialog({
  title,
  inputs,
  defaults = [],
  confirmLabel,
  isBusy,
  error,
  onSubmit,
  onClose,
  isConfirmDisabled = false,
  children,
}: {
  title: ReactNode;
  inputs: readonly AskedInput[];
  defaults?: readonly ResolvedValue[];
  confirmLabel: ReactNode;
  isBusy: boolean;
  error: Error | null;
  onSubmit: (answers: InputAnswer[]) => void;
  onClose: () => void;
  // Keeps the confirm key off for a reason the dialog's content explains.
  isConfirmDisabled?: boolean;
  // What the action does, and any choice that goes with it, above the fields.
  children?: ReactNode;
}) {
  const answers = useAnswers(inputs, defaults);
  const formId = useId();
  const refused = error instanceof ApiError ? error : null;
  const fieldErrors = answerErrors(refused);

  const close = () => {
    answers.forgetPasswords();
    onClose();
  };

  return (
    <Dialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          close();
        }
      }}
      title={title}
      isBusy={isBusy}
      width="lg"
      footer={
        <>
          <Button variant="secondary" isDisabled={isBusy} onPress={close}>
            <Trans>Cancel</Trans>
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="primary"
            isDisabled={isBusy || isConfirmDisabled}
          >
            {confirmLabel}
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

          const collected = answers.collect();

          if (collected !== null) {
            onSubmit(collected);
          }
        }}
      >
        {children}
        <InputsForm inputs={inputs} answers={answers} defaults={defaults} errors={fieldErrors} />
        {error !== null && !hasAnswerErrors(refused) ? (
          <Notice tone="fail">{error.message}</Notice>
        ) : null}
      </Form>
    </Dialog>
  );
}
