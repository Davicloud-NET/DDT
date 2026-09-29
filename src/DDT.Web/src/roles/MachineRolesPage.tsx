// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { IconPlus } from "@tabler/icons-react";

import { Button } from "@/ui/Button";
import { EmptyState } from "@/ui/EmptyState";
import { ListSkeleton } from "@/ui/ListSkeleton";
import { Notice } from "@/ui/Notice";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";
import { Panel } from "@/ui/Panel";

import { DeleteRoleDialog } from "./DeleteRoleDialog";
import { RolesTable } from "./list/RolesTable";
import { RoleDrawer } from "./RoleDrawer";
import { useMachineRolesPage } from "./useMachineRolesPage";

// Machine roles, such as "Kiosk". A role is a set of values that rules give machines together. One rule can say
// "these are kiosks", and the role says what a kiosk gets. These aren't user roles.
export function MachineRolesPage() {
  const { roles, list, ruleList, canEdit, mark, drawer, deleting, open, close, setDeleting } =
    useMachineRolesPage();

  return (
    <Page>
      <PageHeader title={<Trans>Machine roles</Trans>}>
        <div className="flex-1" />
        {canEdit ? (
          <Button
            variant="primary"
            onPress={() => {
              open(null);
            }}
          >
            <IconPlus aria-hidden="true" size={14} stroke={2} />
            <Trans>Add machine role</Trans>
          </Button>
        ) : null}
      </PageHeader>

      <p className="max-w-[80ch] text-ink-2">
        <Trans>
          A machine role is a set of values that rules give machines together, such as the time zone
          and the computer name of a kiosk. It says nothing about what a person may do; that is a
          user's role, under Administration.
        </Trans>
      </p>

      {roles.isError ? (
        <Notice tone="fail">
          <Trans>The machine roles could not be loaded.</Trans>
        </Notice>
      ) : null}

      {roles.isPending ? (
        <Panel>
          <ListSkeleton widths={["w-1/3", "w-2/3"]} padded={false} />
        </Panel>
      ) : roles.isSuccess && list.length === 0 ? (
        <Panel>
          <EmptyState title={<Trans>No machine roles yet</Trans>}>
            {canEdit ? (
              <Trans>
                Add a machine role for values that belong together, then let a rule give it to the
                machines it matches.
              </Trans>
            ) : (
              <Trans>An administrator adds machine roles here.</Trans>
            )}
          </EmptyState>
        </Panel>
      ) : roles.isSuccess ? (
        <Panel flush>
          <RolesTable
            list={list}
            rules={ruleList}
            canEdit={canEdit}
            mark={mark}
            onOpen={open}
            onDelete={setDeleting}
          />
        </Panel>
      ) : null}

      {drawer !== null ? (
        <RoleDrawer
          key={drawer.key}
          role={drawer.role}
          rules={ruleList}
          canEdit={canEdit}
          onClose={close}
        />
      ) : null}

      {deleting !== null ? (
        <DeleteRoleDialog
          role={deleting}
          rules={ruleList}
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
