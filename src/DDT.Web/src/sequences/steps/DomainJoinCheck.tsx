// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation } from "@tanstack/react-query";

import {
  checkDomainJoin,
  domainFindingText,
  type DomainJoinFindingLevel,
} from "@/deployments/domainJoinCheck";
import { ApiError } from "@/lib/api";
import { Button } from "@/ui/Button";
import { cx } from "@/ui/cx";
import { StateTag, type StateTone } from "@/ui/StateTag";

const levelTone: Record<DomainJoinFindingLevel, StateTone> = {
  Passed: "ok",
  Warning: "attention",
  Problem: "fail",
};

// Asks the domain whether the join account can join a machine into this organizational unit, the way the step would.
// That way a machine doesn't find out in the middle of its run. The server checks with its current settings.
export function DomainJoinCheck({
  organizationalUnit,
  className,
}: {
  organizationalUnit: string | null;
  className?: string;
}) {
  const { t } = useLingui();
  const check = useMutation({ mutationFn: checkDomainJoin });
  const result = check.data;
  const stale = result !== undefined && check.variables !== organizationalUnit;
  const failure = check.error?.message ?? "";
  const levelLabel: Record<DomainJoinFindingLevel, string> = {
    Passed: t`OK`,
    Warning: t`Warning`,
    Problem: t`Problem`,
  };

  return (
    <div className={cx("flex flex-col gap-2.5 rounded-key bg-well px-3.5 py-3", className)}>
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
        <Button
          size="sm"
          isDisabled={check.isPending}
          onPress={() => {
            check.mutate(organizationalUnit);
          }}
        >
          {check.isPending ? (
            <Trans>Checking the join account</Trans>
          ) : (
            <Trans>Check the join account</Trans>
          )}
        </Button>
        {result !== undefined ? (
          <span
            role="status"
            className={cx("type-label", result.canJoin ? "text-ok-text" : "text-fail-text")}
          >
            {result.canJoin ? (
              <Trans>It can join machines here.</Trans>
            ) : (
              <Trans>It cannot join machines here.</Trans>
            )}
            {stale ? (
              <span className="font-normal text-ink-2">
                {" "}
                <Trans>The organizational unit changed since, so check again.</Trans>
              </span>
            ) : null}
          </span>
        ) : null}
      </div>
      {check.error !== null ? (
        <p role="alert" className="type-small text-fail-text">
          {check.error instanceof ApiError && check.error.status === 403 ? (
            <Trans>Only administrators may check the join account.</Trans>
          ) : (
            <Trans>The check did not run: {failure}</Trans>
          )}
        </p>
      ) : null}
      {result !== undefined ? (
        <ul aria-label={t`What the check found`} className="flex flex-col gap-1.5">
          {result.findings.map((finding, index) => (
            <li key={index} className="flex items-start gap-2.5 type-small text-ink">
              <StateTag tone={levelTone[finding.level]} className="h-5 px-1.5">
                {levelLabel[finding.level]}
              </StateTag>
              <span className="pt-0.5">{domainFindingText(finding)}</span>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
