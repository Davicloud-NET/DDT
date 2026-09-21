// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

const units = ["KB", "MB", "GB", "TB"] as const;
const number = new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 });

// Binary multiples with the labels Windows shows, so a size here matches what Explorer and Disk
// Management report for the same file or disk.
export function formatBytes(bytes: number): string {
  if (bytes < 1024) {
    return `${number.format(bytes)} bytes`;
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

  return `${number.format(value)} ${unit}`;
}

export function formatDuration(milliseconds: number): string {
  const total = Math.max(0, Math.floor(milliseconds / 1000));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;

  if (hours > 0) {
    return `${String(hours)} h ${String(minutes)} min`;
  }

  if (minutes > 0) {
    return `${String(minutes)} min ${String(seconds)} s`;
  }

  return `${String(seconds)} s`;
}

// Rounded down, so 100% appears only once everything is there.
export function percentOf(part: number, whole: number): number {
  if (whole <= 0) {
    return 0;
  }

  return Math.min(100, Math.floor((part * 100) / whole));
}
