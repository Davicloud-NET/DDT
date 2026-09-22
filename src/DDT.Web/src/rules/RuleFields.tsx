// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@/lib/format";
import { FormField } from "@/sequences/FormField";
import { canRun, type SequenceSummary } from "@/sequences/sequences";

import type { AssignmentRuleKind, RuleEdit, RuleField } from "./rules";

import styles from "./RuleFields.module.scss";

// The datalists of what registered machines reported.
export interface RuleLists {
  macs: string;
  models: string;
  manufacturers: string;
}

export interface RuleFieldsProps {
  kind: AssignmentRuleKind;
  edit: RuleEdit;
  messages: (field: RuleField) => string[];
  sequences: SequenceSummary[];
  lists: RuleLists;
  onChange: (patch: Partial<RuleEdit>, immediate: boolean) => void;
}

// What a rule matches and the sequence it chooses. A sequence with problems cannot run, so it cannot be chosen.
export function RuleFields({ kind, edit, messages, sequences, lists, onChange }: RuleFieldsProps) {
  return (
    <div className={styles.fields}>
      {kind === "Mac" ? (
        <FormField label="MAC address" messages={messages("mac")}>
          {(control) => (
            <input
              {...control}
              type="text"
              list={lists.macs}
              placeholder="00:15:5D:01:02:03"
              value={edit.mac}
              onChange={(event) => {
                onChange({ mac: event.target.value }, false);
              }}
            />
          )}
        </FormField>
      ) : (
        <>
          <FormField
            label="Manufacturer"
            messages={messages("manufacturer")}
            hint="Empty matches any manufacturer."
          >
            {(control) => (
              <input
                {...control}
                type="text"
                list={lists.manufacturers}
                placeholder="Any"
                value={edit.manufacturer}
                onChange={(event) => {
                  onChange({ manufacturer: event.target.value }, false);
                }}
              />
            )}
          </FormField>
          <FormField
            label="Model"
            messages={messages("model")}
            hint="Ending in * matches every model that starts with the text before it."
          >
            {(control) => (
              <input
                {...control}
                type="text"
                list={lists.models}
                value={edit.model}
                onChange={(event) => {
                  onChange({ model: event.target.value }, false);
                }}
              />
            )}
          </FormField>
        </>
      )}
      <FormField label="Sequence" messages={messages("sequenceId")}>
        {(control) => (
          <select
            {...control}
            value={edit.sequenceId}
            onChange={(event) => {
              onChange({ sequenceId: event.target.value }, true);
            }}
          >
            {edit.sequenceId === "" && <option value="">Choose a sequence</option>}
            {sequences.map((sequence) => (
              <option key={sequence.id} value={sequence.id} disabled={!canRun(sequence)}>
                {canRun(sequence)
                  ? sequence.name
                  : `${sequence.name} (${plural(sequence.problemCount, "problem")}, cannot run)`}
              </option>
            ))}
          </select>
        )}
      </FormField>
      <FormField label="Description" messages={messages("description")}>
        {(control) => (
          <input
            {...control}
            type="text"
            value={edit.description}
            onChange={(event) => {
              onChange({ description: event.target.value }, false);
            }}
          />
        )}
      </FormField>
    </div>
  );
}
