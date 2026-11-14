// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQueryClient } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import type { ImportFile } from "@/images/import/imports";
import { removeByIds } from "@/lib/listCache";
import { useNow } from "@/lib/useNow";
import { DeleteDialog } from "@/library/DeleteDialog";
import { useDeletion } from "@/library/useDeletion";
import { useLiveMarks } from "@/live/useLiveMarks";
import { EmptyState } from "@/ui/EmptyState";
import { ListSkeleton } from "@/ui/ListSkeleton";
import { Notice } from "@/ui/Notice";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";
import { Panel } from "@/ui/Panel";
import { SearchField } from "@/ui/SearchField";
import { useListSearch } from "@/ui/useListSearch";

import { PackageDialog } from "./PackageDialog";
import { packageKindTexts } from "./packageKindTexts";
import {
  deletePackage,
  deletionConsequence,
  packagesQuery,
  type PackageKind,
  type PackageSummary,
} from "./packages";
import { PackagesTable } from "./PackagesTable";
import { PackageUploadPanel } from "./PackageUploadPanel";
import { matchesPackage } from "./packageView";
import { usePackageLibrary } from "./usePackageLibrary";

interface PackagesPageProps {
  kind: PackageKind;
  // Called when an upload was a Languages and Optional Features ISO, with where the server put it.
  onLanguageIso?: (file: ImportFile) => void;
  // Shown between the upload and the list, to administrators.
  children?: ReactNode;
}

// The library's packages of one kind. An Inject drivers step gives drivers to the machines whose model they name.
// Files are unpacked for a Run script step that names them. Windows packages hold updates and languages. The list is
// live.
export function PackagesPage({ kind, onLanguageIso, children }: PackagesPageProps) {
  const texts = packageKindTexts(kind);
  const queryClient = useQueryClient();
  const library = usePackageLibrary();
  const now = useNow(30_000);
  const canEdit = useIsAdministrator();
  const [editing, setEditing] = useState<PackageSummary | null>(null);
  const deletion = useDeletion<PackageSummary>(deletePackage, (id) => {
    queryClient.setQueryData(packagesQuery.queryKey, (list) => removeByIds(list, [id]));
  });
  // A new package animates in, and a changed one flashes, such as after a save in its dialog.
  const mark = useLiveMarks({
    queryKey: packagesQuery.queryKey,
    items: (list) => list.filter((item) => item.kind === kind),
    id: (item) => item.id,
    signature: (item) =>
      [item.name, item.description, item.bootImage, item.targets.length, item.windows?.builds].join(
        "|",
      ),
    tone: () => "idle",
  });

  const all = (library.packages.data ?? []).filter((item) => item.kind === kind);
  const { query, setQuery, shown } = useListSearch(all, matchesPackage);

  return (
    <Page>
      <PageHeader title={texts.title}>
        <div className="flex-1" />
        {all.length > 0 ? (
          <SearchField
            label={texts.find}
            placeholder={texts.placeholder}
            value={query}
            onChange={setQuery}
          />
        ) : null}
      </PageHeader>

      {canEdit ? (
        <PackageUploadPanel kind={kind} onEdit={setEditing} onLanguageIso={onLanguageIso} />
      ) : null}

      {canEdit ? children : null}

      {library.packages.isError ? (
        <Notice tone="fail">
          <Trans>The packages could not be loaded.</Trans>
        </Notice>
      ) : null}

      <Panel flush>
        {library.packages.isPending ? (
          <ListSkeleton />
        ) : all.length === 0 ? (
          <EmptyState title={texts.empty} />
        ) : shown.length === 0 ? (
          <EmptyState title={<Trans>No package matches</Trans>} />
        ) : (
          <PackagesTable
            items={shown}
            kind={kind}
            library={library}
            now={now}
            canEdit={canEdit}
            mark={mark}
            onEdit={setEditing}
            onDelete={deletion.ask}
          />
        )}
      </Panel>

      {editing !== null ? (
        <PackageDialog
          item={editing}
          models={library.models}
          onClose={() => {
            setEditing(null);
          }}
        />
      ) : null}

      <DeleteDialog
        deletion={deletion}
        confirmLabel={<Trans>Delete package</Trans>}
        consequence={(item) => deletionConsequence(item, library.usersOf(item))}
      />
    </Page>
  );
}
