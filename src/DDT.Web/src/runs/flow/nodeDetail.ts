// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { conditionSummary } from "@/conditions/conditions";
import type { Subject } from "@/conditions/conditionSubjects";
import { formatDuration } from "@/lib/format";

import { outcomesOf, outcomeText, repeatText } from "../decisions";
import type { PathNode } from "../runPath";
import { stepDuration } from "../runs";

export interface NodeDetail {
  text: string;
  code: boolean;
}

// The single line a node's card shows on a run: how long it took or has been running, why it was skipped, what it
// tested, or the value it set.
export function nodeDetail(
  node: PathNode,
  subjects: readonly Subject[],
  variables: Record<string, string>,
  now: number,
): NodeDetail {
  const plain = (text: string) => ({ text, code: false });
  const step = node.step;
  const duration = step === null ? null : stepDuration(step, now);
  const took = duration === null ? null : formatDuration(duration);

  if (node.state === "notTaken") {
    return plain(t`Not taken`);
  }

  switch (node.node.kind) {
    case "if":
      return plain(conditionSummary(node.node.test, subjects));
    case "repeat":
      if (step !== null && (step.iteration ?? 0) > 0) {
        return plain(repeatText(node.node.maxTimes, step));
      }
      break;
    case "setVariable":
      if (node.state === "done") {
        const name = node.node.variable;
        const set =
          Object.entries(variables).find(
            ([candidate]) => candidate.toLowerCase() === name.toLowerCase(),
          )?.[1] ?? node.node.value;

        return { text: `${name} = ${set}`, code: true };
      }
      break;
    default:
      break;
  }

  switch (node.state) {
    case "waiting":
      return plain(t`Not started`);
    case "running":
      return plain(took === null ? t`Running` : t`Running for ${took}`);
    case "paused":
      return plain(took === null ? t`Paused` : t`Paused for ${took}`);
    case "failed":
      return plain(step?.error ?? t`Failed`);
    case "skipped": {
      const [reason] = outcomesOf(node.node, step, "condition").filter(
        (outcome) => !outcome.held && outcome.test !== null,
      );

      if (reason === undefined) {
        return plain(t`Skipped`);
      }

      const reasons = outcomeText(reason, subjects);

      return plain(t`Skipped: ${reasons}`);
    }
    case "done": {
      const passes = step?.pass ?? 0;

      if (took === null) {
        return plain(t`Done`);
      }

      return plain(passes > 1 ? t`${passes} passes, the last took ${took}` : t`Took ${took}`);
    }
  }
}
