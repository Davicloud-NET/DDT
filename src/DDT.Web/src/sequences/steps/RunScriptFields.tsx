// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { AccountSetting } from "../builder/AccountSetting";
import { ChoiceSetting } from "../fields/ChoiceSetting";
import type { Choice } from "../fields/fieldBase";
import { TextSetting } from "../fields/TextSetting";
import { kindOfPhase, restartCodes, scriptInterpreters, scriptPhases } from "../sequenceKinds";
import type { RunScriptStep, ScriptInterpreter, SequencePhase } from "../sequences";
import { interpreterLabel } from "../steps";
import type { KindFieldsProps } from "./kindFields";
import { ScriptIntro } from "./ScriptIntro";
import { ScriptPackageSetting } from "./ScriptPackageSetting";
import { ScriptResultFields } from "./ScriptResultFields";

// Where a script runs, as the choice names it.
function phaseChoice(phase: SequencePhase): string {
  switch (phase) {
    case "WindowsPE":
      return t`Windows PE, before the hand-over`;
    case "Windows":
      return t`Windows, after the hand-over`;
    case "LinuxBoot":
      return t`Linux boot environment, before the hand-over`;
    case "Linux":
      return t`Installed Linux, after the hand-over`;
  }
}

// The restart codes that follow a script to another phase: the ones it has, unless they are what its phase gave it.
function movedCodes(step: RunScriptStep, phase: SequencePhase): number[] {
  const given = restartCodes(step.phase);
  const untouched =
    step.rebootExitCodes.length === given.length &&
    step.rebootExitCodes.every((code, index) => code === given[index]);

  return untouched ? restartCodes(phase) : step.rebootExitCodes;
}

// What the sequence's kind offers, and the step's own value while it is another, so it shows next to its problem.
function withOwn<T>(offered: readonly T[], own: T): T[] {
  return offered.includes(own) ? [...offered] : [own, ...offered];
}

export function RunScriptFields({
  step,
  sequence,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<RunScriptStep>) {
  const phases: Choice[] = withOwn(scriptPhases(sequence), step.phase).map((phase) => ({
    id: phase,
    label: phaseChoice(phase),
  }));
  const interpreters: Choice[] = withOwn(scriptInterpreters(sequence), step.interpreter).map(
    (interpreter) => ({ id: interpreter, label: interpreterLabel(interpreter) }),
  );
  const root = kindOfPhase(step.phase) === "Linux" ? t`root` : t`SYSTEM`;

  // A phase of the other kind takes the interpreter that kind runs. The script has to be rewritten for it anyway.
  const changePhase = (phase: SequencePhase) => {
    const [first] = scriptInterpreters(kindOfPhase(phase));
    const fits = scriptInterpreters(kindOfPhase(phase)).includes(step.interpreter);
    const rebootExitCodes = movedCodes(step, phase);

    onChange(
      fits || first === undefined
        ? { phase, rebootExitCodes }
        : { phase, rebootExitCodes, interpreter: first },
    );
  };

  return (
    <>
      <ScriptIntro step={step} />
      <ChoiceSetting
        label={<Trans>Runs in</Trans>}
        field="phase"
        findings={findings}
        value={step.phase}
        choices={phases}
        onChange={(phase) => {
          changePhase(phase as SequencePhase);
        }}
      />
      {step.phase === "Windows" || (step.runAs ?? null) !== null ? (
        <AccountSetting
          label={<Trans>Run as</Trans>}
          field="runAs"
          findings={findings}
          className="sm:col-span-2"
          use="runAs"
          noneLabel={root}
          hint={
            step.phase === "Windows" ? (
              <Trans>An account in the installed Windows, signed in for the script alone.</Trans>
            ) : step.phase === "LinuxBoot" ? (
              <Trans>
                Only a script in Windows runs as an account; in the Linux boot environment it runs
                as root.
              </Trans>
            ) : step.phase === "Linux" ? (
              <Trans>
                Only a script in Windows runs as an account; in the installed Linux it runs as root.
              </Trans>
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
        choices={interpreters}
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
        {...(step.interpreter === "Shell"
          ? {
              hint: (
                <Trans>
                  Started with /bin/sh, or with the interpreter its first line names, such as
                  #!/bin/bash.
                </Trans>
              ),
            }
          : {})}
        value={step.script}
        onChange={(script) => {
          onChange({ script });
        }}
      />
      {step.phase !== "LinuxBoot" || step.packageId !== null ? (
        <ScriptPackageSetting
          step={step}
          sequence={sequence}
          findings={findings}
          catalog={catalog}
          onChange={onChange}
        />
      ) : null}
      <ScriptResultFields step={step} findings={findings} onChange={onChange} />
    </>
  );
}
