// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconPlus, IconX } from "@tabler/icons-react";
import { useId } from "react";
import { Button as AriaButton } from "react-aria-components";

import { fieldFindings, type Findings } from "@/sequences/problems";
import { TextField } from "@/ui/TextField";

import type { AccountEdit } from "../accounts";

interface HostsEditorProps {
  rows: AccountEdit["hosts"];
  findings: Findings;
  onChange: (rows: AccountEdit["hosts"]) => void;
}

// The servers the account may connect shares on, one per row, as share paths name them.
export function HostsEditor({ rows, findings, onChange }: HostsEditorProps) {
  const { t } = useLingui();
  const labelId = useId();
  const own = fieldFindings(findings, "hosts").problems;

  return (
    <div role="group" aria-labelledby={labelId} data-field="hosts" className="flex flex-col gap-2">
      <span id={labelId} className="type-label text-ink">
        <Trans>Servers</Trans>
      </span>
      <span className="-mt-1 type-small text-muted">
        <Trans>
          The servers a step may connect shares on with this account, such as files.corp.example.
          None, and it connects no share.
        </Trans>
      </span>
      {rows.map((row, index) => {
        const number = index + 1;
        const field = `hosts[${String(index)}]`;
        const problems = fieldFindings(findings, field).problems;

        return (
          <div
            key={row.key}
            data-field={field}
            className="grid grid-cols-[minmax(0,1fr)_2rem] items-start gap-1.5"
          >
            <TextField
              label={<span className="sr-only">{t`Server ${number}`}</span>}
              mono
              autoComplete="off"
              spellCheck="false"
              value={row.host}
              onChange={(host) => {
                onChange(rows.map((other) => (other.key === row.key ? { ...other, host } : other)));
              }}
              isInvalid={problems.length > 0}
              errorMessage={problems.join(" ")}
            />
            <AriaButton
              aria-label={t`Remove server ${number}`}
              className="mt-0.75 flex size-8 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover hover:text-ink pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:outline-focus"
              onPress={() => {
                onChange(rows.filter((other) => other.key !== row.key));
              }}
            >
              <IconX size={14} stroke={2} />
            </AriaButton>
          </div>
        );
      })}
      {own.map((message) => (
        <p key={message} className="type-small text-fail-text">
          {message}
        </p>
      ))}
      <AriaButton
        className="flex cursor-pointer items-center gap-1.5 self-start rounded-key px-1 py-1 type-label text-ink key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:outline-focus"
        onPress={() => {
          onChange([...rows, { key: crypto.randomUUID(), host: "" }]);
        }}
      >
        <IconPlus aria-hidden="true" size={14} stroke={2} />
        <Trans>Add a server</Trans>
      </AriaButton>
    </div>
  );
}
