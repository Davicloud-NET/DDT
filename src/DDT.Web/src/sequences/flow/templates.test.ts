// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import fixture from "@/test/fixtures/template-cases.json";

import {
  complete,
  completionAt,
  lookup,
  MAX_COUNT,
  parseTemplate,
  renderTemplate,
  TEMPLATE_FILTERS,
} from "./templates";

// The server writes these cases from its own ValueTemplate: what Parse reports with the values' names as the known
// ones, and what the template renders to, or the problem that stops it.
interface Case {
  template: string;
  values: Record<string, string>;
  names: string[];
  problems: unknown[];
  output: string | null;
  error: unknown;
}

const cases = fixture.cases as Case[];

describe("the template mirror", () => {
  it("knows the server's filters and their limit", () => {
    expect([...TEMPLATE_FILTERS]).toEqual(fixture.filters);
    expect(MAX_COUNT).toBe(fixture.maxCount);
  });

  it.each(cases.map((one) => [one.template, one] as const))(
    "reads and renders %j as the server does",
    (template, one) => {
      const known = (name: string) =>
        Object.keys(one.values).some((key) => key.toLowerCase() === name.toLowerCase()) ||
        // The server knows every name of its machine; the fixture lists only those the template uses.
        ["ComputerName", "SerialNumber", "Office", "Padded", "Empty", "Site_2", "Owner"].some(
          (key) => key.toLowerCase() === name.toLowerCase(),
        );
      const parsed = parseTemplate(template, known);
      const rendered = renderTemplate(template, lookup(one.values));

      expect(parsed.names).toEqual(one.names);
      expect(parsed.problems).toEqual(one.problems);
      expect(rendered.output).toBe(one.output);
      expect(rendered.error).toEqual(one.error);
    },
  );

  it("finds the name being typed at the caret and completes it", () => {
    expect(completionAt("PC-{{Ser", 8)).toEqual({ from: 5, typed: "Ser" });
    expect(completionAt("PC-{{ ", 6)).toEqual({ from: 6, typed: "" });
    expect(completionAt("PC-{{Serial}}", 13)).toBeNull();
    expect(completionAt("PC-{Ser", 7)).toBeNull();

    expect(complete("PC-{{Ser", 8, "SerialNumber")).toEqual({
      text: "PC-{{SerialNumber}}",
      caret: 19,
    });
    // A placeholder closed already, with its filters, keeps them.
    expect(complete("PC-{{Ser|alnum}}", 8, "SerialNumber")).toEqual({
      text: "PC-{{SerialNumber|alnum}}",
      caret: 17,
    });
    expect(complete("PC-01", 5, "SerialNumber")).toBeNull();
  });
});
