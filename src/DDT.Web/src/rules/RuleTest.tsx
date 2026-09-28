// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { liveListOptions } from "@/live/freshness";
import { useLiveStatus } from "@/live/useLiveStatus";
import { formatMac, machinesQuery, type MachineSummary } from "@/machines/machines";
import { findingText } from "@/sequences/problems";
import { Panel, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { ComboBox, ListBoxItem } from "@/ui/Select";

import {
  resolutionText,
  ruleNames,
  sequenceResolutionQuery,
  valueLines,
  valueSourceText,
  type MachineSequenceResolution,
  type RuleView,
} from "./rules";

// A machine's name as the picker shows it: the name it was given and its hardware, such as "PC-042, LENOVO ThinkPad
// T14 Gen 4", or the hardware and its MAC address before it has a name.
function machineChoice(machine: MachineSummary): string {
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

// Which rules match a machine and what a run of it would start with, as the server works it out now: the rules that
// match, top first; the sequence and where it comes from; each value with the rule or machine role it came from and
// what else set it. The rules only choose: someone still approves the machine or signs in at it.
export function RuleTest({ rules }: { rules: readonly RuleView[] }) {
  const { t: translate } = useLingui();
  const machines = useQuery({ ...machinesQuery, ...liveListOptions(useLiveStatus()) });
  const [machineId, setMachineId] = useState<string | null>(null);
  const resolution = useQuery({
    ...sequenceResolutionQuery(machineId ?? ""),
    enabled: machineId !== null,
  });
  const list = machines.data ?? [];

  return (
    <Panel
      title={<Trans>Test a machine</Trans>}
      actions={
        <ComboBox
          label={<span className="sr-only">{translate`Machine to test`}</span>}
          placeholder={translate`Choose a machine`}
          className="w-44 sm:w-75"
          value={machineId}
          onChange={(key) => {
            setMachineId(key === null ? null : String(key));
          }}
        >
          {list.map((machine) => (
            <ListBoxItem key={machine.id} id={machine.id} textValue={machineChoice(machine)}>
              {machineChoice(machine)}
            </ListBoxItem>
          ))}
        </ComboBox>
      }
    >
      {machineId === null ? (
        <p className="type-small text-muted">
          <Trans>
            Choose a machine to see which rules match it, the sequence it would get and the values a
            run of it would start with.
          </Trans>
        </p>
      ) : resolution.isPending ? (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-5 w-1/2" />
          <Skeleton className="h-5 w-2/3" />
        </div>
      ) : resolution.isError ? (
        <Notice tone="fail">
          <Trans>What the rules give this machine could not be worked out.</Trans>{" "}
          {resolution.error.message}
        </Notice>
      ) : (
        <>
          <Outcome resolution={resolution.data} rules={rules} />
          <p className="type-small text-muted">
            <Trans>
              An operator still approves this machine, or someone signs in at it; the rules only
              choose what then runs.
            </Trans>
          </p>
        </>
      )}
    </Panel>
  );
}

function sequenceLine(resolution: MachineSequenceResolution, rules: readonly RuleView[]): string {
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

function Outcome({
  resolution,
  rules,
}: {
  resolution: MachineSequenceResolution;
  rules: readonly RuleView[];
}) {
  const matched = (resolution.matchedRuleIds ?? []).flatMap((id) =>
    rules.filter((rule) => rule.id === id),
  );
  const lines = valueLines(resolution.values ?? []);
  const problems = resolution.valueProblems ?? [];
  const count = resolution.problemCount;

  return (
    <dl className="grid grid-cols-1 gap-y-0.5 type-body sm:grid-cols-[minmax(7rem,11rem)_minmax(0,1fr)] sm:gap-x-4 sm:gap-y-2">
      <dt className="mt-1.5 text-muted first:mt-0 sm:mt-0">
        <Trans>Matches</Trans>
      </dt>
      <dd className="min-w-0">
        {matched.length === 0 ? <Trans>No rule matches this machine.</Trans> : ruleNames(matched)}
      </dd>
      <dt className="mt-1.5 text-muted first:mt-0 sm:mt-0">
        <Trans>Task sequence</Trans>
      </dt>
      <dd className="flex min-w-0 flex-col">
        <span>{sequenceLine(resolution, rules)}</span>
        {count > 0 ? (
          <span className="type-small text-fail-text">
            {plural(count, {
              one: "It has # problem, so it cannot run until it is fixed.",
              other: "It has # problems, so it cannot run until they are fixed.",
            })}
          </span>
        ) : null}
      </dd>
      {lines.map((line) => {
        const source = valueSourceText(line.used, rules);
        const value = line.used.value;

        return (
          <div key={line.name} className="contents">
            <dt
              className="mt-1.5 min-w-0 truncate pt-px type-data text-ink-2 sm:mt-0"
              title={line.name}
            >
              {line.name}
            </dt>
            <dd className="flex min-w-0 flex-col break-words">
              <span>
                {value === null ? t`A secret, set by ${source}` : t`${value}, from ${source}`}
              </span>
              {line.overridden.map((other, index) => {
                const also = valueSourceText(other, rules);

                return (
                  <span key={index} className="type-small text-muted">
                    {t`Also set by ${also}, but ${source} comes first.`}
                  </span>
                );
              })}
            </dd>
          </div>
        );
      })}
      {problems.map((problem, index) => (
        <div key={index} className="contents">
          <dt className="mt-1.5 min-w-0 truncate pt-px type-data text-fail-text sm:mt-0">
            {problem.field}
          </dt>
          <dd className="text-fail-text">{findingText(problem)}</dd>
        </div>
      ))}
    </dl>
  );
}
