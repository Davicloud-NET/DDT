// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

export interface LoggingSettings {
  // The level per category. Default applies to every category that isn't listed.
  logLevel: Record<string, string>;
}

// The levels DDT starts with, the same ones the server's code sets.
export const DEFAULT_LOG_LEVELS: Record<string, string> = {
  Default: "Information",
  "Microsoft.AspNetCore": "Warning",
  "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
  "Microsoft.EntityFrameworkCore.Infrastructure": "Warning",
};

export const LOG_LEVELS = [
  "Trace",
  "Debug",
  "Information",
  "Warning",
  "Error",
  "Critical",
  "None",
] as const;

export type LogLevel = (typeof LOG_LEVELS)[number];

// A category's row as typed. The id keeps the row in place while the category changes.
export interface CategoryLevel {
  id: number;
  category: string;
  level: string;
  // The Default row can't be removed, because every other category logs at its level.
  fixed: boolean;
}

export function rowsOf(logLevel: Record<string, string>): CategoryLevel[] {
  return Object.entries(logLevel).map(([category, level], id) => ({
    id,
    category,
    level,
    fixed: category.toLowerCase() === "default",
  }));
}

// Rows without a category are still being typed, and are not part of the section yet.
export function levelsOf(rows: CategoryLevel[]): Record<string, string> {
  return Object.fromEntries(
    rows.filter((row) => row.category.trim() !== "").map((row) => [row.category.trim(), row.level]),
  );
}

// Returns the list's spelling of a level that the section spells in another case.
export function knownLevel(level: string): LogLevel | undefined {
  return LOG_LEVELS.find((known) => known.toLowerCase() === level.toLowerCase());
}

// Categories match case-insensitively. When two rows name the same category, the lower row's level applies.
export function isListedAgain(rows: CategoryLevel[], index: number): boolean {
  const category = rows[index]?.category.trim().toLowerCase() ?? "";

  return rows.slice(index + 1).some((other) => other.category.trim().toLowerCase() === category);
}

export function withCategory(rows: CategoryLevel[], id: number, category: string): CategoryLevel[] {
  return rows.map((other) => (other.id === id ? { ...other, category } : other));
}

export function withLevel(rows: CategoryLevel[], id: number, level: string): CategoryLevel[] {
  return rows.map((other) => (other.id === id ? { ...other, level } : other));
}

export function withoutRow(rows: CategoryLevel[], id: number): CategoryLevel[] {
  return rows.filter((other) => other.id !== id);
}

export function withNewRow(rows: CategoryLevel[]): CategoryLevel[] {
  return [
    ...rows,
    {
      id: Math.max(-1, ...rows.map((row) => row.id)) + 1,
      category: "",
      level: "Debug",
      fixed: false,
    },
  ];
}
