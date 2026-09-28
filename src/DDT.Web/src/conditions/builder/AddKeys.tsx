// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { IconPlus } from "@tabler/icons-react";
import { Button as AriaButton } from "react-aria-components";

import { MAX_CONDITION_DEPTH } from "../conditions";
import { subjectFor } from "../conditionSubjects";
import { newTestOf } from "../conditionValues";
import { addKey } from "./conditionKeys";
import type { RowContext } from "./rowContext";

export function AddKeys({
  path,
  depth,
  context,
}: {
  path: readonly number[];
  depth: number;
  context: RowContext;
}) {
  const { subjects, onChange } = context;
  const first = subjectFor(subjects, "Model");

  return (
    <div className="flex flex-wrap gap-x-4 gap-y-1">
      <AriaButton
        className={addKey}
        onPress={() => {
          onChange(path, { op: "add", part: newTestOf(first) });
        }}
      >
        <IconPlus aria-hidden="true" size={14} stroke={2} />
        <Trans>Add a condition</Trans>
      </AriaButton>
      <AriaButton
        className={addKey}
        isDisabled={depth >= MAX_CONDITION_DEPTH}
        onPress={() => {
          onChange(path, { op: "add", part: { kind: "any", parts: [newTestOf(first)] } });
        }}
      >
        <IconPlus aria-hidden="true" size={14} stroke={2} />
        <Trans>Add a group</Trans>
      </AriaButton>
    </div>
  );
}
