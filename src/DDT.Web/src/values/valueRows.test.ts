// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import type { RunInputView } from "@/deployments/deployments";
import type { InputDeclaration } from "@/sequences/declarations";
import type { SequenceDefinition } from "@/sequences/sequences";

import { valueRows } from "./valueRows";
import type { ResolvedValue } from "./values";

const office = "0197a3c0-0000-7000-8000-000000000001";
const sevenZip = "0197a3c0-0000-7000-8000-000000000002";
const gone = "0197a3c0-0000-7000-8000-000000000003";

function declared(kind: InputDeclaration["kind"]): InputDeclaration {
  return {
    name: "Extras",
    label: "Extras",
    help: null,
    kind,
    choices: [],
    default: null,
    required: false,
    maxLength: null,
    askAt: "Both",
    account: null,
  };
}

const answer: ResolvedValue = {
  name: "extras",
  value: `${office};${sevenZip.toUpperCase()};${gone}`,
  source: "Input",
  sourceId: null,
  sourceName: null,
  overridden: false,
};

// The server asks an Applications input as a MultiChoice of the entries the library still has.
const asked: RunInputView = {
  input: {
    name: "Extras",
    label: "Extras",
    help: null,
    kind: "MultiChoice",
    choices: [
      { value: office, label: "Microsoft 365 Apps" },
      { value: sevenZip, label: "7-Zip" },
    ],
    default: null,
    required: false,
    maxLength: null,
  },
  answered: true,
  answeredBy: "admin",
};

function rows(kind: InputDeclaration["kind"]) {
  const definition: SequenceDefinition = { version: 6, steps: [], inputs: [declared(kind)] };

  return valueRows({ values: [answer], definition, inputs: [asked] });
}

describe("valueRows", () => {
  it("names the applications an Applications input's answer holds, and keeps an id the library lost", () => {
    expect(rows("Applications")[0]?.value).toBe(`Microsoft 365 Apps; 7-Zip; ${gone}`);
  });

  it("leaves the answer of any other input as it is", () => {
    expect(rows("MultiChoice")[0]?.value).toBe(answer.value);
  });
});
