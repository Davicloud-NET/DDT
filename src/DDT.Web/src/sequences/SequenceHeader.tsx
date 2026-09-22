// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@/lib/format";

import { FormField } from "./FormField";

import styles from "./SequenceHeader.module.scss";

export interface SequenceHeaderProps {
  name: string;
  description: string;
  // The server's refusal of a name or description, such as a name another sequence has.
  nameMessages: readonly string[];
  descriptionMessages: readonly string[];
  problemCount: number;
  warningCount: number;
  onRename: (name: string) => void;
  onDescribe: (description: string) => void;
  onShowFirstFinding: () => void;
}

export function SequenceHeader({
  name,
  description,
  nameMessages,
  descriptionMessages,
  problemCount,
  warningCount,
  onRename,
  onDescribe,
  onShowFirstFinding,
}: SequenceHeaderProps) {
  const findings = problemCount + warningCount;

  return (
    <header className={styles.header}>
      <h1 className={styles.title}>{name.trim() === "" ? "Unnamed sequence" : name}</h1>

      <p className={problemCount > 0 ? styles.problems : styles.ready}>
        {problemCount > 0
          ? `${plural(problemCount, "problem")} ${problemCount === 1 ? "keeps" : "keep"} it from running.`
          : "Ready to run."}
        {warningCount > 0 && ` ${plural(warningCount, "warning")}.`}{" "}
        {findings > 0 && (
          <button type="button" className={styles.jump} onClick={onShowFirstFinding}>
            Show the first
          </button>
        )}
      </p>

      <FormField label="Sequence name" messages={nameMessages}>
        {(control) => (
          <input
            {...control}
            type="text"
            value={name}
            onChange={(event) => {
              onRename(event.target.value);
            }}
          />
        )}
      </FormField>
      <FormField label="Description" messages={descriptionMessages}>
        {(control) => (
          <textarea
            {...control}
            rows={2}
            value={description}
            onChange={(event) => {
              onDescribe(event.target.value);
            }}
          />
        )}
      </FormField>
    </header>
  );
}
