// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { IconPlus } from "@tabler/icons-react";

import { Button } from "@/ui/Button";

import type { FlowEdit } from "../../flow/flowEdits";
import type { SequenceDraft } from "../../sequenceDraft";
import type { OpenRow } from "./declarationRows";
import { newInput, newVariable } from "./newDeclarations";

// Adds a variable or an input with a name that isn't taken yet. onAdded opens its row.
export function AddDeclarationKeys({
  draft,
  onEdit,
  onAdded,
}: {
  draft: SequenceDraft;
  onEdit: (edit: FlowEdit) => void;
  onAdded: (list: OpenRow["list"], index: number) => void;
}) {
  return (
    <div className="flex flex-wrap gap-2">
      <Button
        size="sm"
        onPress={() => {
          onEdit({ type: "addVariable", variable: newVariable(draft) });
          onAdded("variables", draft.variables.length);
        }}
      >
        <IconPlus aria-hidden="true" size={16} stroke={2} />
        <Trans>Add a variable</Trans>
      </Button>
      <Button
        size="sm"
        variant="quiet"
        onPress={() => {
          onEdit({ type: "addInput", input: newInput(draft) });
          onAdded("inputs", draft.inputs.length);
        }}
      >
        <IconPlus aria-hidden="true" size={16} stroke={2} />
        <Trans>Add an input</Trans>
      </Button>
    </div>
  );
}
