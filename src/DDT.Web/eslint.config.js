// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import js from "@eslint/js";
import { defineConfig, globalIgnores } from "eslint/config";
import prettier from "eslint-config-prettier/flat";
import reactHooks from "eslint-plugin-react-hooks";
import { reactRefresh } from "eslint-plugin-react-refresh";
import globals from "globals";
import tseslint from "typescript-eslint";

export default defineConfig([
  globalIgnores(["dist", "coverage", "node_modules"]),
  {
    files: ["**/*.{ts,tsx}"],
    extends: [
      js.configs.recommended,
      tseslint.configs.strictTypeChecked,
      tseslint.configs.stylisticTypeChecked,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite(),
      prettier,
    ],
    languageOptions: {
      ecmaVersion: 2023,
      globals: globals.browser,
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
  },
  {
    files: ["src/app/router.tsx"],
    rules: {
      // TanStack Router's documented control flow is `throw redirect(...)`, and redirect returns
      // a plain object rather than an Error.
      "@typescript-eslint/only-throw-error": [
        "error",
        { allow: [{ from: "package", package: "@tanstack/router-core", name: "Redirect" }] },
      ],
    },
  },
  // docs/code-style.md's size limits. serverMessages.ts is written by the server's tests.
  {
    files: ["src/**/*.{ts,tsx}"],
    ignores: ["src/**/*.test.{ts,tsx}", "src/lib/serverMessages.ts"],
    rules: {
      "max-lines": ["error", 400],
      "max-lines-per-function": ["error", 120],
      "max-depth": ["error", 3],
      "max-params": ["error", 5],
    },
  },
  {
    files: ["src/**/*.test.{ts,tsx}"],
    rules: {
      "max-lines": ["error", 700],
    },
  },
  // The cleanup's baseline: files that still break a size limit. A file leaves its list once it keeps the limit.
  {
    files: [
      "src/account/AccountPage.tsx",
      "src/accounts/AccountDrawer.tsx",
      "src/app/DesignPage.tsx",
      "src/conditions/ConditionBuilder.tsx",
      "src/conditions/conditions.ts",
      "src/deployments/deployments.ts",
      "src/images/ImagesPage.tsx",
      "src/lib/autosave.ts",
      "src/live/liveConnection.test.ts",
      "src/live/liveConnection.ts",
      "src/machines/AssignDialog.tsx",
      "src/machines/MachinePage.test.tsx",
      "src/machines/MachinesPage.test.tsx",
      "src/machines/MachinesPage.tsx",
      "src/packages/PackagesPage.tsx",
      "src/rules/RuleDrawer.tsx",
      "src/rules/RulesPage.tsx",
      "src/rules/rules.ts",
      "src/runs/RunFlow.tsx",
      "src/sequences/SequenceEditorPage.test.tsx",
      "src/sequences/builder/FlowBuilder.tsx",
      "src/sequences/builder/FlowCanvas.tsx",
      "src/sequences/builder/NodeInspector.tsx",
      "src/sequences/builder/VariablesPanel.tsx",
      "src/sequences/flow/flowEdits.ts",
      "src/sequences/flow/flowKeyboard.ts",
      "src/sequences/flow/flowLayout.ts",
      "src/sequences/sequences.ts",
      "src/settings/AgentPanel.tsx",
      "src/settings/CertificatePanel.tsx",
      "src/settings/DirectorySettings.tsx",
      "src/settings/NetworkBootPage.tsx",
      "src/settings/SettingsParts.tsx",
      "src/test/treeRun.ts",
      "src/ui/FlowViewport.tsx",
      "src/uploads/UploadPanel.tsx",
      "src/users/UsersPage.tsx",
    ],
    rules: { "max-lines": "off" },
  },
  {
    files: [
      "src/account/AccountPage.tsx",
      "src/accounts/AccountDrawer.tsx",
      "src/accounts/AccountsPage.tsx",
      "src/app/DesignPage.tsx",
      "src/audit/AuditPage.tsx",
      "src/auth/SignInPage.tsx",
      "src/conditions/ConditionBuilder.tsx",
      "src/images/ImagesPage.tsx",
      "src/inputs/InputsForm.tsx",
      "src/lib/autosave.ts",
      "src/live/liveConnection.ts",
      "src/log/LogPanel.tsx",
      "src/log/LogViewport.tsx",
      "src/log/logReader.ts",
      "src/machines/AssignDialog.tsx",
      "src/machines/MachineActions.tsx",
      "src/machines/MachinePage.tsx",
      "src/machines/MachinesPage.tsx",
      "src/machines/RunWaiting.tsx",
      "src/machines/useMachineActions.ts",
      "src/packages/PackageDialog.tsx",
      "src/packages/PackagesPage.tsx",
      "src/roles/MachineRolesPage.tsx",
      "src/roles/RoleDrawer.tsx",
      "src/rules/RuleDrawer.tsx",
      "src/rules/RulesPage.tsx",
      "src/runs/RunFlow.tsx",
      "src/runs/RunHistoryPage.tsx",
      "src/runs/RunPanel.tsx",
      "src/runs/RunSteps.tsx",
      "src/sequences/NewSequenceDialog.tsx",
      "src/sequences/SequencesPage.tsx",
      "src/sequences/builder/AccountSetting.tsx",
      "src/sequences/builder/FlowBuilder.tsx",
      "src/sequences/builder/FlowCanvas.tsx",
      "src/sequences/builder/FlowOutline.tsx",
      "src/sequences/builder/NodeInspector.tsx",
      "src/sequences/builder/ProblemsPanel.tsx",
      "src/sequences/builder/TemplateField.tsx",
      "src/sequences/builder/VariablesPanel.tsx",
      "src/sequences/flow/flowLayout.ts",
      "src/sequences/steps/ScriptFields.tsx",
      "src/sequences/useSequenceEditor.ts",
      "src/settings/AgentPanel.tsx",
      "src/settings/CertificatePanel.tsx",
      "src/settings/ConsoleLogoPanel.tsx",
      "src/settings/DeploymentDefaultsPage.tsx",
      "src/settings/DirectorySettings.tsx",
      "src/settings/LoggingPanel.tsx",
      "src/settings/NetworkBootPage.tsx",
      "src/settings/RoleMapEditor.tsx",
      "src/settings/SignInSettingsPage.tsx",
      "src/settings/useSettingsForm.ts",
      "src/tokens/MakeTokenDialog.tsx",
      "src/ui/FlowNode.tsx",
      "src/ui/FlowViewport.tsx",
      "src/uploads/UploadPanel.tsx",
      "src/uploads/resumableUpload.ts",
      "src/users/UserDialogs.tsx",
      "src/users/UsersPage.tsx",
    ],
    rules: { "max-lines-per-function": "off" },
  },
  {
    files: [
      "src/log/logReader.ts",
      "src/sequences/flow/flowEdits.ts",
      "src/sequences/flow/references.ts",
    ],
    rules: { "max-depth": "off" },
  },
  {
    files: ["src/sequences/flow/flowLayout.ts", "src/uploads/resumableUpload.ts"],
    rules: { "max-params": "off" },
  },
  {
    files: ["**/*.config.ts", "eslint.config.js"],
    languageOptions: { globals: globals.node },
  },
]);
