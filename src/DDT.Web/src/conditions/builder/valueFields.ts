// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { Subject, ValueKind } from "../conditionSubjects";

// An example value for a test of this kind. For In, which takes several values, it's a list.
export function placeholderOf(kind: ValueKind, list: boolean): string | undefined {
  const example = {
    text: "Latitude",
    number: "4",
    memory: "16",
    yesNo: undefined,
    ipv4: "10.0.4.51",
    network: "10.20.4.0/24",
    mac: "00:15:5D:01:02:03",
    oneOf: undefined,
  }[kind];

  return example === undefined ? undefined : list ? `${example}; …` : example;
}

// No can be written as false, no or 0. Anything else shows as yes.
export function yesNoOf(value: string): "true" | "false" {
  const lower = value.trim().toLowerCase();

  return ["false", "no", "0"].includes(lower) ? "false" : "true";
}

// A value that isn't in the list, such as one typed earlier, stays among the choices so it still shows.
export function choicesOf(subject: Subject, value: string): { id: string; label: string }[] {
  const known = subject.choices.some((choice) => choice.value === value);

  return [
    ...(known || value === "" ? [] : [{ id: value, label: value }]),
    ...subject.choices.map((choice) => ({ id: choice.value, label: choice.label })),
  ];
}
