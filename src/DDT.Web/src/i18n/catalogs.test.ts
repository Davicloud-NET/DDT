// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

/// <reference types="node" />
// @vitest-environment node

import { readFileSync } from "node:fs";

import { describe, expect, it } from "vitest";

// The catalogs as `npm run i18n` writes them: one msgid and msgstr per entry, each on a single line.
function read(locale: string): Map<string, string> {
  const text = readFileSync(new URL(`../locales/${locale}/messages.po`, import.meta.url), "utf8");
  const entries = new Map<string, string>();

  for (const match of text.matchAll(
    /^msgid "((?:[^"\\]|\\.)*)"\r?\nmsgstr "((?:[^"\\]|\\.)*)"/gm,
  )) {
    const [, id = "", translation = ""] = match;

    if (id !== "") {
      entries.set(id, translation);
    }
  }

  return entries;
}

// The arguments a message names, such as {name} and {count, plural, …}. The text of a plural branch, such as
// {Paused, # new lines}, is not an argument.
function argumentsOf(message: string): string[] {
  return [
    ...new Set(
      [
        ...message.matchAll(
          /\{([A-Za-z_]\w*)(?:\}|\s*,\s*(?:plural|select|selectordinal|number|date|time)\s*[,}])/g,
        ),
      ].map((match) => match[1] ?? ""),
    ),
  ].sort();
}

describe("the translation catalogs", () => {
  const english = read("en");
  const german = read("de");

  it("hold the same messages", () => {
    expect([...german.keys()].sort()).toEqual([...english.keys()].sort());
  });

  it("translate every message into German", () => {
    const missing = [...german].filter(([, translation]) => translation === "").map(([id]) => id);

    expect(missing).toEqual([]);
  });

  // A value interpolated as a member or a call becomes {0}, which tells a translator nothing; the code names it.
  it("name every argument instead of numbering it", () => {
    const numbered = [...english.keys()].filter((id) => /\{\d+[,}]/.test(id));

    expect(numbered).toEqual([]);
  });

  it("keep every argument in the German text", () => {
    const changed = [...german]
      .filter(([, translation]) => translation !== "")
      .filter(([id, translation]) => argumentsOf(id).join() !== argumentsOf(translation).join())
      .map(([id]) => id);

    expect(changed).toEqual([]);
  });
});
