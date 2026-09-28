// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { formattingLocale } from "@/i18n/i18n";
import { apiGet } from "@/lib/api";
import { formatDuration } from "@/lib/format";

export type AgentLogLevel = "Information" | "Warning" | "Error";

export const logLevels: readonly AgentLogLevel[] = ["Information", "Warning", "Error"];

// timestampUtc is the agent's time corrected by how far its clock was off when it sent the line, which in
// Windows PE can be hours; agentTimestampUtc is the agent's own. receivedUtc is the server's. deploymentId is
// the run that was active when the line arrived, and stepId the step the agent was running then.
export interface MachineLogEntry {
  id: number;
  timestampUtc: string;
  receivedUtc: string;
  level: AgentLogLevel;
  message: string;
  agentTimestampUtc: string;
  deploymentId: string | null;
  stepId: string | null;
}

// Lines in the order they arrived. hasOlder says whether lines before the first one exist.
export interface MachineLogPage {
  lines: MachineLogEntry[];
  hasOlder: boolean;
}

// Without before or after, the newest lines. deploymentId keeps the lines of one run.
export interface LogRead {
  before?: number;
  after?: number;
  limit: number;
  deploymentId: string | null;
}

// The server's default read; it answers at most 1000 lines.
export const LOG_PAGE_LINES = 500;

// The server's MachineLogLimits.MaxStoredLinesPerMachine.
export const STORED_LINES_PER_MACHINE = 50_000;

// Below this the server does not correct the agent's time, because the difference is the network's delay.
const SKEW_TOLERANCE_MS = 2_000;

export function readLog(machineId: string, read: LogRead): Promise<MachineLogPage> {
  const query = new URLSearchParams();

  if (read.before !== undefined) {
    query.set("before", String(read.before));
  }

  if (read.after !== undefined) {
    query.set("after", String(read.after));
  }

  query.set("limit", String(read.limit));

  if (read.deploymentId !== null) {
    query.set("deploymentId", read.deploymentId);
  }

  return apiGet<MachineLogPage>(`/api/machines/${machineId}/log?${query.toString()}`);
}

// Null when the agent's clock agreed with the server's. The page never corrects times itself.
export function clockNote(line: MachineLogEntry): string | null {
  const offset = Date.parse(line.agentTimestampUtc) - Date.parse(line.timestampUtc);

  if (Math.abs(offset) <= SKEW_TOLERANCE_MS) {
    return null;
  }

  const said = new Date(line.agentTimestampUtc).toLocaleTimeString(formattingLocale());
  const off = formatDuration(Math.abs(offset));

  return offset < 0
    ? t`The agent's clock said ${said}, ${off} behind the server.`
    : t`The agent's clock said ${said}, ${off} ahead of the server.`;
}

export interface LogFilter {
  levels: ReadonlySet<AgentLogLevel>;
  search: string;
  stepId: string | null;
}

// Filters only the lines already loaded.
export function filterLines(
  lines: readonly MachineLogEntry[],
  filter: LogFilter,
): readonly MachineLogEntry[] {
  const needle = filter.search.trim().toLowerCase();

  if (filter.levels.size === logLevels.length && needle === "" && filter.stepId === null) {
    return lines;
  }

  return lines.filter(
    (line) =>
      filter.levels.has(line.level) &&
      (filter.stepId === null || line.stepId === filter.stepId) &&
      (needle === "" || line.message.toLowerCase().includes(needle)),
  );
}

export function countByLevel(lines: readonly MachineLogEntry[]): Map<AgentLogLevel, number> {
  const byLevel = new Map<AgentLogLevel, number>();

  for (const line of lines) {
    byLevel.set(line.level, (byLevel.get(line.level) ?? 0) + 1);
  }

  return byLevel;
}

// A row shows the first line of a message and how many follow.
export function firstLine(message: string): { text: string; more: number } {
  const lines = message.split(/\r?\n/);

  return { text: lines[0] ?? "", more: lines.length - 1 };
}
