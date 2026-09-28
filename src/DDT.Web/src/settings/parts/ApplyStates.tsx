// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";

import { StateTag } from "@/ui/StateTag";

import { settingsText, type SettingsApplyState } from "../settings";

// How far each host has applied a section that rebuilds a subsystem, as of the section's version.
export function ApplyStates({
  states,
  version,
}: {
  states: SettingsApplyState[];
  version: number;
}) {
  const { t: translate } = useLingui();

  return (
    <ul aria-label={translate`Where it applies`} className="flex flex-col gap-1.5">
      {states.map((state) => {
        const host = state.host;
        const pending = state.state === "Pending" || state.version < version;

        return (
          <li key={host} className="flex flex-wrap items-center gap-2 type-small">
            <StateTag tone={state.state === "Failed" ? "fail" : pending ? "idle" : "ok"}>
              {state.state === "Failed"
                ? translate`Failed`
                : pending
                  ? translate`Pending`
                  : translate`Applied`}
            </StateTag>
            <span className="type-data text-ink">{host}</span>
            {/* A failure's reason can be long, so it reads as a line of its own. */}
            {state.message !== null ? (
              <span
                className={`min-w-0 break-words text-ink-2 ${state.state === "Failed" ? "basis-full" : ""}`}
              >
                {settingsText({ message: state.message, text: state.text })}
              </span>
            ) : null}
          </li>
        );
      })}
    </ul>
  );
}
