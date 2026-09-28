// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { isActive } from "@/deployments/deployments";
import { useLiveMarks } from "@/live/useLiveMarks";
import { MachineActions } from "@/machines/MachineActions";
import { machinesQuery, type MachineSummary } from "@/machines/machines";
import { deviceKind, displayName, machineTag } from "@/machines/machineView";
import type { MachineActionState } from "@/machines/useMachineActions";
import { cx } from "@/ui/cx";
import { DeviceGlyph } from "@/ui/DeviceGlyph";
import { StateTag } from "@/ui/StateTag";

import { MachinePlate } from "./MachinePlate";

interface MachineHeaderProps {
  machine: MachineSummary;
  // Null for someone who may only look.
  actions: MachineActionState | null;
  // Why the machine would get the sequence it gets, as the server explains it.
  resolution: string | null;
  now: number;
}

// The machine's name, state and plate of facts, with what an operator can do to it.
export function MachineHeader({ machine, actions, resolution, now }: MachineHeaderProps) {
  const { i18n } = useLingui();
  // The name and state flash when the state changes while the page is open.
  const mark = useLiveMarks({
    queryKey: machinesQuery.queryKey,
    items: (list) => list.filter((candidate) => candidate.id === machine.id),
    id: (candidate) => candidate.id,
    signature: (candidate) => `${candidate.state} ${machineTag(candidate).tone}`,
    tone: (candidate) => machineTag(candidate).tone,
  });
  const tag = machineTag(machine);
  const maker = [machine.manufacturer, machine.model].filter((part) => part !== null).join(" ");
  const signedInBy = machine.signedInBy;

  return (
    <header className="flex flex-col gap-4">
      <div className="flex flex-wrap items-start gap-x-4 gap-y-3">
        <DeviceGlyph kind={deviceKind(machine)} size="lg" />
        <div className="flex min-w-0 flex-1 flex-col gap-1">
          <span
            className={cx(
              "-mx-2 -my-1 flex w-fit flex-wrap items-center gap-3 rounded-key px-2 py-1",
              mark(machine.id),
            )}
          >
            <h1 className="type-title text-ink">{displayName(machine)}</h1>
            <StateTag tone={tag.tone}>{i18n._(tag.label)}</StateTag>
          </span>
          <span className="text-ink-2">
            {maker === "" ? <Trans>Model not reported</Trans> : maker}
            {signedInBy !== null ? (
              <>
                {". "}
                <Trans>{signedInBy} signed in at the machine.</Trans>
              </>
            ) : null}
          </span>
        </div>
        {actions !== null ? (
          <MachineActions machine={machine} actions={actions} layout="panel" />
        ) : null}
      </div>

      <MachinePlate machine={machine} now={now} />

      {resolution !== null && !isActive(machine.deployment) ? (
        <p className="type-small text-ink-2">{resolution}</p>
      ) : null}
    </header>
  );
}
