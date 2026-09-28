// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Button } from "@/ui/Button";
import { ListBoxItem, Select } from "@/ui/Select";

import { architectureDescription } from "../networkBoot";

// Picks an architecture without a boot target and adds one for it.
export function AddBootTarget({
  architecture,
  available,
  onChoose,
  onAdd,
}: {
  architecture: string;
  available: string[];
  onChoose: (architecture: string | null) => void;
  onAdd: () => void;
}) {
  const { i18n } = useLingui();

  return (
    <div className="flex flex-wrap items-end gap-2">
      <Select
        label={<Trans>Architecture</Trans>}
        value={architecture}
        onChange={(key) => {
          onChoose(key === null ? null : String(key));
        }}
        className="w-80 max-w-full"
      >
        {available.map((name) => {
          const description = architectureDescription(name);

          return (
            <ListBoxItem
              key={name}
              id={name}
              textValue={name}
              {...(description === null ? {} : { description: i18n._(description) })}
            >
              {name}
            </ListBoxItem>
          );
        })}
      </Select>
      <Button onPress={onAdd}>
        <Trans>Add boot target</Trans>
      </Button>
    </div>
  );
}
