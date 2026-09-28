// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

/// <reference types="node" />
// @vitest-environment node

import { readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

import * as ts from "typescript";
import { expect, it } from "vitest";

// docs/code-style.md: one exported component per file, and private helper components of at most 30 lines.
const MAX_PRIVATE_COMPONENT_LINES = 30;

// Compound components share their root's file.
const COMPOUNDS = new Set(["ui/Menu.tsx", "ui/Select.tsx", "ui/Table.tsx", "ui/Tabs.tsx"]);

// Files that broke the rule when it came in. A file leaves the list once it keeps the rule.
const BASELINE = new Set<string>([]);

const SRC = fileURLToPath(new URL("..", import.meta.url));

function componentFiles(): string[] {
  return readdirSync(SRC, { recursive: true, encoding: "utf8" })
    .map((file) => file.replaceAll("\\", "/"))
    .filter((file) => file.endsWith(".tsx") && !file.endsWith(".test.tsx"))
    .sort();
}

function isExported(node: ts.Node): boolean {
  return (
    ts.canHaveModifiers(node) &&
    (ts.getModifiers(node) ?? []).some((m) => m.kind === ts.SyntaxKind.ExportKeyword)
  );
}

// The top-level functions named like components, with whether each is exported and how long it is.
function components(file: string): { exported: boolean; lines: number }[] {
  const text = readFileSync(`${SRC}/${file}`, "utf8");
  const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const lines = (node: ts.Node) =>
    source.getLineAndCharacterOfPosition(node.getEnd()).line -
    source.getLineAndCharacterOfPosition(node.getStart()).line +
    1;
  const found: { exported: boolean; lines: number }[] = [];

  for (const statement of source.statements) {
    if (
      ts.isFunctionDeclaration(statement) &&
      statement.name &&
      /^[A-Z]/.test(statement.name.text)
    ) {
      found.push({ exported: isExported(statement), lines: lines(statement) });
    } else if (ts.isVariableStatement(statement)) {
      for (const declaration of statement.declarationList.declarations) {
        const init = declaration.initializer;
        if (
          ts.isIdentifier(declaration.name) &&
          /^[A-Z]/.test(declaration.name.text) &&
          init !== undefined &&
          (ts.isArrowFunction(init) || ts.isFunctionExpression(init))
        ) {
          found.push({ exported: isExported(statement), lines: lines(declaration) });
        }
      }
    }
  }

  return found;
}

function breaksRule(file: string): boolean {
  if (COMPOUNDS.has(file)) {
    return false;
  }

  const found = components(file);
  return (
    found.filter((c) => c.exported).length > 1 ||
    found.some((c) => !c.exported && c.lines > MAX_PRIVATE_COMPONENT_LINES)
  );
}

it("keeps one component per file", () => {
  const breaking = componentFiles().filter(breaksRule);

  expect(breaking.filter((file) => !BASELINE.has(file))).toEqual([]);
  expect([...BASELINE].filter((file) => !breaking.includes(file))).toEqual([]);
});
