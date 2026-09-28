// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { nodeTitle } from "../../flow/flowLabels";
import type { TreeEntry } from "../../flow/flowTree";
import type { StepKind } from "../../sequences";
import { AddNodeMenu } from "../AddNodeMenu";

interface AddStepMenuProps {
  selected: TreeEntry | undefined;
  onAdd: (kind: StepKind) => void;
}

// The outline's key that adds a step after the chosen node, or at the end.
export function AddStepMenu({ selected, onAdd }: AddStepMenuProps) {
  const { t } = useLingui();
  const selectedTitle = selected === undefined ? "" : nodeTitle(selected.node);

  return (
    <AddNodeMenu
      label={<Trans>Add a step</Trans>}
      fullLabel={
        selected === undefined ? t`Add a step at the end` : t`Add a step after ${selectedTitle}`
      }
      onAdd={onAdd}
    />
  );
}
