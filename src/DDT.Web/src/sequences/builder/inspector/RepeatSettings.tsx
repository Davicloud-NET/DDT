// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { ConditionBuilder } from "@/conditions/ConditionBuilder";
import type { Subject } from "@/conditions/conditionSubjects";

import { FlagSetting } from "../../fields/FlagSetting";
import { NumberSetting } from "../../fields/NumberSetting";
import type { Findings } from "../../problems";
import type { SequenceEdit, StepPatch } from "../../sequenceEdits";
import type { RepeatStep } from "../../sequences";

// When a Repeat stops: its until, and the limit of times with what happens when it is reached.
export function RepeatSettings({
  node,
  subjects,
  findings,
  onEdit,
  change,
}: {
  node: RepeatStep;
  subjects: readonly Subject[];
  findings: Findings;
  onEdit: (edit: SequenceEdit) => void;
  change: (patch: StepPatch) => void;
}) {
  return (
    <>
      <ConditionBuilder
        label={<Trans>Repeat until</Trans>}
        hint={<Trans>Tested after each time the steps inside ran.</Trans>}
        use="until"
        field="until"
        value={node.until}
        subjects={subjects}
        findings={findings}
        onChange={(path, condition) => {
          onEdit({
            type: "editCondition",
            id: node.id,
            field: "until",
            path,
            change: condition,
          });
        }}
      />
      <NumberSetting
        label={<Trans>At most this many times</Trans>}
        field="maxTimes"
        findings={findings}
        hint={<Trans>From 1 to 100.</Trans>}
        minValue={1}
        maxValue={100}
        value={node.maxTimes}
        onChange={(maxTimes) => {
          change({ maxTimes });
        }}
      />
      <FlagSetting
        label={<Trans>Go on when the limit is reached</Trans>}
        field="goOnAtLimit"
        findings={findings}
        hint={<Trans>Otherwise the repeat fails when its condition still does not hold.</Trans>}
        value={node.goOnAtLimit}
        onChange={(goOnAtLimit) => {
          change({ goOnAtLimit });
        }}
      />
    </>
  );
}
