// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import type { ReactNode } from "react";

import { ListBoxItem, Select } from "@/ui/Select";

import { knownLevel, LOG_LEVELS, type LogLevel } from "./logLevels";

export function LevelSelect({
  label,
  value,
  isDisabled,
  onChange,
}: {
  label: ReactNode;
  value: string;
  isDisabled: boolean;
  onChange: (level: string) => void;
}) {
  return (
    <Select
      label={label}
      value={knownLevel(value) ?? value}
      isDisabled={isDisabled}
      onChange={(key) => {
        if (key !== null) {
          onChange(String(key));
        }
      }}
    >
      {LOG_LEVELS.map((level) => (
        <ListBoxItem key={level} id={level} description={<LevelMeaning level={level} />}>
          {level}
        </ListBoxItem>
      ))}
    </Select>
  );
}

function LevelMeaning({ level }: { level: LogLevel }) {
  switch (level) {
    case "Trace":
      return <Trans>Everything, a line for every packet and request</Trans>;
    case "Debug":
      return <Trans>Details for finding a fault</Trans>;
    case "Information":
      return <Trans>The normal course of events</Trans>;
    case "Warning":
      return <Trans>Only what went unexpectedly</Trans>;
    case "Error":
      return <Trans>Only failures</Trans>;
    case "Critical":
      return <Trans>Only failures that stop the server or a part of it</Trans>;
    case "None":
      return <Trans>Nothing</Trans>;
  }
}
