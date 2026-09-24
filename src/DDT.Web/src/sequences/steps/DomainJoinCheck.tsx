// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation } from "@tanstack/react-query";

import { checkDomainJoin, type DomainJoinFindingLevel } from "@/deployments/deployments";
import { ApiError } from "@/lib/api";

import styles from "./DomainJoinCheck.module.scss";

const levelText: Record<DomainJoinFindingLevel, string> = {
  Passed: "OK",
  Warning: "Warning",
  Problem: "Problem",
};

// Asks the domain whether the join account can join a machine into this step's organizational unit, the way the
// step would, before a machine finds out in the middle of its run. The server answers with its settings of the
// moment, so a check after they changed uses the new ones.
export function DomainJoinCheck({ organizationalUnit }: { organizationalUnit: string | null }) {
  const check = useMutation({ mutationFn: checkDomainJoin });
  const result = check.data;
  const stale = result !== undefined && check.variables !== organizationalUnit;

  return (
    <div className={styles.check}>
      <div className={styles.row}>
        <button
          type="button"
          className={styles.button}
          disabled={check.isPending}
          onClick={() => {
            check.mutate(organizationalUnit);
          }}
        >
          {check.isPending ? "Checking the join account…" : "Check the join account"}
        </button>
        {result !== undefined && (
          <span className={result.canJoin ? styles.canJoin : styles.cannotJoin} role="status">
            {result.canJoin ? "It can join machines here." : "It cannot join machines here."}
            {stale && " The organizational unit changed since, check again."}
          </span>
        )}
      </div>
      {check.error !== null && (
        <p className={styles.error} role="alert">
          {check.error instanceof ApiError && check.error.status === 403
            ? "Only administrators may check the join account."
            : `The check failed: ${check.error.message}`}
        </p>
      )}
      {result !== undefined && (
        <ul className={styles.findings} aria-label="What the check found">
          {result.findings.map((finding, index) => (
            <li key={index} className={styles[finding.level]}>
              <span className={styles.level}>{levelText[finding.level]}</span> {finding.text}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
