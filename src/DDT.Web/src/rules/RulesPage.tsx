// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconDots } from "@tabler/icons-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { useState } from "react";
import { Button as AriaButton, MenuTrigger } from "react-aria-components";

import { currentUserQuery } from "@/auth/auth";
import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { machinesQuery, modelsQuery } from "@/machines/machines";
import { sequencesQuery } from "@/sequences/sequences";
import { Button } from "@/ui/Button";
import { ConfirmDialog } from "@/ui/Dialog";
import { EmptyState, Page, PageHeader, Panel, Skeleton } from "@/ui/Layout";
import { Menu, MenuItem } from "@/ui/Menu";
import { Notice } from "@/ui/Notice";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";

import { RuleDialog } from "./RuleDialog";
import {
  deleteRule,
  describeRule,
  ruleDeletionConsequence,
  ruleMatches,
  rulesQuery,
  ruleTarget,
  sequenceResolutionsKey,
  type AssignmentRuleKind,
  type AssignmentRuleView,
} from "./rules";

// The rules that choose a sequence for machines by MAC address or hardware model. The list is live: a save here
// patches it from the server's answer, and another administrator's change arrives through the hub.
export function RulesPage() {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const rules = useQuery(rulesQuery);
  const sequences = useQuery(sequencesQuery);
  const machines = useQuery(machinesQuery);
  const models = useQuery(modelsQuery);
  const user = useQuery(currentUserQuery).data ?? null;
  const now = useNow(30_000);
  const canEdit = user?.roles.includes("Administrator") === true;

  // Null while no dialog is open; a rule to change it, "new" to add one.
  const [editing, setEditing] = useState<AssignmentRuleView | "new" | null>(null);
  const [deleting, setDeleting] = useState<AssignmentRuleView | null>(null);

  const remove = useMutation({
    mutationFn: (id: string) => deleteRule(id),
    onSuccess: (_, id) => {
      queryClient.setQueryData(rulesQuery.queryKey, (list) =>
        list?.filter((rule) => rule.id !== id),
      );
      void queryClient.invalidateQueries({ queryKey: sequenceResolutionsKey });
      setDeleting(null);
    },
  });

  const list = rules.data ?? [];
  const sequenceList = sequences.data ?? [];
  const machineList = machines.data ?? [];
  const modelList = models.data ?? [];

  const section = (kind: AssignmentRuleKind) => {
    const ofKind = list.filter((rule) => rule.kind === kind);

    if (ofKind.length === 0) {
      return null;
    }

    return (
      <Panel
        title={kind === "Mac" ? <Trans>By MAC address</Trans> : <Trans>By hardware model</Trans>}
        flush
      >
        <Table
          aria-label={kind === "Mac" ? t`Rules by MAC address` : t`Rules by hardware model`}
          className="min-w-[720px] table-fixed"
        >
          <TableHeader>
            <TableColumn id="target" isRowHeader className="w-[30%] pl-4">
              {kind === "Mac" ? <Trans>MAC address</Trans> : <Trans>Model</Trans>}
            </TableColumn>
            <TableColumn id="sequence">
              <Trans>Task sequence</Trans>
            </TableColumn>
            <TableColumn id="matches" className="w-40">
              <Trans>Matches</Trans>
            </TableColumn>
            <TableColumn id="changed" className="w-44">
              <Trans>Changed</Trans>
            </TableColumn>
            <TableColumn id="actions" className="w-14 pr-4">
              <span className="sr-only">
                <Trans>Actions</Trans>
              </span>
            </TableColumn>
          </TableHeader>
          <TableBody items={ofKind} dependencies={[machineList, modelList, now, canEdit]}>
            {(rule) => (
              <TableRow id={rule.id} textValue={ruleTarget(rule)}>
                <TableCell className="pl-4">
                  <span className="flex min-w-0 flex-col">
                    <span className={kind === "Mac" ? "truncate type-data" : "truncate type-label"}>
                      {ruleTarget(rule)}
                    </span>
                    {rule.description !== null ? (
                      <span className="truncate type-small text-muted">{rule.description}</span>
                    ) : null}
                  </span>
                </TableCell>
                <TableCell>
                  <Link
                    to="/deployment/sequences/$sequenceId"
                    params={{ sequenceId: rule.sequenceId }}
                    className="truncate hover:underline"
                  >
                    {rule.sequenceName}
                  </Link>
                </TableCell>
                <TableCell className="type-small text-ink-2">
                  <MatchCount count={ruleMatches(rule, machineList, modelList)} />
                </TableCell>
                <TableCell className="type-small text-muted">
                  <ChangedBy rule={rule} now={now} />
                </TableCell>
                <TableCell className="pr-4">
                  {canEdit ? (
                    <RuleMenu
                      rule={rule}
                      onEdit={() => {
                        setEditing(rule);
                      }}
                      onDelete={() => {
                        remove.reset();
                        setDeleting(rule);
                      }}
                    />
                  ) : null}
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </Panel>
    );
  };

  return (
    <Page>
      <PageHeader title={<Trans>Assignment rules</Trans>}>
        <div className="flex-1" />
        {canEdit ? (
          <Button
            variant="primary"
            isDisabled={sequenceList.length === 0}
            onPress={() => {
              setEditing("new");
            }}
          >
            <Trans>Add rule</Trans>
          </Button>
        ) : null}
      </PageHeader>

      <p className="max-w-[80ch] text-ink-2">
        <Trans>
          A rule chooses the task sequence for a machine that has none. It never authorizes a
          machine: someone still signs in at it, or an operator approves it. A sequence assigned on
          the web or chosen at the machine comes first; then a rule for one of the machine's MAC
          addresses, then a rule for its model, the exact model before one ending in *, and a rule
          that names the manufacturer before one for any.
        </Trans>
      </p>

      {rules.isError ? (
        <Notice tone="fail">
          <Trans>The rules could not be loaded.</Trans>
        </Notice>
      ) : null}

      {canEdit && sequences.isSuccess && sequenceList.length === 0 ? (
        <Notice tone="info">
          <Trans>
            A rule needs a task sequence to choose. Create one under Deployment, Task sequences
            first.
          </Trans>
        </Notice>
      ) : null}

      {rules.isPending ? (
        <Panel>
          <Skeleton className="h-6 w-1/3" />
          <Skeleton className="h-6 w-2/3" />
          <Skeleton className="h-6 w-1/2" />
        </Panel>
      ) : rules.isSuccess && list.length === 0 ? (
        <Panel>
          <EmptyState title={<Trans>No rules yet</Trans>}>
            {canEdit ? (
              <Trans>
                Without rules an operator assigns a sequence to each machine. Add a rule to choose
                one by hardware model or MAC address.
              </Trans>
            ) : (
              <Trans>
                Without rules an operator assigns a sequence to each machine. An administrator adds
                rules here.
              </Trans>
            )}
          </EmptyState>
        </Panel>
      ) : (
        <>
          {section("Model")}
          {section("Mac")}
        </>
      )}

      {editing !== null ? (
        <RuleDialog
          rule={editing === "new" ? null : editing}
          sequences={sequenceList}
          machines={machineList}
          models={modelList}
          onClose={() => {
            setEditing(null);
          }}
        />
      ) : null}

      <ConfirmDialog
        isOpen={deleting !== null}
        onOpenChange={(open) => {
          if (!open) {
            setDeleting(null);
          }
        }}
        title={deleting === null ? "" : <DeleteTitle description={describeRule(deleting)} />}
        confirmLabel={<Trans>Delete rule</Trans>}
        danger
        isBusy={remove.isPending}
        error={remove.isError ? remove.error.message : undefined}
        onConfirm={() => {
          if (deleting !== null) {
            remove.mutate(deleting.id);
          }
        }}
      >
        <p>{deleting === null ? null : ruleDeletionConsequence(deleting)}</p>
      </ConfirmDialog>
    </Page>
  );
}

function DeleteTitle({ description }: { description: string }) {
  return <Trans>Delete the rule for {description}?</Trans>;
}

function MatchCount({ count }: { count: number }) {
  return count === 0 ? (
    <Trans>No registered machine</Trans>
  ) : (
    <>{plural(count, { one: "# machine", other: "# machines" })}</>
  );
}

function ChangedBy({ rule, now }: { rule: AssignmentRuleView; now: number }) {
  const when = relativeTime(rule.updatedUtc, now);
  const by = rule.updatedBy;

  return (
    <span title={new Date(rule.updatedUtc).toLocaleString()}>
      {by === null ? (
        when
      ) : (
        <Trans>
          {when} by {by}
        </Trans>
      )}
    </span>
  );
}

function RuleMenu({
  rule,
  onEdit,
  onDelete,
}: {
  rule: AssignmentRuleView;
  onEdit: () => void;
  onDelete: () => void;
}) {
  const { t } = useLingui();
  const description = describeRule(rule);

  return (
    <MenuTrigger>
      <AriaButton
        aria-label={t`Actions for the rule for ${description}`}
        className="flex size-7.5 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed hover:text-ink focus-visible:outline-2 focus-visible:outline-focus"
      >
        <IconDots size={18} stroke={2} />
      </AriaButton>
      <Menu
        aria-label={t`Actions for the rule for ${description}`}
        onAction={(key) => {
          if (key === "edit") {
            onEdit();
          } else {
            onDelete();
          }
        }}
      >
        <MenuItem id="edit">
          <Trans>Change</Trans>
        </MenuItem>
        <MenuItem id="delete" className="text-fail-text">
          <Trans>Delete</Trans>
        </MenuItem>
      </Menu>
    </MenuTrigger>
  );
}
