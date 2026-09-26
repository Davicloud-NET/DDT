// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useSyncExternalStore } from "react";

// Light or dark follows the system unless the person picks one. The choice lives in this browser only.
export type ThemeChoice = "system" | "light" | "dark";

const STORAGE_KEY = "ddt.theme";
const listeners = new Set<() => void>();
const systemDark = () => window.matchMedia("(prefers-color-scheme: dark)");

function readChoice(): ThemeChoice {
  try {
    const value = window.localStorage.getItem(STORAGE_KEY);

    return value === "light" || value === "dark" ? value : "system";
  } catch {
    return "system";
  }
}

let choice: ThemeChoice = "system";

function apply(): void {
  const dark = choice === "dark" || (choice === "system" && systemDark().matches);

  document.documentElement.classList.toggle("dark", dark);
}

// Called once before the first render, so the page never flashes in the other theme.
export function startTheme(): void {
  choice = readChoice();
  apply();
  systemDark().addEventListener("change", apply);
}

export function chooseTheme(next: ThemeChoice): void {
  choice = next;

  try {
    if (next === "system") {
      window.localStorage.removeItem(STORAGE_KEY);
    } else {
      window.localStorage.setItem(STORAGE_KEY, next);
    }
  } catch {
    // Without storage the choice lasts until the page is reloaded.
  }

  apply();
  listeners.forEach((listener) => {
    listener();
  });
}

export function useThemeChoice(): ThemeChoice {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);

      return () => listeners.delete(listener);
    },
    () => choice,
  );
}
