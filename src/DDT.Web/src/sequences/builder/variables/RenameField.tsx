// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useContext, useState } from "react";

import { Button } from "@/ui/Button";
import { TextField } from "@/ui/TextField";

import { EditorLock } from "../../editorLock";
import { namePattern } from "../../flow/declarationEdits";
import type { FlowEdit } from "../../flow/flowEdits";
import type { SequenceDraft } from "../../sequenceDraft";
import type { OpenRow } from "./declarationRows";

// A name is changed everywhere at once: in templates, conditions, Set variable steps and account references, and for
// the variable and the input of that name alike. The field holds what is typed until then.
export function RenameField({
  list,
  index,
  name,
  draft,
  onEdit,
}: {
  list: OpenRow["list"];
  index: number;
  name: string;
  draft: SequenceDraft;
  onEdit: (edit: FlowEdit) => void;
}) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);
  const [typed, setTyped] = useState(name);
  const next = typed.trim();
  const valid = namePattern.test(next);
  const lower = next.toLowerCase();
  const taken =
    lower !== name.toLowerCase() &&
    [...draft.variables, ...draft.inputs].some((item) => item.name.toLowerCase() === lower);
  const field = `${list}[${String(index)}].name`;
  const placeholder = `{{${name}}}`;

  return (
    <div data-field={field} className="flex flex-col gap-2">
      <TextField
        label={<Trans>Name</Trans>}
        value={locked ? name : typed}
        isReadOnly={locked}
        mono
        spellCheck="false"
        autoComplete="off"
        onChange={setTyped}
        isInvalid={!valid || taken}
        errorMessage={
          !valid
            ? t`A name starts with a letter and holds only letters, digits and _, at most 64 in all.`
            : t`The sequence already has a variable or an input of that name.`
        }
        hint={t`Templates use it as ${placeholder}.`}
      />
      {locked || next === name ? null : (
        <div className="flex flex-wrap gap-2">
          <Button
            size="sm"
            isDisabled={!valid || taken}
            onPress={() => {
              onEdit({ type: "renameVariable", from: name, to: next });
            }}
          >
            <Trans>Rename everywhere</Trans>
          </Button>
          <Button
            size="sm"
            variant="quiet"
            onPress={() => {
              setTyped(name);
            }}
          >
            <Trans>Keep {name}</Trans>
          </Button>
        </div>
      )}
    </div>
  );
}
