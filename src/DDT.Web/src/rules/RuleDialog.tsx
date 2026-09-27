// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Form } from "react-aria-components";

import { ApiError } from "@/lib/api";
import { formatMac, type HardwareModelCount, type MachineSummary } from "@/machines/machines";
import { matchingMachines } from "@/packages/packages";
import { canRun, type SequenceSummary } from "@/sequences/sequences";
import { Button } from "@/ui/Button";
import { FilterSelector } from "@/ui/Controls";
import { Dialog } from "@/ui/Dialog";
import { Notice } from "@/ui/Notice";
import { ComboBox, ListBoxItem, Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";

import {
  createRule,
  editOf,
  emptyEdit,
  refusalMessages,
  requestOf,
  rulesQuery,
  sequenceResolutionsKey,
  updateRule,
  type AssignmentRuleKind,
  type AssignmentRuleView,
  type RuleEdit,
  type RuleField,
} from "./rules";

// Adds a rule, or changes one. A new rule picks its kind here; an existing one keeps it. The server refuses a
// rule that would match the same machines as another, and says so on the MAC or the model. A sequence with problems
// cannot run, so it cannot be chosen.
export function RuleDialog({
  rule,
  sequences,
  machines,
  models,
  onClose,
}: {
  // Null to add a rule.
  rule: AssignmentRuleView | null;
  sequences: readonly SequenceSummary[];
  machines: readonly MachineSummary[];
  models: readonly HardwareModelCount[];
  onClose: () => void;
}) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const [kind, setKind] = useState<AssignmentRuleKind>(rule?.kind ?? "Model");
  const [edit, setEdit] = useState<RuleEdit>(() =>
    rule === null ? emptyEdit(sequences.find(canRun)?.id ?? "") : editOf(rule),
  );

  const save = useMutation({
    mutationFn: () =>
      rule === null
        ? createRule(requestOf(kind, edit))
        : updateRule(rule.id, requestOf(kind, edit)),
    onSuccess: (saved) => {
      queryClient.setQueryData(rulesQuery.queryKey, (list) =>
        list === undefined
          ? list
          : list.some((existing) => existing.id === saved.id)
            ? list.map((existing) => (existing.id === saved.id ? saved : existing))
            : [...list, saved],
      );
      // What each machine would get is the server's answer to the rules, so it is asked again.
      void queryClient.invalidateQueries({ queryKey: sequenceResolutionsKey });
      onClose();
    },
  });

  // A 400 names its fields and a 409 belongs to the MAC or the model; anything else, such as a lost
  // connection, shows below the form.
  const refusal =
    save.error instanceof ApiError && (save.error.status === 400 || save.error.status === 409)
      ? { message: save.error.message, problem: save.error.problem }
      : null;
  const messages = (field: RuleField) => refusalMessages(kind, refusal, field);
  const change = (patch: Partial<RuleEdit>) => {
    setEdit((current) => ({ ...current, ...patch }));
  };

  const macs = [...new Set(machines.flatMap((machine) => machine.macAddresses))].map(formatMac);
  const manufacturers = [
    ...new Set(
      models.flatMap((model) => (model.manufacturer === null ? [] : [model.manufacturer])),
    ),
  ];
  const modelNames = [
    ...new Set(
      models
        .filter(
          (model) =>
            edit.manufacturer.trim() === "" || model.manufacturer === edit.manufacturer.trim(),
        )
        .map((model) => model.model),
    ),
  ];
  const matching =
    kind === "Model" && edit.model.trim() !== ""
      ? matchingMachines(
          [
            {
              manufacturer: edit.manufacturer.trim() === "" ? null : edit.manufacturer.trim(),
              model: edit.model.trim(),
            },
          ],
          models,
        )
      : null;
  const formId = `rule-${rule?.id ?? "new"}`;
  const problemsText = (sequence: SequenceSummary) => {
    const count = sequence.problemCount;
    const problems = plural(count, { one: "# problem", other: "# problems" });

    return t`${problems}, cannot run`;
  };

  return (
    <Dialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={rule === null ? <Trans>Add an assignment rule</Trans> : <Trans>Change the rule</Trans>}
      isBusy={save.isPending}
      footer={
        <>
          <Button variant="secondary" isDisabled={save.isPending} onPress={onClose}>
            <Trans>Cancel</Trans>
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="primary"
            isDisabled={save.isPending || edit.sequenceId === ""}
          >
            {rule === null ? <Trans>Add rule</Trans> : <Trans>Save rule</Trans>}
          </Button>
        </>
      }
    >
      {/* The server's refusal marks a field invalid; with the browser's own validation that would block the next
          save until the dialog closed, so the form only tells assistive technology. */}
      <Form
        id={formId}
        className="flex flex-col gap-4"
        validationBehavior="aria"
        onSubmit={(event) => {
          event.preventDefault();
          save.mutate();
        }}
      >
        {rule === null ? (
          <FilterSelector
            label={t`Which machines the rule is for`}
            selected={kind}
            onChange={(id) => {
              setKind(id === "Mac" ? "Mac" : "Model");
              save.reset();
            }}
            options={[
              { id: "Model", label: t`A hardware model` },
              { id: "Mac", label: t`One MAC address` },
            ]}
          />
        ) : null}

        {kind === "Mac" ? (
          <ComboBox
            label={<Trans>MAC address</Trans>}
            hint={<Trans>Any of the machine's network adapters, with or without separators.</Trans>}
            mono
            allowsCustomValue
            inputValue={edit.mac}
            onInputChange={(mac) => {
              change({ mac });
            }}
            isInvalid={messages("mac").length > 0}
            errorMessage={messages("mac").join(" ")}
            autoFocus
          >
            {macs.map((mac) => (
              <ListBoxItem key={mac} id={mac} textValue={mac}>
                {mac}
              </ListBoxItem>
            ))}
          </ComboBox>
        ) : (
          <div className="grid gap-4 sm:grid-cols-2">
            <ComboBox
              label={<Trans>Manufacturer</Trans>}
              hint={<Trans>Left empty, any manufacturer.</Trans>}
              allowsCustomValue
              inputValue={edit.manufacturer}
              onInputChange={(manufacturer) => {
                change({ manufacturer });
              }}
              isInvalid={messages("manufacturer").length > 0}
              errorMessage={messages("manufacturer").join(" ")}
            >
              {manufacturers.map((manufacturer) => (
                <ListBoxItem key={manufacturer} id={manufacturer} textValue={manufacturer}>
                  {manufacturer}
                </ListBoxItem>
              ))}
            </ComboBox>
            <ComboBox
              label={<Trans>Model</Trans>}
              hint={<Trans>End it with * to match every model that starts with it.</Trans>}
              allowsCustomValue
              inputValue={edit.model}
              onInputChange={(model) => {
                change({ model });
              }}
              isInvalid={messages("model").length > 0}
              errorMessage={messages("model").join(" ")}
              autoFocus
            >
              {modelNames.map((model) => (
                <ListBoxItem key={model} id={model} textValue={model}>
                  {model}
                </ListBoxItem>
              ))}
            </ComboBox>
          </div>
        )}

        {matching !== null ? (
          <p className="-mt-2 type-small text-muted">
            {matching === 0
              ? t`No registered machine matches yet.`
              : plural(matching, {
                  one: "Matches # registered machine.",
                  other: "Matches # registered machines.",
                })}
          </p>
        ) : null}

        <Select
          label={<Trans>Task sequence</Trans>}
          value={edit.sequenceId === "" ? null : edit.sequenceId}
          onChange={(key) => {
            change({ sequenceId: key === null ? "" : String(key) });
          }}
          isInvalid={messages("sequenceId").length > 0}
          errorMessage={messages("sequenceId").join(" ")}
          placeholder={t`Choose a sequence`}
        >
          {sequences.map((sequence) => (
            <ListBoxItem
              key={sequence.id}
              id={sequence.id}
              textValue={sequence.name}
              isDisabled={!canRun(sequence)}
              {...(canRun(sequence) ? {} : { description: problemsText(sequence) })}
            >
              {sequence.name}
            </ListBoxItem>
          ))}
        </Select>

        <TextField
          label={<Trans>Description</Trans>}
          hint={<Trans>Optional. Why the rule exists, for whoever reads it later.</Trans>}
          value={edit.description}
          onChange={(description) => {
            change({ description });
          }}
          isInvalid={messages("description").length > 0}
          errorMessage={messages("description").join(" ")}
        />

        {save.isError && refusal === null ? (
          <Notice tone="fail">{save.error.message}</Notice>
        ) : null}
      </Form>
    </Dialog>
  );
}
