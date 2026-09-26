// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconX } from "@tabler/icons-react";
import { useMemo, useState } from "react";
import { Button as AriaButton } from "react-aria-components";

import type { DeploymentStepView } from "@/deployments/deployments";
import { formattingLocale } from "@/i18n/i18n";
import { number } from "@/lib/format";
import { Button } from "@/ui/Button";
import { FilterChips, FilterSelector, SearchField } from "@/ui/Controls";
import { Panel } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";

import {
  clockNote,
  filterLines,
  logLevels,
  STORED_LINES_PER_MACHINE,
  type AgentLogLevel,
  type MachineLogEntry,
} from "./log";
import { MAX_BUFFERED_LINES } from "./logBuffer";
import { LogViewport } from "./LogViewport";
import { LOG_POLL_MS, useMachineLog } from "./useMachineLog";

type LogScope = "run" | "machine";

export interface LogPanelProps {
  machineId: string;
  // The run the page shows, whose lines the panel shows first.
  runId: string | null;
  active: boolean;
  steps: readonly DeploymentStepView[];
  // Only this step's lines are shown.
  stepFilter: string | null;
  onStepFilterChange: (stepId: string | null) => void;
}

function stepLabel(steps: readonly DeploymentStepView[], stepId: string | null): string | null {
  const step = steps.find((candidate) => candidate.stepId === stepId);

  if (step === undefined) {
    return null;
  }

  const position = step.index + 1;
  const name = step.name;

  return t`step ${position}, ${name}`;
}

function levelLabel(level: AgentLogLevel): string {
  switch (level) {
    case "Information":
      return t`Information`;
    case "Warning":
      return t`Warnings`;
    case "Error":
      return t`Errors`;
  }
}

// A machine's log as the agent sent it: the run's lines or all of the machine's, filtered by level, text and
// step. New lines arrive live; scrolling up pauses following, and a bar says how many arrived meanwhile.
export function LogPanel({
  machineId,
  runId,
  active,
  steps,
  stepFilter,
  onStepFilterChange,
}: LogPanelProps) {
  const { t: translate } = useLingui();
  const [scope, setScope] = useState<LogScope>("run");
  const deploymentId = scope === "run" ? runId : null;
  const log = useMachineLog(machineId, deploymentId, active);
  const [levels, setLevels] = useState<ReadonlySet<string>>(() => new Set(logLevels));
  const [search, setSearch] = useState("");
  const [following, setFollowing] = useState(true);
  const [pausedAt, setPausedAt] = useState<number | null>(null);
  const [selected, setSelected] = useState<MachineLogEntry | null>(null);

  // Other lines replace the loaded ones, so the view starts again at the newest.
  const [shownScope, setShownScope] = useState(deploymentId);

  if (shownScope !== deploymentId) {
    setShownScope(deploymentId);
    setFollowing(true);
    setPausedAt(null);
    setSelected(null);
  }

  const all = log.buffer.lines;
  const lines = useMemo(
    () =>
      filterLines(all, {
        levels: levels as ReadonlySet<AgentLogLevel>,
        search,
        stepId: stepFilter,
      }),
    [all, levels, search, stepFilter],
  );
  const counts = useMemo(() => {
    const byLevel = new Map<AgentLogLevel, number>();

    for (const line of all) {
      byLevel.set(line.level, (byLevel.get(line.level) ?? 0) + 1);
    }

    return byLevel;
  }, [all]);
  const newLines = pausedAt === null ? 0 : lines.filter((line) => line.id > pausedAt).length;
  const full = all.length >= MAX_BUFFERED_LINES;
  const filteredStep = stepLabel(steps, stepFilter);
  const loaded = all.length;
  const bufferLimit = number(MAX_BUFFERED_LINES);
  const storedLimit = number(STORED_LINES_PER_MACHINE);
  const pollSeconds = LOG_POLL_MS / 1000;
  const readError = log.error;

  return (
    <Panel
      title={<Trans>Log</Trans>}
      actions={
        runId === null ? null : (
          <FilterSelector
            label={translate`Which lines`}
            selected={scope}
            onChange={(id) => {
              setScope(id === "machine" ? "machine" : "run");
            }}
            options={[
              { id: "run", label: translate`This run` },
              { id: "machine", label: translate`Whole machine` },
            ]}
          />
        )
      }
    >
      <div className="flex flex-wrap items-center gap-3">
        <FilterChips
          label={translate`Levels`}
          selected={levels}
          onChange={setLevels}
          options={logLevels.map((level) => ({
            id: level,
            label: levelLabel(level),
            count: counts.get(level) ?? 0,
            ...(level === "Error" ? { tone: "fail" as const } : {}),
            ...(level === "Warning" ? { tone: "attention" as const } : {}),
          }))}
        />
        <div className="flex-1" />
        <SearchField
          label={plural(loaded, {
            one: "Search the # loaded line",
            other: "Search the # loaded lines",
          })}
          placeholder={translate`Search the log`}
          value={search}
          onChange={setSearch}
        />
      </div>

      {filteredStep !== null ? (
        <p className="flex flex-wrap items-center gap-2 type-small text-ink-2">
          <Trans>Only the lines of {filteredStep}.</Trans>
          <Button
            size="sm"
            variant="quiet"
            onPress={() => {
              onStepFilterChange(null);
            }}
          >
            <Trans>Show every step</Trans>
          </Button>
        </p>
      ) : null}

      {readError !== null ? (
        <Notice tone="fail">
          <Trans>The log could not be read: {readError}</Trans>
        </Notice>
      ) : null}

      <div className="flex flex-wrap items-center gap-3 type-small text-muted">
        {log.buffer.hasOlder && !full ? (
          <Button size="sm" isDisabled={log.loadingOlder} onPress={log.loadOlder}>
            {log.loadingOlder ? (
              <Trans>Loading older lines</Trans>
            ) : (
              <Trans>Load older lines</Trans>
            )}
          </Button>
        ) : null}
        {log.buffer.hasOlder && full ? (
          <span>
            <Trans>
              This page holds at most {bufferLimit} lines. Older ones stay on the server.
            </Trans>
          </span>
        ) : null}
        {log.loaded && !log.buffer.hasOlder && all.length > 0 ? (
          <span>
            <Trans>
              Start of the stored log. The server keeps the newest {storedLimit} lines of each
              machine.
            </Trans>
          </span>
        ) : null}
        <span className="flex-1" />
        <span>
          {log.polling ? (
            <Trans>Reading every {pollSeconds} s, because the live connection is down.</Trans>
          ) : log.status === "reconnecting" ? (
            <Trans>Reconnecting. Lines sent meanwhile are read once the connection is back.</Trans>
          ) : log.status === "live" ? null : (
            <Trans>The live connection is down. New lines appear once it is back.</Trans>
          )}
        </span>
      </div>

      {log.loaded && all.length === 0 && log.error === null ? (
        <p className="py-6 text-ink-2">
          {deploymentId === null ? (
            <Trans>This machine has sent no log lines yet.</Trans>
          ) : (
            <Trans>No log lines for this run yet. The agent sends its log while it runs.</Trans>
          )}
        </p>
      ) : (
        <div className="relative">
          <LogViewport
            lines={lines}
            following={following}
            onFollowingChange={(next) => {
              setFollowing(next);
              setPausedAt(next ? null : (lines.at(-1)?.id ?? null));
            }}
            selectedId={selected?.id ?? null}
            onSelect={setSelected}
          />
          {all.length > 0 && lines.length === 0 ? (
            <p className="absolute inset-x-0 top-4 text-center type-small text-console-muted">
              <Trans>No loaded line matches the filter.</Trans>
            </p>
          ) : null}
          {!following ? (
            <div
              role="status"
              className="absolute right-3 bottom-3 flex items-center gap-3 rounded-key bg-raised px-3 py-1.5 type-small shadow-overlay"
            >
              <span>
                {newLines > 0 ? (
                  plural(newLines, { one: "Paused, # new line", other: "Paused, # new lines" })
                ) : (
                  <Trans>Paused</Trans>
                )}
              </span>
              <Button
                size="sm"
                variant="primary"
                onPress={() => {
                  setFollowing(true);
                  setPausedAt(null);
                }}
              >
                <Trans>Jump to the newest</Trans>
              </Button>
            </div>
          ) : null}
        </div>
      )}

      {selected !== null ? (
        <LineDetail
          line={selected}
          step={stepLabel(steps, selected.stepId)}
          onClose={() => {
            setSelected(null);
          }}
        />
      ) : null}
    </Panel>
  );
}

// A row shows one line of a message; this shows all of it with every time the server knows.
function LineDetail({
  line,
  step,
  onClose,
}: {
  line: MachineLogEntry;
  step: string | null;
  onClose: () => void;
}) {
  const { t: translate } = useLingui();
  const locale = formattingLocale();
  const note = clockNote(line);
  const at = new Date(line.timestampUtc).toLocaleString(locale);
  const received = new Date(line.receivedUtc).toLocaleString(locale);
  const level = levelLabel(line.level);

  return (
    <section
      aria-label={translate`Log line`}
      className="flex flex-col gap-2 rounded-key bg-well p-3 shadow-[inset_0_0_0_1px_var(--color-line-soft)]"
    >
      <div className="flex items-start gap-3">
        <p className="flex-1 type-small text-ink-2">
          {step === null ? (
            <Trans>
              {level} at {at}. Received {received}.
            </Trans>
          ) : (
            <Trans>
              {level} at {at}, during {step}. Received {received}.
            </Trans>
          )}
          {note !== null ? ` ${note}` : null}
        </p>
        <AriaButton
          aria-label={translate`Close the line`}
          onPress={onClose}
          className="flex size-7 cursor-pointer items-center justify-center rounded-key text-muted outline-none hover:bg-hover hover:text-ink focus-visible:outline-2 focus-visible:outline-focus"
        >
          <IconX size={16} stroke={2} />
        </AriaButton>
      </div>
      <pre className="max-h-64 overflow-auto type-data whitespace-pre-wrap text-ink">
        {line.message}
      </pre>
    </section>
  );
}
