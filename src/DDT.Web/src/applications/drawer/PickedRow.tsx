// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { IconArrowDown, IconArrowUp, IconX } from "@tabler/icons-react";
import type { ReactNode } from "react";
import { Button as AriaButton } from "react-aria-components";

import { FieldErrorText } from "@/ui/FieldErrorText";

interface PickedRowProps {
  id: string;
  index: number;
  // Null for an application that is no longer in the library.
  name: string | null;
  note: string | null;
  extra: ((id: string, shown: string) => ReactNode) | undefined;
  isLast: boolean;
  isReadOnly: boolean;
  errors: readonly string[];
  onMove: (to: number) => void;
  onRemove: () => void;
}

const keyClass =
  "flex size-7 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover hover:text-ink pressed:bg-key-quiet-pressed disabled:cursor-default disabled:opacity-40 focus-visible:outline-2 focus-visible:outline-focus";

function RowKey(props: {
  label: string;
  isDisabled?: boolean;
  onPress: () => void;
  children: ReactNode;
}) {
  return (
    <AriaButton
      aria-label={props.label}
      isDisabled={props.isDisabled ?? false}
      className={keyClass}
      onPress={props.onPress}
    >
      {props.children}
    </AriaButton>
  );
}

// One picked application with its place in the order.
export function PickedRow(props: PickedRowProps) {
  const { id, index, name, note, extra, isLast, isReadOnly, errors, onMove, onRemove } = props;
  const { t } = useLingui();
  const number = index + 1;
  const shown = name ?? t`Not in the library`;

  return (
    <li className="flex flex-col gap-0.5 rounded-key bg-well px-2.5 py-1.5">
      <span className="flex items-center gap-2">
        <span className="w-5 shrink-0 type-data text-muted">{number}</span>
        <span className={name === null ? "flex-1 truncate text-muted" : "flex-1 truncate text-ink"}>
          {shown}
        </span>
        {note === null ? null : <span className="shrink-0 type-small text-muted">{note}</span>}
        {extra?.(id, shown)}
        {isReadOnly ? null : (
          <>
            <RowKey
              label={t`Move ${shown} up`}
              isDisabled={index === 0}
              onPress={() => {
                onMove(index - 1);
              }}
            >
              <IconArrowUp size={14} stroke={2} />
            </RowKey>
            <RowKey
              label={t`Move ${shown} down`}
              isDisabled={isLast}
              onPress={() => {
                onMove(index + 1);
              }}
            >
              <IconArrowDown size={14} stroke={2} />
            </RowKey>
            <RowKey label={t`Remove ${shown}`} onPress={onRemove}>
              <IconX size={14} stroke={2} />
            </RowKey>
          </>
        )}
      </span>
      <FieldErrorText errors={errors} />
    </li>
  );
}
