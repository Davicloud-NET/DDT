// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";

import { MachineActions } from "@/machines/MachineActions";
import { deviceKind } from "@/machines/machineView";
import { cx } from "@/ui/cx";
import { DeviceGlyph } from "@/ui/DeviceGlyph";

import { MachineName } from "./MachineName";
import type { MachineRowsProps } from "./machineRows";
import { MachineStateTag } from "./MachineStateTag";
import { RunCell } from "./RunCell";

// The machine list on a phone. Each machine gets its own row, which links to the machine's page.
export function MachineCards({
  machines,
  now,
  canDecide,
  actions,
  strays,
  mark,
}: MachineRowsProps) {
  const { t: translate } = useLingui();

  return (
    <ul aria-label={translate`Machines`} className="flex flex-col">
      {machines.map((machine) => (
        <li
          key={machine.id}
          className={cx(
            "flex flex-col gap-2 border-b border-line-soft px-4 py-3 last:border-b-0",
            mark(machine.id),
          )}
        >
          <span className="flex items-start gap-3">
            <DeviceGlyph kind={deviceKind(machine)} />
            <MachineName machine={machine} className="flex min-w-0 flex-1 flex-col leading-tight" />
            <MachineStateTag machine={machine} />
          </span>
          <RunCell machine={machine} now={now} />
          {canDecide ? (
            <MachineActions
              machine={machine}
              actions={actions}
              strays={strays.get(machine.id) ?? null}
            />
          ) : null}
        </li>
      ))}
    </ul>
  );
}
