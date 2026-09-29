// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconX } from "@tabler/icons-react";
import { Button as AriaButton } from "react-aria-components";

import { ChoiceSetting } from "@/sequences/fields/ChoiceSetting";
import type { ConditionGroup } from "@/sequences/sequenceConditions";
import { cx } from "@/ui/cx";

import { groupKinds, groupLabel } from "../conditions";
import { AddKeys } from "./AddKeys";
import { addKey } from "./conditionKeys";
import type { RowContext } from "./rowContext";
import { TestRow } from "./TestRow";

// A group with its choice of all, at least one or none, its rows, and the buttons that add to it. virtual means a single
// test shown as a group of one. Choosing another kind puts the test into a group of that kind.
export function GroupRows({
  group,
  path,
  depth,
  virtual = false,
  context,
}: {
  group: ConditionGroup;
  path: readonly number[];
  depth: number;
  virtual?: boolean;
  context: RowContext;
}) {
  const { t } = useLingui();
  const { place, findings, onChange, locked } = context;
  const where = place(path);

  return (
    <>
      <ChoiceSetting
        label={<span className="sr-only">{t`How the conditions combine`}</span>}
        field={virtual || path.length === 0 ? `${where}.kind` : where}
        findings={findings}
        className="w-60 max-w-full"
        value={group.kind}
        choices={groupKinds.map((kind) => ({ id: kind, label: groupLabel(kind) }))}
        onChange={(kind) => {
          if (kind === group.kind) {
            return;
          }

          const chosen = kind as ConditionGroup["kind"];

          onChange(path, virtual ? { op: "wrap", kind: chosen } : { op: "group", kind: chosen });
        }}
      />
      {group.parts.map((part, index) => {
        const at = virtual ? path : [...path, index];

        return part.kind === "test" ? (
          <TestRow key={index} test={part} path={at} context={context} />
        ) : (
          <div
            key={index}
            className="flex flex-col gap-2.5 rounded-key bg-panel p-3 shadow-[inset_0_0_0_1px_var(--color-line-soft)]"
          >
            <GroupRows group={part} path={at} depth={depth + 1} context={context} />
            {locked ? null : (
              <div className="flex justify-end">
                <AriaButton
                  className={cx(addKey, "text-fail-text")}
                  onPress={() => {
                    context.remove(at);
                  }}
                >
                  <IconX aria-hidden="true" size={14} stroke={2} />
                  <Trans>Remove this group</Trans>
                </AriaButton>
              </div>
            )}
          </div>
        );
      })}
      {locked ? null : <AddKeys path={path} depth={depth} context={context} />}
    </>
  );
}
