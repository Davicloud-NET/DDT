// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { RuleView } from "@/rules/rules";
import { TextSetting } from "@/sequences/fields/TextSetting";
import { ValuesEditor } from "@/values/ValuesEditor";

import { rulesGiving } from "../roles";
import { RuleLinks } from "../RuleLinks";
import type { RoleForm } from "./useRoleForm";

// A machine role's fields: its name, what it is for and the values it sets, then the rules that give a saved one.
export function RoleFields({ form, rules }: { form: RoleForm; rules: readonly RuleView[] }) {
  const { base, edit, findings, change } = form;
  const giving = base === null ? [] : rulesGiving(rules, base.id);

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

      <TextSetting
        label={<Trans>Description</Trans>}
        hint={
          <Trans>Optional. What machines with this role are, such as kiosks in the lobby.</Trans>
        }
        field="description"
        findings={findings}
        value={edit.description}
        onChange={(text) => {
          change({ description: text });
        }}
      />

      <ValuesEditor
        label={<Trans>Sets values</Trans>}
        hint={
          <Trans>
            A machine gets them from each rule that gives it the role, after the values the rules
            set themselves.
          </Trans>
        }
        rows={edit.values}
        findings={findings}
        onChange={(values) => {
          change({ values });
        }}
      />

      {base !== null ? (
        <div className="flex flex-col gap-1.5">
          <span className="type-label text-ink">
            <Trans>Given by</Trans>
          </span>
          {giving.length === 0 ? (
            <p className="type-small text-muted">
              <Trans>No rule gives this machine role yet.</Trans>
            </p>
          ) : (
            <RuleLinks rules={giving} />
          )}
        </div>
      ) : null}
    </>
  );
}
