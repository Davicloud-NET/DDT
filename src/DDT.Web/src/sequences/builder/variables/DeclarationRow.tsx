// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconChevronDown, IconChevronRight, IconTrash } from "@tabler/icons-react";
import { useContext, type ReactNode } from "react";
import { Button as AriaButton } from "react-aria-components";

import { buttonClass } from "@/ui/buttonClass";

import { EditorLock } from "../../editorLock";
import type { FlowEdit } from "../../flow/flowEdits";
import { usedBy } from "../../flow/references";
import type { SequenceDraft } from "../../sequenceDraft";
import type { OpenRow } from "./declarationRows";
import { MoveKeys } from "./MoveKeys";
import { RenameField } from "./RenameField";
import { UsedByList } from "./UsedByList";

interface DeclarationRowProps {
  list: OpenRow["list"];
  index: number;
  count: number;
  name: string;
  tag: ReactNode;
  draft: SequenceDraft;
  isOpen: boolean;
  onToggle: () => void;
  onEdit: (edit: FlowEdit) => void;
  onGoToNode: (id: string) => void;
  // The fields of the variable or the input.
  children: ReactNode;
}

// A variable or an input, closed to its name and what uses it, open to its fields.
export function DeclarationRow({
  list,
  index,
  count,
  name,
  tag,
  draft,
  isOpen,
  onToggle,
  onEdit,
  onGoToNode,
  children,
}: DeclarationRowProps) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);
  const users = usedBy(draft, name);
  const uses = users.length;
  const Chevron = isOpen ? IconChevronDown : IconChevronRight;
  const move = (to: number) => {
    onEdit(
      list === "variables" ? { type: "moveVariable", name, to } : { type: "moveInput", name, to },
    );
  };

  return (
    <li className="rounded-key bg-raised shadow-[inset_0_0_0_1px_var(--color-line-soft)]">
      <div className="flex items-center gap-1 px-1.5 py-1.5">
        <AriaButton
          aria-expanded={isOpen}
          className="flex min-w-0 flex-1 cursor-pointer items-center gap-2 rounded-key px-1.5 py-1 text-left motion-colors outline-none hover:bg-hover focus-visible:outline-2 focus-visible:outline-focus"
          onPress={onToggle}
        >
          <Chevron aria-hidden="true" size={14} stroke={2} className="shrink-0 text-muted" />
          <span className="flex min-w-0 flex-col">
            <span className="truncate type-data text-ink">{name}</span>
            <span className="type-small text-muted">
              {tag}
              {" · "}
              {uses === 0
                ? t`Not used yet`
                : plural(uses, { one: "Used by # node", other: "Used by # nodes" })}
            </span>
          </span>
        </AriaButton>
        {locked ? null : <MoveKeys name={name} index={index} count={count} onMove={move} />}
      </div>
      {isOpen ? (
        <div className="flex flex-col gap-4 border-t border-line-soft px-3 pt-3 pb-4">
          <RenameField list={list} index={index} name={name} draft={draft} onEdit={onEdit} />
          {children}
          {uses > 0 ? <UsedByList users={users} draft={draft} onGoToNode={onGoToNode} /> : null}
          {locked ? null : (
            <div>
              <AriaButton
                aria-label={t`Remove ${name}`}
                className={buttonClass("quiet", "sm", "text-fail-text hover:text-fail-text")}
                onPress={() => {
                  onEdit(
                    list === "variables"
                      ? { type: "removeVariable", name }
                      : { type: "removeInput", name },
                  );
                }}
              >
                <IconTrash aria-hidden="true" size={16} stroke={2} />
                <Trans>Remove</Trans>
              </AriaButton>
            </div>
          )}
        </div>
      ) : null}
    </li>
  );
}
