// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import type { ApiError } from "@/lib/api";
import type {
  AccountDestination,
  InputChoice,
  InputDeclaration,
  InputKind,
} from "@/sequences/sequences";
import type { ResolvedValue } from "@/values/values";

// The questions a sequence asks before its run starts, as the web asks them. They're asked when a run is assigned or
// approved, and on the machine's page while a run waits for its answers.

// The server's AgentInput: an input as the console or the web asks it.
export interface AgentInput {
  name: string;
  label: string;
  help: string | null;
  kind: InputKind;
  choices: InputChoice[];
  default: string | null;
  required: boolean;
  maxLength: number | null;
}

// The server's InputAnswer. Every kind uses value, except Account, which uses userName and password. A MultiChoice
// answer is its values separated by semicolons, and a YesNo answer is "true" or "false".
export interface InputAnswer {
  name: string;
  value: string | null;
  userName?: string | null;
  password?: string | null;
}

// An input as a form asks it. account is where an Account input's account may be used, if the page knows it.
export interface AskedInput extends AgentInput {
  account: AccountDestination | null;
}

// What a field holds while it is edited.
export interface AnswerDraft {
  value: string;
  // The values of a MultiChoice input.
  values: string[];
  userName: string;
  password: string;
}

export type AnswerDrafts = Record<string, AnswerDraft>;

// The inputs a sequence asks on the web, in its order.
export function webInputs(inputs: readonly InputDeclaration[] | null | undefined): AskedInput[] {
  return (inputs ?? [])
    .filter((input) => input.askAt !== "Machine")
    .map((input) => ({
      name: input.name,
      label: input.label,
      help: input.help,
      kind: input.kind,
      choices: input.choices,
      default: input.default,
      required: input.required,
      maxLength: input.maxLength,
      account: input.account,
    }));
}

// A run's input as the server lists it, with its account's destination from the sequence the run was given.
export function askedInput(
  input: AgentInput,
  declarations: readonly InputDeclaration[] | null | undefined,
): AskedInput {
  const declared = (declarations ?? []).find((candidate) => sameName(candidate.name, input.name));

  return { ...input, account: declared?.account ?? null };
}

// Input names ignore case, as the server's do.
export function sameName(a: string, b: string): boolean {
  return a.localeCompare(b, undefined, { sensitivity: "accent" }) === 0;
}

function listOf(value: string): string[] {
  return value
    .split(";")
    .map((item) => item.trim())
    .filter((item) => item !== "");
}

// What each field starts with: the default the server worked out for this machine if it sent one, or else the
// input's own default. A password never has a default.
export function initialDrafts(
  inputs: readonly AskedInput[],
  defaults: readonly ResolvedValue[] = [],
): AnswerDrafts {
  return Object.fromEntries(
    inputs.map((input) => {
      const worked = defaults.find(
        (value) => sameName(value.name, input.name) && !value.overridden,
      );
      const start = worked?.value ?? input.default ?? "";

      return [
        input.name,
        {
          value: input.kind === "Account" ? "" : start,
          values: input.kind === "MultiChoice" ? listOf(start) : [],
          userName: input.kind === "Account" ? start : "",
          password: "",
        },
      ];
    }),
  );
}

// The default a field was filled in with, if the server worked one out for this machine.
export function prefilledBy(
  input: AskedInput,
  defaults: readonly ResolvedValue[],
): ResolvedValue | null {
  return (
    defaults.find(
      (value) =>
        sameName(value.name, input.name) &&
        !value.overridden &&
        value.value !== null &&
        value.value !== "",
    ) ?? null
  );
}

function empty(input: AskedInput, draft: AnswerDraft | undefined): boolean {
  switch (input.kind) {
    case "MultiChoice":
      return (draft?.values ?? []).length === 0;
    case "Account":
      return (draft?.userName.trim() ?? "") === "" || (draft?.password ?? "") === "";
    default:
      return (draft?.value.trim() ?? "") === "";
  }
}

// The answers to send: every field that holds something. missingAnswers reports a required input left empty.
export function answersOf(inputs: readonly AskedInput[], drafts: AnswerDrafts): InputAnswer[] {
  return inputs
    .filter((input) => !empty(input, drafts[input.name]))
    .map((input) => {
      const draft = drafts[input.name];

      switch (input.kind) {
        case "MultiChoice":
          return { name: input.name, value: (draft?.values ?? []).join(";") };
        case "Account":
          return {
            name: input.name,
            value: null,
            userName: draft?.userName.trim() ?? "",
            password: draft?.password ?? "",
          };
        default:
          return { name: input.name, value: draft?.value.trim() ?? "" };
      }
    });
}

// What blocks sending the answers, keyed by input name: each required input without an answer.
export function missingAnswers(
  inputs: readonly AskedInput[],
  drafts: AnswerDrafts,
): Record<string, string> {
  return Object.fromEntries(
    inputs
      .filter((input) => input.required && empty(input, drafts[input.name]))
      .map((input) => [
        input.name,
        input.kind === "Account"
          ? t`Enter the user name and the password.`
          : input.kind === "Choice" || input.kind === "MultiChoice" || input.kind === "YesNo"
            ? t`Choose an answer.`
            : t`Enter an answer.`,
      ]),
  );
}

// The server's refusal of the answers, keyed by input name. The server names each field "answers.<name>".
export function answerErrors(error: ApiError | null | undefined): Record<string, string> {
  const errors = error?.problem?.errors ?? {};
  const found: Record<string, string> = {};

  for (const [field, messages] of Object.entries(errors)) {
    const match = /^answers\.(.+)$/i.exec(field);
    const [first] = messages;

    if (match?.[1] !== undefined && first !== undefined) {
      found[match[1]] = first;
    }
  }

  return found;
}

// The error of one input among errors keyed by name, whatever the case of the key.
export function errorFor(errors: Record<string, string>, name: string): string | null {
  const key = Object.keys(errors).find((candidate) => sameName(candidate, name));

  return key === undefined ? null : (errors[key] ?? null);
}

// Whether the server's refusal names any field of the answers, so the form shows it there and not as a notice.
export function hasAnswerErrors(error: ApiError | null | undefined): boolean {
  return Object.keys(answerErrors(error)).length > 0;
}

// Where an Account input's account may be used, in words.
export function destinationText(destination: AccountDestination | null): string | null {
  if (destination === null) {
    return null;
  }

  const domain = destination.domain;
  const hosts = destination.hosts.join(", ");
  const parts: string[] = [];

  if (domain !== null && domain !== "") {
    parts.push(t`It joins the domain ${domain}.`);
  }

  if (destination.hosts.length > 0) {
    parts.push(t`It connects to shares on ${hosts}.`);
  }

  if (destination.runAs) {
    parts.push(t`Scripts may run as it.`);
  }

  return parts.length === 0 ? null : parts.join(" ");
}
