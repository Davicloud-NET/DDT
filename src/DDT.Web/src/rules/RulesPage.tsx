// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import { liveListOptions } from "@/live/freshness";
import { useLiveMarks } from "@/live/useLiveMarks";
import { useLiveStatus } from "@/live/useLiveStatus";
import { machineRolesQuery } from "@/roles/roles";
import { sequencesQuery } from "@/sequences/sequences";
import { ListSkeleton } from "@/ui/ListSkeleton";
import { Notice } from "@/ui/Notice";
import { Page } from "@/ui/Page";
import { Panel } from "@/ui/Panel";

import { DeleteRuleDialog } from "./DeleteRuleDialog";
import { NoRules } from "./list/NoRules";
import { RuleList } from "./list/RuleList";
import { useRuleMoves } from "./list/useRuleMoves";
import { RuleDrawer } from "./RuleDrawer";
import { rulesQuery, type RuleView } from "./rules";
import { RulesHeader } from "./RulesHeader";
import { RuleTest } from "./RuleTest";
import { useRuleDrawer } from "./useRuleDrawer";
import { useRuleSubjects } from "./useRuleSubjects";

// The rules, one ordered list checked from the top: the first rule that chooses a sequence or sets a value wins it,
// and every rule that matches gives its machine roles.
export function RulesPage() {
  const freshness = liveListOptions(useLiveStatus());
  const rules = useQuery({ ...rulesQuery, ...freshness });
  const roles = useQuery({ ...machineRolesQuery, ...freshness });
  const sequences = useQuery({ ...sequencesQuery, ...freshness });
  const subjects = useRuleSubjects();
  const canEdit = useIsAdministrator();
  const { drawer, open, close, showSaved } = useRuleDrawer(rules.data);
  const mark = useLiveMarks({
    queryKey: rulesQuery.queryKey,
    items: (list) => list,
    id: (rule) => rule.id,
    signature: (rule) => `${String(rule.revision)} ${String(rule.position)}`,
    tone: () => "idle",
  });
  const [deleting, setDeleting] = useState<RuleView | null>(null);

  const list = rules.data ?? [];
  const roleList = roles.data ?? [];
  const moves = useRuleMoves(list);

  return (
    <Page className="max-w-230">
      <RulesHeader
        canEdit={canEdit}
        onAdd={() => {
          open(null);
        }}
      />

      {rules.isError ? (
        <Notice tone="fail">
          <Trans>The rules could not be loaded.</Trans>
        </Notice>
      ) : null}

      {moves.problem !== null ? (
        <Notice tone={moves.problem.tone}>{moves.problem.text}</Notice>
      ) : null}

      {rules.isPending ? (
        <Panel>
          <ListSkeleton widths={["w-1/3", "w-2/3", "w-1/2"]} padded={false} />
        </Panel>
      ) : rules.isSuccess && list.length === 0 ? (
        <NoRules canEdit={canEdit} />
      ) : rules.isSuccess ? (
        <RuleList
          list={list}
          roles={roleList}
          subjects={subjects}
          canEdit={canEdit}
          openId={drawer?.rule?.id ?? null}
          mark={mark}
          onOpen={open}
          onReorder={moves.reorder}
          onMoveBy={moves.moveBy}
          onDelete={setDeleting}
        />
      ) : null}

      {rules.isSuccess ? <RuleTest rules={list} /> : null}

      {drawer !== null ? (
        <RuleDrawer
          key={drawer.key}
          rule={drawer.rule}
          count={list.length}
          roles={roleList}
          sequences={sequences.data ?? []}
          subjects={subjects}
          canEdit={canEdit}
          onSaved={showSaved}
          onClose={close}
        />
      ) : null}

      {deleting !== null ? (
        <DeleteRuleDialog
          rule={deleting}
          onClose={() => {
            setDeleting(null);
          }}
          onDeleted={() => {
            setDeleting(null);
          }}
        />
      ) : null}
    </Page>
  );
}
