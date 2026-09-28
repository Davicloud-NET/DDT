// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { formatMac, type MachineSummary } from "@/machines/machines";

import { resolutionText, type MachineSequenceResolution, type RuleView } from "./rules";

// A machine's name as the rule test's picker shows it: the name it was given and its hardware, such as "PC-042, LENOVO
// ThinkPad T14 Gen 4", or the hardware and its MAC address before it has a name.
export function machineChoice(machine: MachineSummary): string {
  const name = machine.assignedName;
  const hardware = [machine.manufacturer, machine.model]
    .filter((part): part is string => part !== null && part !== "")
    .join(" ");
  const mac = formatMac(machine.primaryMac);

  if (name !== null) {
    return hardware === "" ? name : t`${name}, ${hardware}`;
  }

  return hardware === "" ? mac : t`${hardware}, ${mac}`;
}

// The sequence a machine would get and where from, with the number of the rule that chose it.
export function sequenceLine(
  resolution: MachineSequenceResolution,
  rules: readonly RuleView[],
): string {
  const sequence = resolution.sequenceName ?? "";

  switch (resolution.source) {
    case "Rule": {
      const rule = rules.find((candidate) => candidate.id === resolution.ruleId);

      if (rule === undefined) {
        return resolutionText(resolution);
      }

      const number = rule.position + 1;

      return t`${sequence}, from rule ${number}`;
    }
    case "Assigned":
      return t`${sequence}, assigned on the web, which comes before every rule`;
    case "Console":
      return t`${sequence}, chosen at the machine, which comes before every rule`;
    default:
      return resolutionText(resolution);
  }
}
