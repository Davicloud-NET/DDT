// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconX } from "@tabler/icons-react";
import { Button as AriaButton } from "react-aria-components";

import { activityLabel, isActive } from "@/deployments/deployments";
import { formatDuration } from "@/lib/format";
import { relativeTime } from "@/lib/relativeTime";
import { formatMac, type MachineSummary } from "@/machines/machines";
import { secureBootFact } from "@/machines/secureBoot";
import type { MachineActionState } from "@/machines/useMachineActions";
import { useRunDetail } from "@/runs/useRunDetail";
import { LinkButton } from "@/ui/Button";
import { DeviceGlyph } from "@/ui/DeviceGlyph";
import { Facts } from "@/ui/Layout";
import { SequenceRail } from "@/ui/SequenceRail";
import { StateTag } from "@/ui/StateTag";

import { MachineActions } from "./MachineActions";
import {
  deviceKind,
  displayName,
  hardwareLine,
  railFromSteps,
  railFromSummary,
  railStepText,
  stateLabel,
  stateTone,
} from "./machineView";

// The machine picked in the list, beside it: its run as it happens and what it is, without leaving the list. It
// follows the machine live: the run's steps arrive from the hub while the panel is open. It enters from the list's
// side when it opens; picking another machine only changes what it shows.
export function MachinePanel({
  machine,
  actions,
  canDecide,
  now,
  onClose,
}: {
  machine: MachineSummary;
  actions: MachineActionState;
  canDecide: boolean;
  now: number;
  onClose: () => void;
}) {
  const { i18n, t: translate } = useLingui();
  const detail = useRunDetail(machine.id, null);
  const run = machine.deployment;
  const steps =
    detail.view?.summary.id === run?.id && detail.view ? railFromSteps(detail.view.steps) : null;
  const rail = steps ?? (run === null ? [] : railFromSummary(run));
  const activity = run === null ? null : activityLabel(run.activity);
  const running = run?.state === "Running";
  const signedInBy = machine.signedInBy;

  return (
    <aside
      aria-label={displayName(machine)}
      className="flex w-100 shrink-0 animate-panel-in flex-col self-start overflow-hidden rounded-overlay bg-raised shadow-overlay max-lg:hidden"
    >
      <div className="flex items-start gap-3.5 px-4.5 pt-4.5 pb-3.5">
        <DeviceGlyph kind={deviceKind(machine)} size="lg" />
        <div className="flex min-w-0 flex-1 flex-col gap-1">
          <span className="truncate type-subtitle">{displayName(machine)}</span>
          <span className="truncate text-muted">{hardwareLine(machine)}</span>
          <span className="flex flex-wrap items-center gap-2 pt-0.5">
            <StateTag tone={stateTone[machine.state]}>{i18n._(stateLabel[machine.state])}</StateTag>
            {signedInBy !== null ? (
              <span className="type-small text-muted">
                <Trans>{signedInBy} signed in here</Trans>
              </span>
            ) : null}
          </span>
        </div>
        <AriaButton
          aria-label={translate`Close the details`}
          onPress={onClose}
          className="-mt-1 -mr-1 flex size-8 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover hover:text-ink pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:outline-focus"
        >
          <IconX size={18} stroke={2} />
        </AriaButton>
      </div>

      {run !== null ? (
        <section
          aria-label={translate`Current run`}
          className="flex flex-col gap-2.5 border-t border-line-soft px-4.5 py-3.5"
        >
          <span className="flex justify-between gap-3 type-small text-ink-2">
            <span className="truncate">{run.title}</span>
            {run.stepIndex !== null && run.stepCount > 0 ? (
              <span className="shrink-0">{stepOf(run.stepIndex, run.stepCount)}</span>
            ) : null}
          </span>
          {running && activity === null && run.stepName !== null ? (
            <span className="flex items-end gap-3">
              <span className="type-display text-run-text">{run.percent}%</span>
              <span className="type-heading pb-1">{run.stepName}</span>
            </span>
          ) : (
            <span className="type-heading">{activity ?? runStateText(run.state)}</span>
          )}
          {rail.length > 0 ? (
            <SequenceRail steps={rail} showNames={false} describe={railStepText} />
          ) : null}
          {run.state === "Failed" && run.error !== null ? (
            <p className="type-small text-fail-text">{run.error}</p>
          ) : null}
          {run.startedUtc !== null && isActive(run) ? (
            <span className="type-small text-muted">
              {runningFor(now - Date.parse(run.startedUtc))}
            </span>
          ) : null}
        </section>
      ) : null}

      <div className="border-t border-line-soft px-4.5 py-3.5">
        <Facts
          items={[
            ...(machine.serialNumber === null
              ? []
              : [{ label: <Trans>Serial</Trans>, value: machine.serialNumber, mono: true }]),
            { label: <Trans>MAC address</Trans>, value: formatMac(machine.primaryMac), mono: true },
            ...(machine.lastSeenAddress === null
              ? []
              : [{ label: <Trans>Address</Trans>, value: machine.lastSeenAddress, mono: true }]),
            { label: <Trans>Secure Boot</Trans>, value: secureBootFact(machine) },
            ...(machine.disks === null
              ? []
              : [{ label: <Trans>Disks</Trans>, value: machine.disks }]),
            { label: <Trans>Last seen</Trans>, value: relativeTime(machine.lastSeenUtc, now) },
          ]}
        />
      </div>

      <div className="flex flex-wrap items-center gap-2 border-t border-line-soft bg-panel px-4.5 py-3.5">
        <LinkButton variant="primary" href={`/machines/${machine.id}`}>
          <Trans>Open machine</Trans>
        </LinkButton>
        <div className="flex-1" />
        {canDecide ? <MachineActions machine={machine} actions={actions} layout="panel" /> : null}
      </div>
    </aside>
  );
}

function stepOf(index: number, count: number): string {
  const number = index + 1;

  return t`Step ${number} of ${count}`;
}

function runningFor(milliseconds: number): string {
  const duration = formatDuration(milliseconds);

  return t`Running for ${duration}`;
}

function runStateText(state: NonNullable<MachineSummary["deployment"]>["state"]): string {
  switch (state) {
    case "Assigned":
      return t`Waiting to start`;
    case "Running":
      return t`Starting`;
    case "Done":
      return t`Done`;
    case "Failed":
      return t`Failed`;
    case "Cancelled":
      return t`Stopped`;
  }
}
