// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useId } from "react";

import type { HardwareModel } from "@/machines/machines";

import styles from "./TargetsEditor.module.scss";

export interface TargetsEditorProps {
  // The package's name, which the controls are labelled with.
  name: string;
  targets: HardwareModel[];
  // The server's refusal of the targets.
  messages: readonly string[];
  // The datalists of the models and manufacturers machines reported.
  modelsListId: string;
  manufacturersListId: string;
  onChange: (targets: HardwareModel[], immediate: boolean) => void;
}

// The hardware models a driver package is for, each a manufacturer, empty for any, and a model.
export function TargetsEditor({
  name,
  targets,
  messages,
  modelsListId,
  manufacturersListId,
  onChange,
}: TargetsEditorProps) {
  const messagesId = useId();
  const invalid = messages.length > 0;

  const changed = (index: number, patch: Partial<HardwareModel>) => {
    onChange(
      targets.map((target, at) => (at === index ? { ...target, ...patch } : target)),
      false,
    );
  };

  return (
    <div className={styles.targets}>
      {targets.length === 0 && (
        <p className={styles.warning}>Targets no model, so no machine gets these drivers.</p>
      )}
      {targets.map((target, index) => {
        const number = String(index + 1);

        return (
          <div key={index} className={styles.row}>
            <input
              type="text"
              list={manufacturersListId}
              aria-label={`Manufacturer of target ${number} of ${name}`}
              placeholder="Any manufacturer"
              value={target.manufacturer ?? ""}
              aria-invalid={invalid ? true : undefined}
              aria-describedby={invalid ? messagesId : undefined}
              onChange={(event) => {
                changed(index, {
                  manufacturer: event.target.value.trim() === "" ? null : event.target.value,
                });
              }}
            />
            <input
              type="text"
              list={modelsListId}
              aria-label={`Model of target ${number} of ${name}`}
              placeholder="Model"
              value={target.model}
              aria-invalid={invalid ? true : undefined}
              aria-describedby={invalid ? messagesId : undefined}
              onChange={(event) => {
                changed(index, { model: event.target.value });
              }}
            />
            <button
              type="button"
              className={styles.button}
              aria-label={`Remove target ${number} of ${name}`}
              onClick={() => {
                onChange(
                  targets.filter((_, at) => at !== index),
                  true,
                );
              }}
            >
              Remove
            </button>
          </div>
        );
      })}
      {invalid && (
        <ul id={messagesId} className={styles.messages}>
          {messages.map((message) => (
            <li key={message}>{message}</li>
          ))}
        </ul>
      )}
      <div>
        <button
          type="button"
          className={styles.button}
          aria-label={`Add a target to ${name}`}
          onClick={() => {
            onChange([...targets, { manufacturer: null, model: "" }], false);
          }}
        >
          Add a target
        </button>
      </div>
    </div>
  );
}
