// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconPlus } from "@tabler/icons-react";

import { DeleteDialog } from "@/library/DeleteDialog";
import { Button } from "@/ui/Button";
import { EmptyState } from "@/ui/EmptyState";
import { ListSkeleton } from "@/ui/ListSkeleton";
import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";
import { SearchField } from "@/ui/SearchField";

import { AddApplicationDialog } from "./add/AddApplicationDialog";
import { ApplicationDrawer } from "./ApplicationDrawer";
import { deletionConsequence } from "./applicationView";
import { ApplicationsTable } from "./list/ApplicationsTable";
import { useApplicationsTab } from "./useApplicationsTab";

export function ApplicationsTab() {
  const { t } = useLingui();
  const tab = useApplicationsTab();
  const { applications, all, search, canEdit, drawer, deletion } = tab;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-3">
        {all.length > 0 ? (
          <SearchField
            label={t`Find an application`}
            placeholder={t`Name, publisher or file`}
            value={search.query}
            onChange={search.setQuery}
          />
        ) : null}
        <div className="flex-1" />
        {canEdit ? (
          <Button variant="primary" onPress={tab.startAdding}>
            <IconPlus aria-hidden="true" size={14} stroke={2} />
            <Trans>Add application</Trans>
          </Button>
        ) : null}
      </div>

      {applications.isError ? (
        <Notice tone="fail">
          <Trans>The applications could not be loaded.</Trans>
        </Notice>
      ) : null}

      <Panel flush>
        {applications.isPending ? (
          <ListSkeleton />
        ) : all.length === 0 ? (
          <EmptyState title={<Trans>No applications yet</Trans>} />
        ) : search.shown.length === 0 ? (
          <EmptyState title={<Trans>No application matches</Trans>} />
        ) : (
          <ApplicationsTable
            list={search.shown}
            canEdit={canEdit}
            mark={tab.mark}
            onOpen={tab.edit}
            onDelete={deletion.ask}
          />
        )}
      </Panel>

      {tab.adding ? (
        <AddApplicationDialog
          onChosen={(seed) => {
            tab.open(null, seed);
          }}
          onClose={tab.stopAdding}
        />
      ) : null}

      {drawer !== null ? (
        <ApplicationDrawer
          key={drawer.key}
          application={drawer.application}
          seed={drawer.seed}
          library={all}
          onClose={tab.close}
          onDelete={deletion.ask}
        />
      ) : null}

      <DeleteDialog
        deletion={deletion}
        confirmLabel={<Trans>Delete application</Trans>}
        consequence={deletionConsequence}
      />
    </div>
  );
}
