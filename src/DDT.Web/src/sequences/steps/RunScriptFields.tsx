// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { AccountSetting } from "../builder/AccountSetting";
import { ChoiceSetting } from "../fields/ChoiceSetting";
import type { Choice } from "../fields/fieldBase";
import { TextSetting } from "../fields/TextSetting";
import type { RunScriptStep, ScriptInterpreter, SequencePhase } from "../sequences";
import { interpreterLabel, interpreters } from "../steps";
import type { KindFieldsProps } from "./kindFields";
import { ScriptPackageSetting } from "./ScriptPackageSetting";
import { ScriptResultFields } from "./ScriptResultFields";

export function RunScriptFields({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<RunScriptStep>) {
  const phases: Choice[] = [
    { id: "WindowsPE", label: t`Windows PE, before the hand-over` },
    { id: "Windows", label: t`Windows, after the hand-over` },
  ];

  return (
    <>
      <p className="text-ink-2 sm:col-span-2">
        {(step.runAs ?? null) === null ? (
          <Trans>
            Runs as SYSTEM on the machine. Everyone who can sign in to DDT can read the script, so
            it must hold no passwords.
          </Trans>
        ) : (
          <Trans>
            Runs as the account chosen below, which DDT signs in with; the script never sees its
            password. Everyone who can sign in to DDT can read the script, so it must hold no
            passwords.
          </Trans>
        )}
      </p>
      <ChoiceSetting
        label={<Trans>Runs in</Trans>}
        field="phase"
        findings={findings}
        value={step.phase}
        choices={phases}
        onChange={(phase) => {
          onChange({ phase: phase as SequencePhase });
        }}
      />
      {step.phase === "Windows" || (step.runAs ?? null) !== null ? (
        <AccountSetting
          label={<Trans>Run as</Trans>}
          field="runAs"
          findings={findings}
          className="sm:col-span-2"
          use="runAs"
          noneLabel={t`SYSTEM`}
          hint={
            step.phase === "Windows" ? (
              <Trans>An account in the installed Windows, signed in for the script alone.</Trans>
            ) : (
              <Trans>
                Only a script in Windows runs as an account; in Windows PE it runs as SYSTEM.
              </Trans>
            )
          }
          value={step.runAs ?? null}
          onChange={(runAs) => {
            onChange({ runAs }, true);
          }}
        />
      ) : null}
      <ChoiceSetting
        label={<Trans>Interpreter</Trans>}
        field="interpreter"
        findings={findings}
        value={step.interpreter}
        choices={interpreters.map((interpreter) => ({
          id: interpreter,
          label: interpreterLabel(interpreter),
        }))}
        onChange={(interpreter) => {
          onChange({ interpreter: interpreter as ScriptInterpreter });
        }}
      />
      <TextSetting
        label={<Trans>Script</Trans>}
        field="script"
        findings={findings}
        className="sm:col-span-2"
        mono
        multiline
        rows={12}
        value={step.script}
        onChange={(script) => {
          onChange({ script });
        }}
      />
      <ScriptPackageSetting step={step} findings={findings} catalog={catalog} onChange={onChange} />
      <ScriptResultFields step={step} findings={findings} onChange={onChange} />
    </>
  );
}
