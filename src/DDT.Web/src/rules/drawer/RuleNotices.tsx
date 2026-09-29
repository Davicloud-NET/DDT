// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";

import { Notice } from "@/ui/Notice";

import { DrawerNotices } from "../DrawerNotices";
import type { RuleForm } from "./useRuleForm";

// The notices over a rule's form, with the problems it was saved with.
export function RuleNotices({ form }: { form: RuleForm }) {
  const { t } = useLingui();
  const problemCount = form.base?.problems.length ?? 0;
  const who = form.theirs?.updatedBy ?? null;

  return (
    <DrawerNotices
      savedMeanwhile={
        form.theirs === null
          ? null
          : who === null
            ? t`Someone else saved this rule while you were editing it.`
            : t`${who} saved this rule while you were editing it.`
      }
      gone={form.gone ? <Trans>Someone deleted this rule while you were editing it.</Trans> : null}
      loose={form.loose}
      refused={form.refused}
      isBusy={form.busy}
      onTakeTheirs={form.takeTheirs}
      onKeepMine={form.keepMine}
    >
      {problemCount > 0 && form.theirs === null ? (
        <Notice tone="attention">
          {plural(problemCount, {
            one: "This rule has # problem, shown at its field. It matches no machine until it is fixed.",
            other:
              "This rule has # problems, shown at their fields. It matches no machine until they are fixed.",
          })}
        </Notice>
      ) : null}
    </DrawerNotices>
  );
}
