// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { liveListOptions } from "@/live/freshness";
import { useLiveStatus } from "@/live/useLiveStatus";
import { machinesQuery } from "@/machines/machines";
import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";
import { ComboBox, ListBoxItem } from "@/ui/Select";
import { Skeleton } from "@/ui/Skeleton";

import { sequenceResolutionQuery, type RuleView } from "./rules";
import { machineChoice } from "./ruleTestText";
import { RuleTestOutcome } from "./RuleTestOutcome";

// Which rules match a machine and what a run of it would start with, as the server works it out now. The rules only
// choose: someone still approves the machine or signs in at it.
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
          <RuleTestOutcome resolution={resolution.data} rules={rules} />
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
