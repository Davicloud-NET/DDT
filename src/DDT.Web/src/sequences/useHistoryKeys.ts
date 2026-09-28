// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useEffect } from "react";

import { isTextField } from "@/lib/textField";

import { historyCommand, type HistoryCommand } from "./flow/history";

// Ctrl+Z and Ctrl+Y anywhere on the page but in a text field, whose own undo takes back its typing.
export function useHistoryKeys(step: (command: HistoryCommand) => void): void {
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const command = historyCommand(event);

      if (command === null || event.defaultPrevented || isTextField(event.target)) {
        return;
      }

      event.preventDefault();
      step(command);
    };

    document.addEventListener("keydown", onKeyDown);

    return () => {
      document.removeEventListener("keydown", onKeyDown);
    };
  });
}
