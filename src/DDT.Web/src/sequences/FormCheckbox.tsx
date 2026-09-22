// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useId } from "react";

import styles from "./form.module.scss";

export interface FormCheckboxProps {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  messages: readonly string[];
  hint?: string;
}

export function FormCheckbox({ label, checked, onChange, messages, hint }: FormCheckboxProps) {
  const id = useId();
  const messagesId = `${id}-messages`;
  const invalid = messages.length > 0;

  return (
    <div className={styles.check}>
      <div className={styles.checkLine}>
        <input
          id={id}
          type="checkbox"
          checked={checked}
          aria-invalid={invalid ? true : undefined}
          aria-describedby={invalid ? messagesId : undefined}
          onChange={(event) => {
            onChange(event.target.checked);
          }}
        />
        <label htmlFor={id}>{label}</label>
      </div>
      {hint !== undefined && <span className={styles.hint}>{hint}</span>}
      {invalid && (
        <ul id={messagesId} className={styles.messages}>
          {messages.map((message) => (
            <li key={message}>{message}</li>
          ))}
        </ul>
      )}
    </div>
  );
}
