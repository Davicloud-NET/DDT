// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import {
  IconArrowLeft,
  IconArrowRight,
  IconArrowsHorizontal,
  IconTrash,
} from "@tabler/icons-react";
import { useContext, useId, useRef, type KeyboardEvent as ReactKeyboardEvent } from "react";
import { Button as AriaButton, type KeyboardEvent } from "react-aria-components";

import { buttonClass } from "@/ui/buttonClass";
import { Panel } from "@/ui/Layout";
import { StateTag } from "@/ui/StateTag";
import { Tooltip } from "@/ui/Tooltip";

import { AddStepMenu } from "./AddStepMenu";
import { movesFrom } from "./editorFocus";
import { EditorLock } from "./editorLock";
import { FlagSetting, TextSetting } from "./fields";
import { unplacedFindings, type Findings } from "./problems";
import { findingCounts } from "./sequenceList";
import type { SequenceEdit, StepPatch } from "./sequenceEdits";
import type { SequencePhase, SequenceStep, StepKind } from "./sequences";
import { StepConditions } from "./StepConditions";
import { StepFields } from "./steps/StepFields";
import { phaseLabel, stepKindLabel } from "./steps";
import type { StepCatalog } from "./useSequenceEditor";

const iconKey =
  "flex size-7.5 cursor-pointer items-center justify-center rounded-key text-ink-2 outline-none hover:bg-hover hover:text-ink " +
  "focus-visible:outline-2 focus-visible:outline-focus disabled:cursor-not-allowed disabled:opacity-40";

// The step picked on the rail, with everything it does. Its keys move, insert and remove it; the arrow keys on its
// Move key, and Alt with Up or Down anywhere in it, move it without leaving the keyboard.
export function StepInspector({
  step,
  index,
  count,
  phase,
  findings,
  catalog,
  onEdit,
  onMove,
  onInsert,
  onRemove,
}: {
  step: SequenceStep;
  index: number;
  count: number;
  phase: SequencePhase;
  // This step's findings.
  findings: Findings;
  catalog: StepCatalog;
  onEdit: (edit: SequenceEdit) => void;
  onMove: (to: number) => void;
  onInsert: (kind: StepKind) => void;
  onRemove: () => void;
}) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);
  const handle = useRef<HTMLButtonElement>(null);
  const hintId = useId();

  const name = step.name;
  const number = String(index + 1).padStart(2, "0");
  const kind = stepKindLabel(step.kind);
  const phaseName = phaseLabel(phase);
  const unplaced = unplacedFindings(findings, step);
  const counts = findingCounts(findings.problems.length, findings.warnings.length);

  const moveTo = (to: number) => {
    if (!locked && to >= 0 && to < count && to !== index) {
      onMove(to);
    }
  };

  // A key that is about to be turned off hands the focus to the Move key first, so it is never lost.
  const moveBy = (to: number) => {
    if (to === 0 || to === count - 1) {
      handle.current?.focus();
    }

    moveTo(to);
  };

  const onHandleKey = (event: KeyboardEvent) => {
    const targets: Record<string, number> = {
      ArrowUp: index - 1,
      ArrowLeft: index - 1,
      ArrowDown: index + 1,
      ArrowRight: index + 1,
      Home: 0,
      End: count - 1,
    };
    const to = targets[event.key];

    if (event.altKey || to === undefined) {
      event.continuePropagation();
      return;
    }

    event.preventDefault();
    moveTo(to);
  };

  const onKey = (event: ReactKeyboardEvent) => {
    if (
      event.altKey &&
      (event.key === "ArrowUp" || event.key === "ArrowDown") &&
      movesFrom(event.target)
    ) {
      event.preventDefault();
      moveTo(event.key === "ArrowUp" ? index - 1 : index + 1);
    }
  };

  const change = (patch: StepPatch, chosen?: boolean) => {
    onEdit({ type: "updateStep", id: step.id, patch, ...(chosen === true ? { chosen } : {}) });
  };

  return (
    <div onKeyDown={onKey}>
      <Panel
        flush
        title={
          <span className="flex min-w-0 items-baseline gap-3">
            <span className="type-subtitle text-muted">{number}</span>
            <span className="truncate">{name.trim() === "" ? t`Unnamed step` : name}</span>
          </span>
        }
        actions={
          locked ? null : (
            <div className="flex flex-wrap items-center justify-end gap-1">
              <span id={hintId} className="sr-only">
                <Trans>
                  Use the arrow keys to move the step, and Home or End to move it to the start or
                  the end.
                </Trans>
              </span>
              <Tooltip
                content={<Trans>Arrow keys move the step; Home and End, to the ends.</Trans>}
              >
                <AriaButton
                  ref={handle}
                  aria-label={t`Move ${name}`}
                  aria-describedby={hintId}
                  onKeyDown={onHandleKey}
                  className={buttonClass("quiet", "sm")}
                >
                  <IconArrowsHorizontal aria-hidden="true" size={16} stroke={2} />
                  <Trans>Move</Trans>
                </AriaButton>
              </Tooltip>
              <Tooltip content={<Trans>Earlier</Trans>}>
                <AriaButton
                  aria-label={t`Move ${name} earlier`}
                  isDisabled={index === 0}
                  onPress={() => {
                    moveBy(index - 1);
                  }}
                  className={iconKey}
                >
                  <IconArrowLeft size={16} stroke={2} />
                </AriaButton>
              </Tooltip>
              <Tooltip content={<Trans>Later</Trans>}>
                <AriaButton
                  aria-label={t`Move ${name} later`}
                  isDisabled={index === count - 1}
                  onPress={() => {
                    moveBy(index + 1);
                  }}
                  className={iconKey}
                >
                  <IconArrowRight size={16} stroke={2} />
                </AriaButton>
              </Tooltip>
              <AddStepMenu
                label={<Trans>Insert after</Trans>}
                fullLabel={t`Insert after ${name}`}
                variant="quiet"
                onAdd={onInsert}
              />
              <AriaButton
                aria-label={t`Remove ${name}`}
                onPress={onRemove}
                className={buttonClass("quiet", "sm", "text-fail-text hover:text-fail-text")}
              >
                <IconTrash aria-hidden="true" size={16} stroke={2} />
                <Trans>Remove</Trans>
              </AriaButton>
            </div>
          )
        }
      >
        {/* Keyed by the step, so what a field holds while it is typed in stays with its step. */}
        <div key={step.id} className="flex flex-col gap-5 px-4 pt-3 pb-5">
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
            <span className="type-small text-ink-2">
              <Trans>
                {kind}, in {phaseName}
              </Trans>
            </span>
            {counts !== null ? (
              <StateTag tone={findings.problems.length > 0 ? "fail" : "attention"}>
                {counts}
              </StateTag>
            ) : null}
          </div>

          {unplaced.problems.length > 0 || unplaced.warnings.length > 0 ? (
            <ul
              data-findings
              tabIndex={-1}
              aria-label={t`Problems and warnings of this step`}
              className="flex flex-col gap-1 rounded-key bg-well px-3.5 py-2.5 type-small outline-none focus-visible:outline-2 focus-visible:outline-focus"
            >
              {unplaced.problems.map((problem, position) => (
                <li key={`p${String(position)}`} className="text-fail-text">
                  {problem.message}
                </li>
              ))}
              {unplaced.warnings.map((warning, position) => (
                <li key={`w${String(position)}`} className="text-attention-text">
                  {warning.message}
                </li>
              ))}
            </ul>
          ) : null}

          <TextSetting
            label={<Trans>Name</Trans>}
            field="name"
            findings={findings}
            hint={<Trans>Shown on the rail, in the run and in the log.</Trans>}
            value={step.name}
            onChange={(text) => {
              change({ name: text });
            }}
          />

          <div className="grid gap-x-4 gap-y-4 sm:grid-cols-2">
            <StepFields step={step} findings={findings} catalog={catalog} onChange={change} />
          </div>

          <StepConditions step={step} findings={findings} onEdit={onEdit} />

          <div className="flex flex-col gap-3">
            <FlagSetting
              label={<Trans>Go on when this step fails</Trans>}
              field="continueOnError"
              findings={findings}
              hint={<Trans>Otherwise the run stops at this step.</Trans>}
              value={step.continueOnError}
              onChange={(continueOnError) => {
                change({ continueOnError });
              }}
            />
            {step.kind !== "reboot" || step.rebootAfter ? (
              <FlagSetting
                label={<Trans>Restart after this step</Trans>}
                field="rebootAfter"
                findings={findings}
                hint={<Trans>The machine restarts once the step is done.</Trans>}
                value={step.rebootAfter}
                onChange={(rebootAfter) => {
                  change({ rebootAfter });
                }}
              />
            ) : null}
          </div>
        </div>
      </Panel>
    </div>
  );
}
