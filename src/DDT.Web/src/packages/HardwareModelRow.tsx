// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconX } from "@tabler/icons-react";
import { Button as AriaButton } from "react-aria-components";

import type { HardwareModelCount } from "@/machines/machines";
import { ComboBox, ListBoxItem } from "@/ui/Select";

import type { TargetRow } from "./packageTargets";

interface HardwareModelRowProps {
  row: TargetRow;
  // Counts from 1, for the remove button's aria-label.
  position: number;
  manufacturers: readonly string[];
  // The reported models of the row's manufacturer.
  models: readonly HardwareModelCount[];
  onChange: (change: Partial<Omit<TargetRow, "key">>) => void;
  onRemove: () => void;
}

// One hardware model a driver package is for. Both fields take any text and suggest what machines reported.
export function HardwareModelRow(props: HardwareModelRowProps) {
  const { row, position, manufacturers, models, onChange, onRemove } = props;
  const { t } = useLingui();

  return (
    <div className="grid grid-cols-[1fr_1.4fr_auto] items-end gap-2">
      <ComboBox
        label={<Trans>Manufacturer</Trans>}
        allowsCustomValue
        inputValue={row.manufacturer}
        onInputChange={(manufacturer) => {
          onChange({ manufacturer });
        }}
      >
        {manufacturers.map((manufacturer) => (
          <ListBoxItem key={manufacturer} id={manufacturer} textValue={manufacturer}>
            {manufacturer}
          </ListBoxItem>
        ))}
      </ComboBox>
      <ComboBox
        label={<Trans>Model</Trans>}
        allowsCustomValue
        inputValue={row.model}
        onInputChange={(model) => {
          onChange({ model });
        }}
      >
        {models.map((model) => (
          <ListBoxItem
            key={`${model.manufacturer ?? ""}/${model.model}`}
            id={`${model.manufacturer ?? ""}/${model.model}`}
            textValue={model.model}
          >
            {model.model}
          </ListBoxItem>
        ))}
      </ComboBox>
      <AriaButton
        aria-label={t`Remove model ${position}`}
        onPress={onRemove}
        className="mb-0.5 flex size-9 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed hover:text-ink focus-visible:outline-2 focus-visible:outline-focus"
      >
        <IconX size={16} stroke={2} />
      </AriaButton>
    </div>
  );
}
