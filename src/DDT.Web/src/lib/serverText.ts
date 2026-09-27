// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";

import { serverMessages } from "@/lib/serverMessages";

// A sentence the server says, as a stable code and the values its text names: a string, a number, or a message
// for a sentence within this one. The server sends its English beside it.
export interface ServerMessage {
  code: string;
  args: ServerArguments;
}

export type ServerArguments = Record<string, ServerArgument>;

export type ServerArgument = string | number | ServerMessage;

// The server's sentence in the person's language. A code this build does not know, in the message or in one within
// it, leaves the English the server sent, fallback, which is also what a text without a code says.
export function serverText(
  code: string | null | undefined,
  args: ServerArguments | null | undefined,
  fallback: string,
): string {
  if (code === null || code === undefined) {
    return fallback;
  }

  return translated({ code, args: args ?? {} }) ?? fallback;
}

function translated(message: ServerMessage): string | null {
  const descriptor = serverMessages[message.code];

  if (descriptor === undefined) {
    return null;
  }

  const values: Record<string, string | number> = {};

  for (const [name, value] of Object.entries(message.args)) {
    if (typeof value === "object") {
      const nested = translated(value);

      if (nested === null) {
        return null;
      }

      values[name] = nested;
    } else {
      values[name] = value;
    }
  }

  return i18n._({ ...descriptor, values });
}

// A message as the server sends it, so a value from the wire can be read without trusting its shape.
export function isServerMessage(value: unknown): value is ServerMessage {
  return (
    typeof value === "object" &&
    value !== null &&
    typeof (value as { code?: unknown }).code === "string" &&
    typeof (value as { args?: unknown }).args === "object" &&
    (value as { args?: unknown }).args !== null
  );
}
