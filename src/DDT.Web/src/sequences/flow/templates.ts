// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { serverText, type ServerArguments } from "@/lib/serverText";

// The server's ValueTemplate, mirrored for the template field's completion and preview, and held to it by the cases
// in src/test/fixtures/template-cases.json: text with placeholders such as PC-{{SerialNumber|alnum|right:12}}, a name
// that ignores case and filters after bars, done from left to right. Text between double braces that is not a name
// followed by filters, such as Jinja's {{ v1.local_hostname }}, is not a placeholder and stays as it is.

export const TEMPLATE_FILTERS = ["upper", "lower", "trim", "alnum", "left", "right"] as const;

// The most characters left:n and right:n take.
export const MAX_COUNT = 1024;

// The filters as a person writes them, for a message that lists them.
const filterList = "upper, lower, trim, alnum, left:n, right:n";

export interface TemplateFilter {
  name: string;
  // What follows the colon, such as 12 in right:12; null is no colon.
  argument: string | null;
}

export interface TemplatePlaceholder {
  // The whole placeholder, braces included, and where it is in the text.
  text: string;
  start: number;
  name: string;
  filters: TemplateFilter[];
}

export type TemplateProblemKind =
  "unknownName" | "unknownFilter" | "filterNeedsCount" | "filterTakesNoCount" | "missingValue";

export interface TemplateProblem {
  kind: TemplateProblemKind;
  placeholder: string;
  name: string;
  filter: string | null;
  // The server's message for it: its code, its values, and its English.
  code: string;
  args: ServerArguments;
  message: string;
}

export interface ParsedTemplate {
  placeholders: TemplatePlaceholder[];
  // Every name the template uses, once, ignoring case, in the order they first appear.
  names: string[];
  problems: TemplateProblem[];
}

// A name, then filters, each after a bar and holding no brace or bar.
const placeholderPattern = /\{\{\s*([A-Za-z][A-Za-z0-9_]*)(\s*(?:\|[^{}|]*)*)\}\}/g;

function sameName(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase();
}

// The number of characters left:n and right:n take, or null when the argument is not a whole number of 1 to
// MAX_COUNT written in the digits 0 to 9.
export function filterCount(filter: TemplateFilter): number | null {
  const digits = filter.argument;

  if (digits === null || !/^[0-9]{1,4}$/.test(digits)) {
    return null;
  }

  const count = Number(digits);

  return count >= 1 && count <= MAX_COUNT ? count : null;
}

// The English the server says with each problem, from the same codes, so the page says it in the person's language.
function problem(
  kind: TemplateProblemKind,
  placeholder: string,
  name: string,
  filter: string | null = null,
): TemplateProblem {
  const [code, args]: [string, ServerArguments] =
    kind === "unknownName"
      ? ["valueTemplate.unknownName", { name, placeholder }]
      : kind === "unknownFilter"
        ? [
            "valueTemplate.unknownFilter",
            { filter: filter ?? "", filters: filterList, placeholder },
          ]
        : kind === "filterNeedsCount"
          ? [
              "valueTemplate.filterNeedsCount",
              { filter: filter ?? "", max: MAX_COUNT, placeholder },
            ]
          : kind === "filterTakesNoCount"
            ? ["valueTemplate.filterTakesNoCount", { filter: filter ?? "", placeholder }]
            : ["valueTemplate.noValue", { placeholder }];

  return { kind, placeholder, name, filter, code, args, message: serverText(code, args, "") };
}

function read(match: RegExpMatchArray): TemplatePlaceholder {
  const [text, name = "", rest = ""] = match;
  // The text before the first bar is the white space after the name.
  const filters = rest
    .split("|")
    .slice(1)
    .map((filter) => {
      const colon = filter.indexOf(":");

      return colon < 0
        ? { name: filter.trim(), argument: null }
        : { name: filter.slice(0, colon).trim(), argument: filter.slice(colon + 1).trim() };
    });

  return { text, start: match.index ?? 0, name, filters };
}

export function placeholdersOf(text: string): TemplatePlaceholder[] {
  return [...text.matchAll(placeholderPattern)].map(read);
}

// What is wrong with one filter, or null.
export function filterProblem(
  placeholder: TemplatePlaceholder,
  filter: TemplateFilter,
): TemplateProblem | null {
  const name = filter.name.toLowerCase();

  if (!(TEMPLATE_FILTERS as readonly string[]).includes(name)) {
    return problem("unknownFilter", placeholder.text, placeholder.name, filter.name);
  }

  const counted = name === "left" || name === "right";

  if (counted ? filterCount(filter) !== null : filter.argument === null) {
    return null;
  }

  return problem(
    counted ? "filterNeedsCount" : "filterTakesNoCount",
    placeholder.text,
    placeholder.name,
    name,
  );
}

// known says which names the caller knows, ignoring case; without it every name is known. Filter problems are always
// reported. Each problem is reported once.
export function parseTemplate(text: string, known?: (name: string) => boolean): ParsedTemplate {
  const placeholders = placeholdersOf(text);
  const problems: TemplateProblem[] = [];
  const add = (found: TemplateProblem | null) => {
    if (
      found !== null &&
      !problems.some(
        (other) =>
          other.kind === found.kind &&
          other.placeholder === found.placeholder &&
          other.name === found.name &&
          other.filter === found.filter,
      )
    ) {
      problems.push(found);
    }
  };

  for (const placeholder of placeholders) {
    if (known !== undefined && !known(placeholder.name)) {
      add(problem("unknownName", placeholder.text, placeholder.name));
    }

    for (const filter of placeholder.filters) {
      add(filterProblem(placeholder, filter));
    }
  }

  const names: string[] = [];

  for (const { name } of placeholders) {
    if (!names.some((other) => sameName(other, name))) {
      names.push(name);
    }
  }

  return { placeholders, names, problems };
}

// .NET's ToUpperInvariant and ToLowerInvariant change one character into one, where JavaScript makes ß into SS.
function eachChar(value: string, change: (char: string) => string): string {
  return Array.from(value, (char) => {
    const changed = change(char);

    return changed.length === char.length ? changed : char;
  }).join("");
}

// The value with the placeholder's filters done, which must have no problems.
export function applyFilters(placeholder: TemplatePlaceholder, value: string): string {
  let result = value;

  for (const filter of placeholder.filters) {
    const count = filterCount(filter) ?? 0;

    switch (filter.name.toLowerCase()) {
      case "upper":
        result = eachChar(result, (char) => char.toUpperCase());
        break;
      case "lower":
        result = eachChar(result, (char) => char.toLowerCase());
        break;
      case "trim":
        result = result.trim();
        break;
      case "alnum":
        result = result.replace(/[^A-Za-z0-9]/g, "");
        break;
      case "left":
        result = result.length > count ? result.slice(0, count) : result;
        break;
      case "right":
        result = result.length > count ? result.slice(result.length - count) : result;
        break;
    }
  }

  return result;
}

export type Rendered = { output: string; error: null } | { output: null; error: TemplateProblem };

// The text with each placeholder's value, filtered. values gives a name's value, or null when there is none, which is
// a problem, as a filter written wrong is; rendering stops at the first.
export function renderTemplate(text: string, values: (name: string) => string | null): Rendered {
  let output = "";
  let copied = 0;

  for (const placeholder of placeholdersOf(text)) {
    const wrong = placeholder.filters
      .map((filter) => filterProblem(placeholder, filter))
      .find((found) => found !== null);

    if (wrong !== undefined) {
      return { output: null, error: wrong };
    }

    const value = values(placeholder.name);

    if (value === null) {
      return { output: null, error: problem("missingValue", placeholder.text, placeholder.name) };
    }

    output += text.slice(copied, placeholder.start) + applyFilters(placeholder, value);
    copied = placeholder.start + placeholder.text.length;
  }

  return { output: output + text.slice(copied), error: null };
}

// A name's value from a record, ignoring case.
export function lookup(values: Readonly<Record<string, string>>): (name: string) => string | null {
  return (name) => {
    if (Object.hasOwn(values, name)) {
      return values[name] ?? null;
    }

    const key = Object.keys(values).find((candidate) => sameName(candidate, name));

    return key === undefined ? null : (values[key] ?? null);
  };
}

// Where the caret is in an unfinished placeholder, such as after "PC-{{Ser": the name typed so far and where it
// starts, so a completion can replace it. Null when the caret is not in one.
export function completionAt(text: string, caret: number): { from: number; typed: string } | null {
  const before = text.slice(0, caret);
  const match = /\{\{\s*([A-Za-z0-9_]*)$/.exec(before);

  if (match === null) {
    return null;
  }

  const typed = match[1] ?? "";

  return { from: caret - typed.length, typed };
}

// The text with the name completed at the caret, closing the placeholder unless it is closed already, and where the
// caret goes after it.
export function complete(
  text: string,
  caret: number,
  name: string,
): { text: string; caret: number } | null {
  const at = completionAt(text, caret);

  if (at === null) {
    return null;
  }

  const after = text.slice(caret);
  const rest = after.replace(/^[A-Za-z0-9_]*/, "");
  const closing = /^\s*(\|[^{}]*)?\}\}/.test(rest) ? "" : "}}";
  const inserted = name + closing;

  return {
    text: text.slice(0, at.from) + inserted + rest,
    caret: at.from + inserted.length,
  };
}
