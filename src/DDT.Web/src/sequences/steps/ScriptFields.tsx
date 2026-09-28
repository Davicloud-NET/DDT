// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { Link } from "@tanstack/react-router";

import { AccountSetting } from "../builder/AccountSetting";
import { ChoiceSetting, CodesSetting, NumberSetting, TextSetting, type Choice } from "../fields";
import type { RebootStep, RunScriptStep, ScriptInterpreter, SequencePhase } from "../sequences";
import { interpreterLabel, interpreters } from "../steps";
import type { KindFieldsProps } from "./kindFields";

// The steps any sequence may have: a script, and a restart.

// The Select needs a key for "no package"; a package id is a GUID, so this never is one.
const NO_PACKAGE = "none";

export function RunScriptFields({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<RunScriptStep>) {
  const files = catalog.packages.filter((item) => item.kind === "Files");
  const listed = step.packageId === null || files.some((item) => item.id === step.packageId);
  const phases: Choice[] = [
    { id: "WindowsPE", label: t`Windows PE, before the hand-over` },
    { id: "Windows", label: t`Windows, after the hand-over` },
  ];
  const packages: Choice[] = [
    { id: NO_PACKAGE, label: t`No package` },
    ...(listed || step.packageId === null
      ? []
      : [{ id: step.packageId, label: t`Package deleted` }]),
    ...files.map((item) => ({ id: item.id, label: item.name })),
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
      <ChoiceSetting
        label={<Trans>Files package</Trans>}
        field="packageId"
        findings={findings}
        className="sm:col-span-2"
        hint={
          <Trans>
            Unpacked before the script runs, and the script's working directory. Packages are under{" "}
            <Link to="/library/files" className="font-semibold text-ink underline">
              Files
            </Link>
            .
          </Trans>
        }
        value={step.packageId ?? NO_PACKAGE}
        choices={packages}
        onChange={(packageId) => {
          onChange({ packageId: packageId === NO_PACKAGE ? null : packageId });
        }}
      />
      <NumberSetting
        label={<Trans>Timeout in minutes</Trans>}
        field="timeoutMinutes"
        findings={findings}
        hint={<Trans>The step fails when the script runs longer.</Trans>}
        minValue={1}
        value={step.timeoutMinutes}
        onChange={(timeoutMinutes) => {
          onChange({ timeoutMinutes });
        }}
      />
      <CodesSetting
        label={<Trans>Exit codes that mean success</Trans>}
        field="successExitCodes"
        findings={findings}
        hint={<Trans>Whole numbers, separated by commas.</Trans>}
        value={step.successExitCodes}
        onChange={(successExitCodes) => {
          onChange({ successExitCodes });
        }}
      />
      <CodesSetting
        label={<Trans>Exit codes that ask for a restart</Trans>}
        field="rebootExitCodes"
        findings={findings}
        hint={<Trans>The machine restarts and the sequence goes on with the next step.</Trans>}
        value={step.rebootExitCodes}
        onChange={(rebootExitCodes) => {
          onChange({ rebootExitCodes });
        }}
      />
    </>
  );
}

// A restart has no settings; it runs in the phase of the step before it.
export function RebootFields({ step }: KindFieldsProps<RebootStep>) {
  const name = step.name;

  return (
    <p className="text-ink-2 sm:col-span-2">
      <Trans>
        Restarts the machine, and the sequence goes on with the step after {name}. In Windows PE
        this needs a partitioned disk, where the run's state is kept.
      </Trans>
    </p>
  );
}
