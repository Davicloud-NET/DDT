// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { formattingLocale } from "@/i18n/i18n";
import type { ResolvedValue } from "@/values/values";

import type { RuleView } from "./rules";

// "Rule 2, Berlin office", to begin a sentence or stand alone, and "rule 2, Berlin office" inside one.
export function ruleName(rule: Pick<RuleView, "position" | "name">): string {
  const number = rule.position + 1;
  const name = rule.name;

  return t`Rule ${number}, ${name}`;
}

export function ruleNameInside(rule: Pick<RuleView, "position" | "name">): string {
  const number = rule.position + 1;
  const name = rule.name;

  return t`rule ${number}, ${name}`;
}

// Rules named in a sentence, with the first one written to start it: "Rule 2, Berlin office, and rule 4, Latitude
// laptops".
export function ruleNames(rules: readonly Pick<RuleView, "position" | "name">[]): string {
  const [first, ...rest] = rules;

  if (first === undefined) {
    return "";
  }

  return new Intl.ListFormat(formattingLocale(), { type: "conjunction" }).format([
    ruleName(first),
    ...rest.map(ruleNameInside),
  ]);
}

// What a rule does to the machines it matches, a few words per effect: "Chooses Kiosk", "Sets TimeZone", "Gives
// Office PC". A deleted role is left out. The rule's problems already mention it.
export function ruleEffects(
  rule: Pick<RuleView, "sequenceName" | "values" | "roleIds">,
  roles: readonly { id: string; name: string }[],
): string[] {
  const effects: string[] = [];
  const sequence = rule.sequenceName;

  if (sequence !== null) {
    effects.push(t`Chooses ${sequence}`);
  }

  for (const value of rule.values) {
    const name = value.name;

    effects.push(t`Sets ${name}`);
  }

  for (const id of rule.roleIds) {
    const role = roles.find((candidate) => candidate.id === id)?.name;

    if (role !== undefined) {
      effects.push(t`Gives ${role}`);
    }
  }

  return effects;
}

// A value's source inside a sentence, such as "rule 2" or "the machine role Office PC".
export function ruleValueSource(value: ResolvedValue, rules: readonly RuleView[]): string {
  const name = value.sourceName ?? "";

  switch (value.source) {
    case "Rule": {
      const rule = rules.find((candidate) => candidate.id === value.sourceId);

      if (rule === undefined) {
        return t`the rule ${name}`;
      }

      const number = rule.position + 1;

      return t`rule ${number}`;
    }
    case "Role":
      return t`the machine role ${name}`;
    case "Input":
      return t`an answer to the sequence's question`;
    case "Machine":
      return t`the machine's own value`;
    case "SequenceDefault":
      return t`the sequence's default`;
    case "DeploymentDefault":
      return t`the deployment defaults`;
    case "Fact":
      return t`a fact of the machine`;
    case "Step":
      return t`the step ${name}`;
  }
}
