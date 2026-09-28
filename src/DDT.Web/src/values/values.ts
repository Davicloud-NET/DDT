// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

// The values a run works with, such as ComputerName or TimeZone, and where each came from. The server works them out
// when a run starts, from the first source that sets a name: input answers, the machine's own values, rules from the
// top, machine roles, the sequence's defaults and the deployment defaults.

export type ValueSource =
  "Input" | "Machine" | "Rule" | "Role" | "SequenceDefault" | "DeploymentDefault" | "Fact" | "Step";

// The server's ResolvedValue. sourceId and sourceName name the rule, machine role or step that set it, where one did.
// overridden marks a value a source further up also set, which is not used. value is null for a secret, which shows
// only that it is set.
export interface ResolvedValue {
  name: string;
  value: string | null;
  source: ValueSource;
  sourceId: string | null;
  sourceName: string | null;
  overridden: boolean;
}

// What a value's line says of its source, such as "From the rule Berlin office". answeredBy is who answered an input,
// where the page knows it; step is the step that set a value while the run went on, with its number.
export function valueSourceText(
  value: Pick<ResolvedValue, "source" | "sourceName">,
  more: { answeredBy?: string | null; step?: { number: number | null; name: string } | null } = {},
): string {
  const name = value.sourceName;

  switch (value.source) {
    case "Input": {
      const by = more.answeredBy ?? null;

      return by === null ? t`Answered for this run` : t`Answered by ${by} for this run`;
    }
    case "Machine":
      return t`Set on this machine`;
    case "Rule":
      return name === null ? t`From a rule` : t`From the rule ${name}`;
    case "Role":
      return name === null ? t`From a machine role` : t`From the machine role ${name}`;
    case "SequenceDefault":
      return t`The sequence's default`;
    case "DeploymentDefault":
      return t`From the deployment defaults`;
    case "Fact":
      return t`Reported by the machine`;
    case "Step": {
      const step = more.step ?? null;

      if (step === null) {
        return name === null ? t`Set by a step while the run went on` : t`Set by the step ${name}`;
      }

      const stepName = step.name;
      const number = step.number;

      return number === null
        ? t`Set by the step ${stepName}`
        : t`Set by step ${number}, ${stepName}`;
    }
  }
}

// What an input's field says of the answer it starts with, such as "Filled in from the rule Berlin office.".
export function prefillText(value: Pick<ResolvedValue, "source" | "sourceName">): string {
  const name = value.sourceName;

  switch (value.source) {
    case "Input":
      return t`Filled in with the answer given before.`;
    case "Machine":
      return t`Filled in with this machine's own value.`;
    case "Rule":
      return name === null ? t`Filled in by a rule.` : t`Filled in from the rule ${name}.`;
    case "Role":
      return name === null
        ? t`Filled in by a machine role.`
        : t`Filled in from the machine role ${name}.`;
    case "SequenceDefault":
      return t`Filled in with the sequence's default.`;
    case "DeploymentDefault":
      return t`Filled in from the deployment defaults.`;
    case "Fact":
      return t`Filled in with what the machine reported.`;
    case "Step":
      return t`Filled in with a value a step set.`;
  }
}

// The values that are used, one per name, in the order the server gave them.
export function usedValues(values: readonly ResolvedValue[]): ResolvedValue[] {
  return values.filter((value) => !value.overridden);
}
