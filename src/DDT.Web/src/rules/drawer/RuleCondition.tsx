// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";

import { ConditionBuilder } from "@/conditions/ConditionBuilder";
import type { Subject } from "@/conditions/conditionSubjects";
import { equalJson } from "@/lib/equalJson";
import { changedCondition } from "@/sequences/flow/conditionTree";

import type { RuleForm } from "./useRuleForm";

// The condition for when a rule applies, with how many known machines it matches. The server only counts them for the
// saved condition.
export function RuleCondition({
  form,
  subjects,
}: {
  form: RuleForm;
  subjects: readonly Subject[];
}) {
  const { t } = useLingui();
  const { base, edit, findings, change } = form;
  const sameCondition = base !== null && equalJson(base.when, edit.when);
  const matching = base?.matchingMachines ?? 0;

  return (
    <div className="flex flex-col gap-1.5">
      <ConditionBuilder
        label={<Trans>Applies when</Trans>}
        use="rule"
        value={edit.when}
        subjects={subjects}
        findings={findings}
        onChange={(path, conditionChange) => {
          const next = changedCondition(edit.when, path, conditionChange);

          if (next !== undefined) {
            change({ when: next });
          }
        }}
      />
      {base === null ? null : sameCondition ? (
        <p className="type-small text-muted">
          {matching === 0
            ? t`Matches no known machine now.`
            : plural(matching, {
                one: "Matches # known machine now.",
                other: "Matches # known machines now.",
              })}
        </p>
      ) : (
        <p className="type-small text-muted">
          <Trans>Save the rule to count the machines it matches.</Trans>
        </p>
      )}
    </div>
  );
}
