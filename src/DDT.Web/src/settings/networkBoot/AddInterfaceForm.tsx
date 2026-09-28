// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Button } from "@/ui/Button";
import { TextField } from "@/ui/TextField";

// Adds interface names or addresses that no host reported. withEntries splits several entries at the commas.
export function AddInterfaceForm({
  typed,
  onType,
  onAdd,
}: {
  typed: string;
  onType: (typed: string) => void;
  onAdd: () => void;
}) {
  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        onAdd();
      }}
      className="flex flex-col gap-1.5"
    >
      <span className="flex flex-wrap items-end gap-2">
        <TextField
          label={<Trans>Another interface name or address</Trans>}
          mono
          value={typed}
          onChange={onType}
          className="max-w-80 flex-1"
        />
        <Button type="submit" isDisabled={typed.trim() === ""}>
          <Trans>Add</Trans>
        </Button>
      </span>
      <span className="type-small text-muted">
        <Trans>
          For an adapter no host reported, such as one still to come. An IPv4 address picks the
          interface that has it.
        </Trans>
      </span>
    </form>
  );
}
