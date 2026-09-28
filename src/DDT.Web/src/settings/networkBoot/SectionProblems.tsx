// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Notice } from "@/ui/Notice";

import type { PxeForm } from "../networkBoot";

// The fields outside the boot targets that show their own problems; any other problem is listed here.
const PLACED_FIELDS = [
  "interfaces",
  "enableProxyDhcp",
  "enableTftp",
  "tftpSinglePort",
  "tftpMaxWindowSize",
  "maxConcurrentTftpTransfers",
  "authorisedRelayAgents",
];

// While the stored section has problems, no host serves anything.
export function SectionProblems({ form }: { form: PxeForm }) {
  const problems = form.view?.problems ?? [];
  const elsewhere = problems.filter(
    (problem) => !PLACED_FIELDS.includes(problem.field) && !problem.field.startsWith("bootTargets"),
  );

  if (problems.length === 0) {
    return null;
  }

  return (
    <Notice tone="fail">
      <span className="flex flex-col gap-1.5">
        <span>
          <Trans>
            These settings have problems, so no host serves network boot until they are fixed.
          </Trans>{" "}
          {elsewhere.length < problems.length ? (
            <Trans>The fields marked below say what.</Trans>
          ) : null}
        </span>
        {elsewhere.length > 0 ? (
          <ul className="flex list-disc flex-col gap-1 pl-5">
            {elsewhere.map((problem, index) => (
              <li key={index}>{problem.message}</li>
            ))}
          </ul>
        ) : null}
      </span>
    </Notice>
  );
}
