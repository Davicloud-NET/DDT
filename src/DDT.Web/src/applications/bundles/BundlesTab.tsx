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

import { bundleDeletionConsequence } from "../applicationView";
import { BundleDrawer } from "./BundleDrawer";
import { BundlesTable } from "./BundlesTable";
import { useBundlesTab } from "./useBundlesTab";

export function BundlesTab() {
  const { t } = useLingui();
  const tab = useBundlesTab();
  const { bundles, all, search, canEdit, drawer, deletion } = tab;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-3">
        {all.length > 0 ? (
          <SearchField
            label={t`Find a bundle`}
            placeholder={t`Name or application`}
            value={search.query}
            onChange={search.setQuery}
          />
        ) : null}
        <div className="flex-1" />
        {canEdit ? (
          <Button
            variant="primary"
            onPress={() => {
              tab.open(null);
            }}
          >
            <IconPlus aria-hidden="true" size={14} stroke={2} />
            <Trans>Add bundle</Trans>
          </Button>
        ) : null}
      </div>

      {bundles.isError ? (
        <Notice tone="fail">
          <Trans>The bundles could not be loaded.</Trans>
        </Notice>
      ) : null}

      <Panel flush>
        {bundles.isPending ? (
          <ListSkeleton />
        ) : all.length === 0 ? (
          <EmptyState title={<Trans>No bundles yet</Trans>} />
        ) : search.shown.length === 0 ? (
          <EmptyState title={<Trans>No bundle matches</Trans>} />
        ) : (
          <BundlesTable
            list={search.shown}
            canEdit={canEdit}
            mark={tab.mark}
            nameOf={tab.nameOf}
            onOpen={tab.open}
            onDelete={deletion.ask}
          />
        )}
      </Panel>

      {drawer !== null ? (
        <BundleDrawer
          key={drawer.key}
          bundle={drawer.bundle}
          library={tab.library}
          onClose={tab.close}
          onDelete={deletion.ask}
        />
      ) : null}

      <DeleteDialog
        deletion={deletion}
        confirmLabel={<Trans>Delete bundle</Trans>}
        consequence={bundleDeletionConsequence}
      />
    </div>
  );
}
