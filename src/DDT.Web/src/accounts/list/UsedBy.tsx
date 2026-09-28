// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { Link } from "@tanstack/react-router";
import { Fragment } from "react";

import type { AccountStepUse, AccountUse, AccountView } from "../accounts";

// A sequence's steps that name the account, each once though it may name it twice, to run as and for a share.
function stepsOf(use: AccountUse): AccountStepUse[] {
  return (use.steps ?? []).filter(
    (step, index, all) => all.findIndex((other) => other.stepId === step.stepId) === index,
  );
}

// The sequences that name the account, each with the steps that do, which open the sequence at that step.
export function UsedBy({ account }: { account: AccountView }) {
  if (account.usedBy.length === 0) {
    return (
      <span className="text-muted">
        <Trans>No sequence</Trans>
      </span>
    );
  }

  return (
    <ul className="flex min-w-0 flex-col gap-0.5">
      {account.usedBy.map((use) => (
        <li key={use.sequenceId} className="min-w-0">
          <Link
            to="/deployment/sequences/$sequenceId"
            params={{ sequenceId: use.sequenceId }}
            className="text-ink hover:underline"
          >
            {use.sequenceName}
          </Link>
          {stepsOf(use).length > 0 ? (
            <span className="text-muted">
              {": "}
              {stepsOf(use).map((step, index) => (
                <Fragment key={step.stepId}>
                  {index > 0 ? ", " : null}
                  <Link
                    to="/deployment/sequences/$sequenceId"
                    params={{ sequenceId: use.sequenceId }}
                    search={{ step: step.stepId }}
                    className="text-ink-2 hover:underline"
                  >
                    {step.stepName}
                  </Link>
                </Fragment>
              ))}
            </span>
          ) : null}
        </li>
      ))}
    </ul>
  );
}
