// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { CodesSetting } from "@/sequences/fields/CodesSetting";
import { Checkbox } from "@/ui/Checkbox";
import { NumberField } from "@/ui/NumberField";

import { MAX_TIMEOUT_MINUTES, MIN_TIMEOUT_MINUTES } from "../applications";
import { errorsAt } from "./errorsAt";
import type { ApplicationForm } from "./useApplicationForm";

// How a run treats the install: the exit codes that mean done or restart, the timeout, and a failure.
export function RunFields({ form }: { form: ApplicationForm }) {
  const { edit, findings, change } = form;

  return (
    <>
      <CodesSetting
        label={<Trans>Exit codes that mean success</Trans>}
        field="successExitCodes"
        findings={findings}
        value={edit.successExitCodes}
        onChange={(successExitCodes) => {
          change({ successExitCodes });
        }}
      />
      <CodesSetting
        label={<Trans>Exit codes that ask for a restart</Trans>}
        field="restartExitCodes"
        findings={findings}
        value={edit.restartExitCodes}
        onChange={(restartExitCodes) => {
          change({ restartExitCodes });
        }}
      />

      <div data-field="timeoutMinutes">
        <NumberField
          label={<Trans>Timeout in minutes</Trans>}
          className="w-48"
          minValue={MIN_TIMEOUT_MINUTES}
          maxValue={MAX_TIMEOUT_MINUTES}
          step={1}
          value={edit.timeoutMinutes}
          onChange={(minutes) => {
            // A cleared field keeps the last number.
            if (!Number.isNaN(minutes)) {
              change({ timeoutMinutes: minutes });
            }
          }}
          {...errorsAt(findings, "timeoutMinutes")}
        />
      </div>

      <div data-field="continueOnFailure">
        <Checkbox
          isSelected={edit.continueOnFailure}
          onChange={(continueOnFailure) => {
            change({ continueOnFailure });
          }}
        >
          <Trans>The run goes on if this fails</Trans>
        </Checkbox>
      </div>
    </>
  );
}
