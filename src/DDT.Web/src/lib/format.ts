// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { formattingLocale } from "@/i18n/i18n";

const units = ["KB", "MB", "GB", "TB"] as const;

// Numbers follow the language chosen in DDT, not the browser's, so a German page writes 5,9 GB.
export function number(value: number): string {
  return new Intl.NumberFormat(formattingLocale(), { maximumFractionDigits: 1 }).format(value);
}

// Binary multiples with the labels Windows shows, so a size here matches what Explorer and Disk
// Management report for the same file or disk.
export function formatBytes(bytes: number): string {
  if (bytes < 1024) {
    const count = number(bytes);

    return t`${count} bytes`;
  }

  let value = bytes / 1024;
  let unit: (typeof units)[number] = "KB";

  for (const next of units.slice(1)) {
    if (value < 1024) {
      break;
    }

    value /= 1024;
    unit = next;
  }

  return `${number(value)} ${unit}`;
}

export function formatDuration(milliseconds: number): string {
  const total = Math.max(0, Math.floor(milliseconds / 1000));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;

  if (hours > 0) {
    return t`${hours} h ${minutes} min`;
  }

  if (minutes > 0) {
    return t`${minutes} min ${seconds} s`;
  }

  return t`${seconds} s`;
}

// "1 problem", "2 problems": for nouns that add an s. English only; messages that are translated use Lingui's
// plural instead, and the modules still calling this move to it as their pages are rebuilt.
export function plural(count: number, noun: string): string {
  return `${number(count)} ${noun}${count === 1 ? "" : "s"}`;
}

// For a phrase that also appears inside sentences, such as "step 4 of 9", when it starts one.
export function upperFirst(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

// Rounded down, so 100% appears only once everything is there.
export function percentOf(part: number, whole: number): number {
  if (whole <= 0) {
    return 0;
  }

  return Math.min(100, Math.floor((part * 100) / whole));
}
