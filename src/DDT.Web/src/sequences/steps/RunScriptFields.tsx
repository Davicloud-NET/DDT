// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { CodesInput } from "../CodesInput";
import { FormField } from "../FormField";
import { NumberInput } from "../NumberInput";
import { fieldMessages } from "../problems";
import type { RunScriptStep, ScriptInterpreter, SequencePhase } from "../sequences";
import { interpreterLabel, interpreters } from "../steps";
import type { KindFieldsProps } from "./kindFields";

import styles from "../form.module.scss";

const phases: { phase: SequencePhase; label: string }[] = [
  { phase: "WindowsPE", label: "Windows PE, before the hand-over" },
  { phase: "Windows", label: "Windows, after the hand-over" },
];

export function RunScriptFields({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<RunScriptStep>) {
  const files = catalog.packages.filter((item) => item.kind === "Files");
  const listed = step.packageId === null || files.some((item) => item.id === step.packageId);

  return (
    <>
      <p className={styles.explain}>
        Runs as SYSTEM on the machine. Every viewer of DDT can read the script, so it must hold no
        passwords.
      </p>
      <FormField label="Runs in" messages={fieldMessages(findings, "phase")}>
        {(control) => (
          <select
            {...control}
            value={step.phase}
            onChange={(event) => {
              onChange({ phase: event.target.value as SequencePhase });
            }}
          >
            {phases.map((choice) => (
              <option key={choice.phase} value={choice.phase}>
                {choice.label}
              </option>
            ))}
          </select>
        )}
      </FormField>
      <FormField label="Interpreter" messages={fieldMessages(findings, "interpreter")}>
        {(control) => (
          <select
            {...control}
            value={step.interpreter}
            onChange={(event) => {
              onChange({ interpreter: event.target.value as ScriptInterpreter });
            }}
          >
            {interpreters.map((interpreter) => (
              <option key={interpreter} value={interpreter}>
                {interpreterLabel(interpreter)}
              </option>
            ))}
          </select>
        )}
      </FormField>
      <FormField label="Script" messages={fieldMessages(findings, "script")}>
        {(control) => (
          <textarea
            {...control}
            className={styles.script}
            spellCheck={false}
            value={step.script}
            onChange={(event) => {
              onChange({ script: event.target.value });
            }}
          />
        )}
      </FormField>
      <FormField
        label="Files package"
        messages={fieldMessages(findings, "packageId")}
        hint="Unpacked before the script runs, and the script's working directory."
      >
        {(control) => (
          <select
            {...control}
            value={step.packageId ?? ""}
            onChange={(event) => {
              onChange({ packageId: event.target.value === "" ? null : event.target.value });
            }}
          >
            <option value="">No package</option>
            {!listed && step.packageId !== null && (
              <option value={step.packageId}>Package deleted</option>
            )}
            {files.map((item) => (
              <option key={item.id} value={item.id}>
                {item.name}
              </option>
            ))}
          </select>
        )}
      </FormField>
      <FormField
        label="Timeout (minutes)"
        messages={fieldMessages(findings, "timeoutMinutes")}
        hint="The step fails when the script runs longer."
      >
        {(control) => (
          <NumberInput
            control={control}
            min={1}
            value={step.timeoutMinutes}
            onChange={(timeoutMinutes) => {
              onChange({ timeoutMinutes });
            }}
          />
        )}
      </FormField>
      <FormField
        label="Exit codes that mean success"
        messages={fieldMessages(findings, "successExitCodes")}
      >
        {(control) => (
          <CodesInput
            control={control}
            value={step.successExitCodes}
            onChange={(successExitCodes) => {
              onChange({ successExitCodes });
            }}
          />
        )}
      </FormField>
      <FormField
        label="Exit codes that ask for a restart"
        messages={fieldMessages(findings, "rebootExitCodes")}
        hint="The machine restarts and the sequence goes on with the next step."
      >
        {(control) => (
          <CodesInput
            control={control}
            value={step.rebootExitCodes}
            onChange={(rebootExitCodes) => {
              onChange({ rebootExitCodes });
            }}
          />
        )}
      </FormField>
    </>
  );
}
