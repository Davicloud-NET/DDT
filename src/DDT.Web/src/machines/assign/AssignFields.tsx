// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useId } from "react";
import { Form } from "react-aria-components";

import type { MachineSummary } from "@/machines/machines";
import { resolutionText } from "@/rules/rules";
import { Checkbox } from "@/ui/Checkbox";
import { Notice } from "@/ui/Notice";
import { TextField } from "@/ui/TextField";

import { AssignAnswers } from "./AssignAnswers";
import { AssignBlockers } from "./AssignBlockers";
import { AssignConsequences } from "./AssignConsequences";
import { nameHint } from "./assignText";
import { SequencePicker } from "./SequencePicker";
import type { AssignFormState } from "./useAssignForm";

// The assign dialog's form. It has the sequence, the computer name, the answers and what the assignment does.
// It also has the checkbox that allows a raw disk image that may not boot with Secure Boot on.
export function AssignFields({
  id,
  machine,
  form,
}: {
  id: string;
  machine: MachineSummary;
  form: AssignFormState;
}) {
  const { t: translate } = useLingui();
  const warningId = useId();
  const { sequence, risk } = form;

  return (
    <Form
      id={id}
      className="flex flex-col gap-4"
      validationBehavior="aria"
      onSubmit={(event) => {
        event.preventDefault();
        form.submit();
      }}
    >
      <SequencePicker
        sequences={form.list}
        sequence={sequence}
        isDisabled={form.runnable.length === 0}
        hint={form.ruleChoice === null ? null : resolutionText(form.ruleChoice)}
        onChange={form.choose}
      />
      <AssignBlockers
        sequencesFailed={form.sequences.isError}
        optionsFailed={form.options.isError}
        noSequence={form.sequences.isSuccess && form.list.length === 0}
        noneRunnable={form.list.length > 0 && form.runnable.length === 0}
      />

      <TextField
        label={<Trans>Computer name</Trans>}
        value={form.computerName}
        onChange={form.setComputerName}
        maxLength={15}
        autoComplete="off"
        spellCheck="false"
        mono
        isRequired={form.nameRequired}
        isInvalid={form.fieldProblem !== null}
        errorMessage={form.fieldProblem}
        hint={`${nameHint(machine, sequence, form.valuesName)} ${translate`Up to 15 letters A to Z, digits and hyphens.`}`}
      />

      <AssignAnswers
        sequenceName={sequence?.name ?? ""}
        inputs={form.inputs}
        answers={form.answers}
        defaults={form.defaults}
        refused={form.refused}
        documentFailed={form.document.isError}
      />

      <AssignConsequences
        machine={machine}
        sequence={sequence}
        erases={form.erases}
        severalDisks={form.severalDisks}
        risk={risk}
        warningId={warningId}
        options={form.options.data}
        now={form.now}
      />

      {risk !== null ? (
        <Checkbox aria-describedby={warningId} isSelected={form.allowed} onChange={form.allow}>
          {risk.allowLabel}
        </Checkbox>
      ) : null}

      {form.error !== null ? <Notice tone="fail">{form.error}</Notice> : null}
    </Form>
  );
}
