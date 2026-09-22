// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useId, type ReactNode } from "react";

import styles from "./form.module.scss";

// What the control inside a field needs to be labelled and to point at the field's messages.
export interface ControlProps {
  id: string;
  "aria-invalid": true | undefined;
  "aria-describedby": string | undefined;
}

export interface FormFieldProps {
  label: string;
  // The server's findings for this field, shown under it.
  messages: readonly string[];
  hint?: string;
  children: (control: ControlProps) => ReactNode;
}

export function FormField({ label, messages, hint, children }: FormFieldProps) {
  const id = useId();
  const messagesId = `${id}-messages`;
  const invalid = messages.length > 0;

  return (
    <div className={styles.field}>
      <label htmlFor={id} className={styles.label}>
        {label}
      </label>
      {children({
        id,
        "aria-invalid": invalid ? true : undefined,
        "aria-describedby": invalid ? messagesId : undefined,
      })}
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
