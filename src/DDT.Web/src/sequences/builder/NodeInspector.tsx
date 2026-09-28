// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconBoxMultiple, IconPlus, IconTrash, IconX } from "@tabler/icons-react";
import { useContext, useId, type KeyboardEvent } from "react";
import { Button as AriaButton } from "react-aria-components";

import { ConditionBuilder } from "@/conditions/ConditionBuilder";
import { legacyPath, legacyTree } from "@/conditions/conditions";
import { formattingLocale } from "@/i18n/i18n";
import { Button } from "@/ui/Button";
import { buttonClass } from "@/ui/buttonClass";
import { StateTag } from "@/ui/StateTag";

import { movesFrom } from "../editorFocus";
import { EditorLock } from "../editorLock";
import { FlagSetting, NumberSetting, TextSetting } from "../fields";
import { changedCondition, type ConditionChange } from "../flow/conditionTree";
import { findingText, unplacedFindings, type Findings } from "../problems";
import type { SequenceEdit, StepPatch } from "../sequenceEdits";
import { findingCounts } from "../sequenceList";
import type { AccountReference, SequencePhase, SequenceStep, ShareConnection } from "../sequences";
import { StepFields } from "../steps/StepFields";
import { isContainer, phaseLabel, stepKindLabel } from "../steps";
import type { StepCatalog } from "../useSequenceEditor";
import { AccountSetting } from "./AccountSetting";
import { useBuilder } from "./builderData";
import { TemplateField } from "./TemplateField";

// A share is connected for one step, and a step connects at most this many.
const MAX_SHARES = 4;

const iconKey =
  "flex size-8 shrink-0 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none " +
  "hover:bg-hover hover:text-ink pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:outline-focus";

// The node chosen in the flow, with everything it does: its kind's fields, when it runs, what it connects, what
// happens when it fails, and the keys that wrap, unwrap and remove it. Alt with Up or Down in its fields moves it
// within its list, as on the canvas.
export function NodeInspector({
  node,
  place,
  phases,
  findings,
  catalog,
  onEdit,
  onRemove,
  onWrap,
  onUnwrap,
  onShift,
}: {
  node: SequenceStep;
  // Where the node is, such as "Step 2 of Then of 'If: Is it a Latitude?'".
  place: string;
  phases: readonly SequencePhase[];
  // This node's findings.
  findings: Findings;
  catalog: StepCatalog;
  onEdit: (edit: SequenceEdit) => void;
  onRemove: () => void;
  onWrap: () => void;
  onUnwrap: () => void;
  onShift: (by: -1 | 1) => void;
}) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);
  const { subjects } = useBuilder();
  const name = node.name;
  const kind = stepKindLabel(node.kind);
  const phaseName = new Intl.ListFormat(formattingLocale(), { type: "disjunction" }).format(
    phases.map(phaseLabel),
  );
  const unplaced = unplacedFindings(findings, node);
  const counts = findingCounts(findings.problems.length, findings.warnings.length);
  const container = isContainer(node);
  const thenCount = node.kind === "if" ? node.then.length : 0;
  const elseCount = String(node.kind === "if" ? node.else.length : 0);
  const bodyCount = node.kind === "group" || node.kind === "repeat" ? node.steps.length : 0;

  const change = (patch: StepPatch, chosen?: boolean) => {
    onEdit({ type: "updateNode", id: node.id, patch, ...(chosen === true ? { chosen } : {}) });
  };

  // The conditions of versions 1 and 2 show with the when as one tree, and become the when at the first change.
  const legacyCount = node.conditions.length;
  const when = legacyTree(node.conditions, node.when);
  const onWhen = (path: readonly number[], condition: ConditionChange) => {
    if (legacyCount === 0) {
      onEdit({ type: "editCondition", id: node.id, field: "when", path, change: condition });
      return;
    }

    const tree = changedCondition(when, path, condition);

    if (tree !== undefined) {
      change({ conditions: [], when: tree }, condition.op !== "update");
    }
  };

  const onKey = (event: KeyboardEvent) => {
    if (
      event.altKey &&
      (event.key === "ArrowUp" || event.key === "ArrowDown") &&
      movesFrom(event.target)
    ) {
      event.preventDefault();
      onShift(event.key === "ArrowUp" ? -1 : 1);
    }
  };

  return (
    // Keyed by the node, so what a field holds while it is typed in stays with its node.
    <div key={node.id} onKeyDown={onKey} className="flex flex-col gap-5">
      <div className="flex flex-col gap-1.5">
        <span className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
          <span className="type-small text-muted">{place}</span>
          {counts !== null ? (
            <StateTag tone={findings.problems.length > 0 ? "fail" : "attention"}>{counts}</StateTag>
          ) : null}
        </span>
        <span className="type-small text-ink-2">
          <Trans>
            {kind}, in {phaseName}.
          </Trans>{" "}
          {node.kind === "if"
            ? plural(thenCount, {
                one: `Then holds # step, Else ${elseCount}.`,
                other: `Then holds # steps, Else ${elseCount}.`,
              })
            : node.kind === "group" || node.kind === "repeat"
              ? plural(bodyCount, { one: "Holds # step.", other: "Holds # steps." })
              : null}
        </span>
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
              {findingText(problem)}
            </li>
          ))}
          {unplaced.warnings.map((warning, position) => (
            <li key={`w${String(position)}`} className="text-attention-text">
              {findingText(warning)}
            </li>
          ))}
        </ul>
      ) : null}

      <TextSetting
        label={<Trans>Name</Trans>}
        field="name"
        findings={findings}
        hint={<Trans>Shown in the flow, in the run and in the log.</Trans>}
        value={node.name}
        onChange={(text) => {
          change({ name: text });
        }}
      />

      {container ? null : (
        <div className="flex flex-col gap-4">
          <StepFields step={node} findings={findings} catalog={catalog} onChange={change} />
        </div>
      )}

      {node.kind === "if" ? (
        <ConditionBuilder
          label={<Trans>Go along Then when</Trans>}
          hint={<Trans>Every other machine goes along Else.</Trans>}
          use="test"
          field="test"
          value={node.test}
          subjects={subjects}
          findings={findings}
          onChange={(path, condition) => {
            onEdit({ type: "editCondition", id: node.id, field: "test", path, change: condition });
          }}
        />
      ) : null}

      {node.kind === "repeat" ? (
        <>
          <ConditionBuilder
            label={<Trans>Repeat until</Trans>}
            hint={<Trans>Tested after each time the steps inside ran.</Trans>}
            use="until"
            field="until"
            value={node.until}
            subjects={subjects}
            findings={findings}
            onChange={(path, condition) => {
              onEdit({
                type: "editCondition",
                id: node.id,
                field: "until",
                path,
                change: condition,
              });
            }}
          />
          <NumberSetting
            label={<Trans>At most this many times</Trans>}
            field="maxTimes"
            findings={findings}
            hint={<Trans>From 1 to 100.</Trans>}
            minValue={1}
            maxValue={100}
            value={node.maxTimes}
            onChange={(maxTimes) => {
              change({ maxTimes });
            }}
          />
          <FlagSetting
            label={<Trans>Go on when the limit is reached</Trans>}
            field="goOnAtLimit"
            findings={findings}
            hint={<Trans>Otherwise the repeat fails when its condition still does not hold.</Trans>}
            value={node.goOnAtLimit}
            onChange={(goOnAtLimit) => {
              change({ goOnAtLimit });
            }}
          />
        </>
      ) : null}

      {node.kind === "if" ? null : (
        <ConditionBuilder
          label={container ? <Trans>Run it only when</Trans> : <Trans>Run only when</Trans>}
          use="when"
          value={when}
          subjects={subjects}
          findings={findings}
          fieldOf={(path) => legacyPath(legacyCount, (node.when ?? null) !== null, path)}
          onChange={onWhen}
        />
      )}

      {container ? null : (
        <SharesSetting
          shares={node.shares ?? []}
          findings={findings}
          onChange={(shares, chosen) => {
            change({ shares: shares.length === 0 ? null : shares }, chosen);
          }}
        />
      )}

      <div className="flex flex-col gap-3">
        <FlagSetting
          label={
            container ? (
              <Trans>Go on when a step in here fails</Trans>
            ) : (
              <Trans>Go on when this step fails</Trans>
            )
          }
          field="continueOnError"
          findings={findings}
          hint={<Trans>Otherwise the run stops here.</Trans>}
          value={node.continueOnError}
          onChange={(continueOnError) => {
            change({ continueOnError });
          }}
        />
        {!container && (node.kind !== "reboot" || node.rebootAfter) ? (
          <FlagSetting
            label={<Trans>Restart after this step</Trans>}
            field="rebootAfter"
            findings={findings}
            hint={<Trans>The machine restarts once the step is done.</Trans>}
            value={node.rebootAfter}
            onChange={(rebootAfter) => {
              change({ rebootAfter });
            }}
          />
        ) : null}
      </div>

      {locked ? null : (
        <div className="flex flex-wrap items-center gap-2 border-t border-line-soft pt-4">
          <AriaButton
            aria-label={t`Wrap ${name} in a group`}
            className={buttonClass("secondary", "sm")}
            onPress={onWrap}
          >
            <IconBoxMultiple aria-hidden="true" size={16} stroke={2} />
            <Trans>Wrap in a group</Trans>
          </AriaButton>
          {container ? (
            <AriaButton
              aria-label={t`Unwrap ${name}`}
              className={buttonClass("quiet", "sm")}
              onPress={onUnwrap}
            >
              <Trans>Unwrap</Trans>
            </AriaButton>
          ) : null}
          <AriaButton
            aria-label={t`Remove ${name}`}
            onPress={onRemove}
            className={buttonClass("quiet", "sm", "text-fail-text hover:text-fail-text")}
          >
            <IconTrash aria-hidden="true" size={16} stroke={2} />
            <Trans>Remove</Trans>
          </AriaButton>
        </div>
      )}
    </div>
  );
}

// The shares DDT connects before the step runs and disconnects after it, each with its account.
function SharesSetting({
  shares,
  findings,
  onChange,
}: {
  shares: readonly ShareConnection[];
  findings: Findings;
  onChange: (shares: ShareConnection[], chosen: boolean) => void;
}) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);
  const titleId = useId();
  const { accounts, inputs } = useBuilder();
  const first: AccountReference =
    accounts?.[0] !== undefined
      ? { accountId: accounts[0].id, input: null }
      : { accountId: null, input: inputs.find((input) => input.kind === "Account")?.name ?? null };

  const set = (index: number, share: ShareConnection, chosen: boolean) => {
    onChange(
      shares.map((other, at) => (at === index ? share : other)),
      chosen,
    );
  };

  return (
    <section aria-labelledby={titleId} data-field="shares" className="flex flex-col gap-2.5">
      <h3 id={titleId} className="type-label text-ink">
        <Trans>Network shares</Trans>
      </h3>
      <p className="type-small text-muted">
        {shares.length === 0 ? (
          <Trans>None. DDT connects the shares listed here before the step runs.</Trans>
        ) : (
          <Trans>
            DDT connects these shares with their accounts before the step runs and disconnects them
            after it. A share's host comes only from values fixed when the run starts.
          </Trans>
        )}
      </p>
      {shares.map((share, index) => {
        const number = index + 1;

        return (
          <div
            key={index}
            className="flex flex-col gap-3 rounded-key bg-well p-3 shadow-[inset_0_0_0_1px_var(--color-line-soft)]"
          >
            <div className="flex items-start gap-1.5">
              <TemplateField
                label={t`Share ${number}`}
                field={`shares[${String(index)}].path`}
                findings={findings}
                className="min-w-0 flex-1"
                placeholder="\\fs01.corp.example\deploy"
                howTo={false}
                value={share.path}
                onChange={(path) => {
                  set(index, { ...share, path }, false);
                }}
              />
              {locked ? null : (
                <AriaButton
                  aria-label={t`Remove share ${number}`}
                  className={`${iconKey} mt-6.5`}
                  onPress={() => {
                    onChange(
                      shares.filter((_, at) => at !== index),
                      true,
                    );
                  }}
                >
                  <IconX size={14} stroke={2} />
                </AriaButton>
              )}
            </div>
            <AccountSetting
              label={t`Account for share ${number}`}
              field={`shares[${String(index)}].account`}
              findings={findings}
              use="share"
              value={share.account}
              onChange={(account) => {
                if (account !== null) {
                  set(index, { ...share, account }, true);
                }
              }}
            />
          </div>
        );
      })}
      {locked ? null : (
        <div>
          <Button
            size="sm"
            variant="quiet"
            isDisabled={shares.length >= MAX_SHARES}
            onPress={() => {
              onChange([...shares, { path: "", account: first }], true);
            }}
          >
            <IconPlus aria-hidden="true" size={16} stroke={2} />
            <Trans>Add a share</Trans>
          </Button>
        </div>
      )}
    </section>
  );
}
