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
const BASELINE = new Set<string>([
  "account/AccountPage.tsx",
  "accounts/AccountDrawer.tsx",
  "accounts/AccountsPage.tsx",
  "app/CommandPalette.tsx",
  "app/DesignPage.tsx",
  "app/Shell.tsx",
  "boot/BootImagePage.tsx",
  "conditions/ConditionBuilder.tsx",
  "images/ImagesPage.tsx",
  "inputs/InputsForm.tsx",
  "log/LogPanel.tsx",
  "machines/MachineActions.tsx",
  "machines/MachinePage.tsx",
  "machines/MachinesPage.tsx",
  "packages/PackagesPage.tsx",
  "roles/MachineRolesPage.tsx",
  "roles/RoleDrawer.tsx",
  "rules/RuleDrawer.tsx",
  "rules/RuleTest.tsx",
  "rules/RulesPage.tsx",
  "rules/drawerParts.tsx",
  "runs/RunFlow.tsx",
  "runs/RunPanel.tsx",
  "sequences/builder/AddNodeMenu.tsx",
  "sequences/builder/FlowCanvas.tsx",
  "sequences/builder/NodeInspector.tsx",
  "sequences/builder/Palette.tsx",
  "sequences/builder/VariablesPanel.tsx",
  "sequences/fields.tsx",
  "sequences/steps/FlowFields.tsx",
  "sequences/steps/RawImageFields.tsx",
  "sequences/steps/ScriptFields.tsx",
  "sequences/steps/WindowsFields.tsx",
  "settings/AgentPanel.tsx",
  "settings/CertificatePanel.tsx",
  "settings/ConsoleLogoPanel.tsx",
  "settings/DirectorySettings.tsx",
  "settings/LoggingPanel.tsx",
  "settings/NetworkBootPage.tsx",
  "settings/ServerPage.tsx",
  "settings/SettingsParts.tsx",
  "settings/SignInSettingsPage.tsx",
  "tokens/TokenTable.tsx",
  "tokens/TokensPage.tsx",
  "ui/Button.tsx",
  "ui/Checkbox.tsx",
  "ui/Controls.tsx",
  "ui/Dialog.tsx",
  "ui/FlowNode.tsx",
  "ui/FlowWires.tsx",
  "ui/Layout.tsx",
  "ui/SequenceRail.tsx",
  "ui/Toast.tsx",
  "uploads/UploadPanel.tsx",
  "users/DirectoryPanel.tsx",
  "users/UserDialogs.tsx",
  "users/UsersPage.tsx",
]);

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
