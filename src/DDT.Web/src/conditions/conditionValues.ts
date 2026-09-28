// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import type { ConditionOperator, TestCondition } from "@/sequences/sequenceConditions";
import { operatorTakesValue } from "@/sequences/steps";

import { operatorsFor } from "./conditionOperators";
import type { Subject, ValueKind } from "./conditionSubjects";

// A list of values, as In takes them: separated by semicolons.
export function listOf(value: string): string[] {
  return value
    .split(";")
    .map((item) => item.trim())
    .filter((item) => item !== "");
}

// Memory is stored in MB and entered in GB, to two places, a list as In takes it item by item.
export function gigabytesOf(megabytes: string): string {
  if (megabytes.includes(";")) {
    return listOf(megabytes).map(gigabytesOf).join("; ");
  }

  const number = Number(megabytes);

  if (megabytes.trim() === "" || !Number.isFinite(number)) {
    return megabytes;
  }

  return String(Math.round((number / 1024) * 100) / 100);
}

export function megabytesOf(gigabytes: string): string {
  if (gigabytes.includes(";")) {
    return gigabytes
      .split(";")
      .map((item) => megabytesOf(item.trim()))
      .join("; ");
  }

  const typed = gigabytes.trim().replace(",", ".");
  const number = Number(typed);

  if (typed === "" || !Number.isFinite(number)) {
    return gigabytes;
  }

  return String(Math.round(number * 1024));
}

const ipv4Pattern = /^(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}$/;

export function isIpv4(value: string): boolean {
  return ipv4Pattern.test(value.trim());
}

export function isNetwork(value: string): boolean {
  const [address = "", prefix, ...rest] = value.trim().split("/");

  return (
    rest.length === 0 &&
    prefix !== undefined &&
    /^\d{1,2}$/.test(prefix) &&
    Number(prefix) <= 32 &&
    isIpv4(address)
  );
}

// Whole bytes of hex digits, written with or without colons, dashes or dots.
export function isMac(value: string, part: boolean): boolean {
  const digits = value.replace(/[:\-. ]/g, "");

  return (
    /^[0-9A-Fa-f]+$/.test(digits) &&
    digits.length % 2 === 0 &&
    (part ? digits.length <= 12 : digits.length === 12)
  );
}

// What is wrong with a value as it is typed, for the field to say before the server does; null when nothing is.
export function valueProblem(
  kind: ValueKind,
  operator: ConditionOperator,
  value: string,
): string | null {
  if (!operatorTakesValue(operator) || value.trim() === "") {
    return null;
  }

  const items = operator === "In" ? listOf(value) : [value.trim()];
  const every = (check: (item: string) => boolean) => items.every(check);
  // An address or a network is compared whole; the other operators take a part of one, or a pattern.
  const whole = operator === "Equals" || operator === "NotEquals" || operator === "In";

  if (operator === "InSubnet") {
    return every(isNetwork) ? null : t`Enter a network such as 10.20.0.0/16.`;
  }

  switch (kind) {
    case "number":
    case "memory":
      return every((item) => Number.isFinite(Number(item.replace(",", "."))))
        ? null
        : t`Enter a number.`;
    case "ipv4":
      return !whole || every(isIpv4) ? null : t`Enter an IPv4 address such as 10.0.4.51.`;
    case "network":
      return !whole || every(isNetwork) ? null : t`Enter a network such as 10.20.4.0/24.`;
    case "mac":
      return every((item) => isMac(item, !whole))
        ? null
        : t`Enter a MAC address such as 00:15:5D:01:02:03.`;
    default:
      return null;
  }
}

// A new test of the subject, with the first operator that fits it and a value to start from.
export function newTestOf(subject: Subject): TestCondition {
  const operator = operatorsFor(subject.kind)[0] ?? "Equals";

  return {
    kind: "test",
    variable: subject.name,
    operator,
    value:
      subject.kind === "yesNo"
        ? "true"
        : subject.kind === "oneOf"
          ? (subject.choices[0]?.value ?? "")
          : "",
  };
}

// The test after its subject changed: the operator stays where it fits the new kind, and the value where the kind
// stayed the same.
export function withSubject(test: TestCondition, from: Subject, to: Subject): TestCondition {
  const fits = operatorsFor(to.kind).includes(test.operator);
  const fresh = newTestOf(to);

  return {
    ...test,
    variable: to.name,
    operator: fits ? test.operator : fresh.operator,
    value: from.kind === to.kind && to.kind !== "oneOf" ? test.value : fresh.value,
  };
}
