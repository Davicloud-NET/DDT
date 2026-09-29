// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";

import { ChoiceSetting } from "@/sequences/fields/ChoiceSetting";
import { TextSetting } from "@/sequences/fields/TextSetting";
import type { Findings } from "@/sequences/problems";
import type { TestCondition } from "@/sequences/sequenceConditions";

import type { Subject } from "../conditionSubjects";
import { ChoiceList } from "./ChoiceList";
import { MemoryValue } from "./MemoryValue";
import { choicesOf, placeholderOf, yesNoOf } from "./valueFields";

// The value of a test, with a field that fits its subject's kind of value.
export function ValueEditor({
  label,
  field,
  findings,
  subject,
  test,
  onChange,
}: {
  label: string;
  field: string;
  findings: Findings;
  subject: Subject;
  test: TestCondition;
  onChange: (value: string) => void;
}) {
  const { t } = useLingui();
  const hidden = <span className="sr-only">{label}</span>;
  const list = test.operator === "In";

  if (subject.kind === "yesNo") {
    return (
      <ChoiceSetting
        label={hidden}
        field={field}
        findings={findings}
        value={yesNoOf(test.value)}
        choices={[
          { id: "true", label: t`yes` },
          { id: "false", label: t`no` },
        ]}
        onChange={onChange}
      />
    );
  }

  if (subject.kind === "oneOf" && list) {
    return (
      <ChoiceList
        label={label}
        field={field}
        subject={subject}
        value={test.value}
        onChange={onChange}
      />
    );
  }

  // Only Equals and NotEquals pick one of the choices. The other operators take part of a value or a pattern, typed
  // like text.
  if (subject.kind === "oneOf" && (test.operator === "Equals" || test.operator === "NotEquals")) {
    return (
      <ChoiceSetting
        label={hidden}
        field={field}
        findings={findings}
        value={test.value === "" ? null : test.value}
        placeholder={t`Choose`}
        choices={choicesOf(subject, test.value)}
        onChange={onChange}
      />
    );
  }

  if (subject.kind === "memory") {
    return (
      <MemoryValue
        label={label}
        field={field}
        findings={findings}
        value={test.value}
        onChange={onChange}
      />
    );
  }

  const placeholder = placeholderOf(test.operator === "InSubnet" ? "network" : subject.kind, list);

  return (
    <TextSetting
      label={hidden}
      field={field}
      findings={findings}
      mono={subject.kind !== "text" && subject.kind !== "oneOf"}
      value={test.value}
      {...(placeholder === undefined ? {} : { placeholder })}
      onChange={onChange}
    />
  );
}
