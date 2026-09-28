// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { Subject } from "@/conditions/conditionSubjects";
import type { MachineRoleView } from "@/roles/roles";
import { TextSetting } from "@/sequences/fields/TextSetting";
import type { SequenceSummary } from "@/sequences/sequences";
import { Checkbox } from "@/ui/Checkbox";
import { ValuesEditor } from "@/values/ValuesEditor";

import { RolesPicker } from "./RolesPicker";
import { RuleCondition } from "./RuleCondition";
import { SequenceChoice } from "./SequenceChoice";
import type { RuleForm } from "./useRuleForm";

interface RuleFieldsProps {
  form: RuleForm;
  roles: readonly MachineRoleView[];
  sequences: readonly SequenceSummary[];
  subjects: readonly Subject[];
  canEdit: boolean;
}

// A rule's fields: its name, the condition it applies when, the sequence it chooses, the values it sets and the
// machine roles it gives.
export function RuleFields({ form, roles, sequences, subjects, canEdit }: RuleFieldsProps) {
  const { edit, findings, change } = form;

  return (
    <>
      <TextSetting
        label={<Trans>Name</Trans>}
        field="name"
        findings={findings}
        value={edit.name}
        onChange={(text) => {
          change({ name: text });
        }}
      />

      <RuleCondition form={form} subjects={subjects} />

      <SequenceChoice form={form} sequences={sequences} />

      <ValuesEditor
        label={<Trans>Sets values</Trans>}
        hint={
          <Trans>
            A value a rule above sets already stays as that rule sets it; a sequence uses it by its
            name.
          </Trans>
        }
        rows={edit.values}
        findings={findings}
        onChange={(values) => {
          change({ values });
        }}
      />

      <RolesPicker
        roleIds={edit.roleIds}
        roles={roles}
        findings={findings}
        onChange={(roleIds) => {
          change({ roleIds });
        }}
      />

      <TextSetting
        label={<Trans>Description</Trans>}
        hint={<Trans>Optional. Why the rule exists, for whoever reads it later.</Trans>}
        field="description"
        findings={findings}
        value={edit.description}
        onChange={(text) => {
          change({ description: text });
        }}
      />

      <div data-field="enabled">
        <Checkbox
          isSelected={edit.enabled}
          isReadOnly={!canEdit}
          onChange={(enabled) => {
            change({ enabled });
          }}
        >
          <span className="flex flex-col">
            <span>
              <Trans>Check machines against this rule</Trans>
            </span>
            <span className="type-small text-muted">
              <Trans>Turned off, the rule is kept but matches no machine.</Trans>
            </span>
          </span>
        </Checkbox>
      </div>
    </>
  );
}
