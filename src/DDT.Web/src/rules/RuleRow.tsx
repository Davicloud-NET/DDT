// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { AutosaveStatus } from "@/components/AutosaveStatus";
import { plural } from "@/lib/format";
import { relativeTime } from "@/lib/relativeTime";
import type { SequenceSummary } from "@/sequences/sequences";

import { RuleFields, type RuleLists } from "./RuleFields";
import { describeRule, type AssignmentRuleView } from "./rules";
import { useRuleRow } from "./useRuleRow";

import styles from "./RuleRow.module.scss";

export interface RuleRowProps {
  rule: AssignmentRuleView;
  sequences: SequenceSummary[];
  // The registered machines it matches.
  matches: number;
  canEdit: boolean;
  lists: RuleLists;
  now: number;
  onDelete: () => void;
}

// One rule, edited in place and saved as it changes.
export function RuleRow({ rule, sequences, matches, canEdit, lists, now, onDelete }: RuleRowProps) {
  const row = useRuleRow(rule);

  return (
    <li className={styles.rule}>
      <fieldset className={styles.edit} disabled={!canEdit}>
        <legend className={styles.legend}>{`The rule for ${describeRule(rule)}`}</legend>
        <RuleFields
          kind={rule.kind}
          edit={row.edit}
          messages={row.messages}
          sequences={sequences}
          lists={lists}
          onChange={row.change}
        />
      </fieldset>
      <div className={styles.facts}>
        <span>
          {matches === 0
            ? "Matches no registered machine"
            : `Matches ${plural(matches, "machine")}`}
        </span>
        <span className={styles.secondary} title={new Date(rule.updatedUtc).toLocaleString()}>
          {`Changed ${relativeTime(rule.updatedUtc, now)}${rule.updatedBy === null ? "" : ` by ${rule.updatedBy}`}`}
        </span>
        <AutosaveStatus state={row.state} />
        {canEdit && (
          <button
            type="button"
            className={styles.delete}
            aria-label={`Delete the rule for ${describeRule(rule)}`}
            onClick={onDelete}
          >
            Delete
          </button>
        )}
      </div>
    </li>
  );
}
