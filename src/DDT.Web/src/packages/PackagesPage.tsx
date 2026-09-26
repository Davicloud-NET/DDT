// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconDots } from "@tabler/icons-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { useState } from "react";
import { Button as AriaButton, MenuTrigger } from "react-aria-components";

import { currentUserQuery } from "@/auth/auth";
import { formattingLocale } from "@/i18n/i18n";
import { formatBytes } from "@/lib/format";
import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { SearchField } from "@/ui/Controls";
import { ConfirmDialog } from "@/ui/Dialog";
import { EmptyState, Page, PageHeader, Panel, Skeleton } from "@/ui/Layout";
import { Menu, MenuItem } from "@/ui/Menu";
import { Notice } from "@/ui/Notice";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";
import type { UploadOutcome } from "@/uploads/resumableUpload";
import { UploadPanel } from "@/uploads/UploadPanel";

import { PackageDialog } from "./PackageDialog";
import {
  deletePackage,
  deletionConsequence,
  describeTarget,
  packagesQuery,
  type PackageKind,
  type PackageSummary,
} from "./packages";
import { usePackageLibrary } from "./usePackageLibrary";

// The driver packages or the file packages of the library. Drivers go to the machines whose model they name, through
// an Inject drivers step; files are unpacked for a Run script step that names them. The list is live.
export function PackagesPage({ kind }: { kind: PackageKind }) {
  const { t: translate } = useLingui();
  const queryClient = useQueryClient();
  const library = usePackageLibrary();
  const user = useQuery(currentUserQuery).data ?? null;
  const now = useNow(30_000);
  const canEdit = user?.roles.includes("Administrator") === true;
  const [query, setQuery] = useState("");
  const [editing, setEditing] = useState<PackageSummary | null>(null);
  const [deleting, setDeleting] = useState<PackageSummary | null>(null);
  const drivers = kind === "Drivers";

  const remove = useMutation({
    mutationFn: (id: string) => deletePackage(id),
    onSuccess: (_, id) => {
      queryClient.setQueryData(packagesQuery.queryKey, (list) =>
        list?.filter((item) => item.id !== id),
      );
      setDeleting(null);
    },
  });

  const all = (library.packages.data ?? []).filter((item) => item.kind === kind);
  const needle = query.trim().toLowerCase();
  const shown = needle === "" ? all : all.filter((item) => matches(item, needle));

  return (
    <Page>
      <PageHeader title={drivers ? <Trans>Drivers</Trans> : <Trans>Files</Trans>}>
        <div className="flex-1" />
        {all.length > 0 ? (
          <SearchField
            label={drivers ? translate`Find a driver package` : translate`Find a file package`}
            placeholder={drivers ? translate`Name, model or file` : translate`Name or file`}
            value={query}
            onChange={setQuery}
          />
        ) : null}
      </PageHeader>

      {canEdit ? (
        <UploadPanel<PackageSummary>
          kind={kind}
          kinds={[kind]}
          what={<Trans>a zip file</Trans>}
          accept={[".zip"]}
          hint={
            drivers ? (
              <Trans>
                A zip of driver folders with their .inf files. Choose the hardware models it is for
                once it is added.
              </Trans>
            ) : (
              <Trans>
                A zip of the files a Run script step needs. The step finds them unpacked in its
                working folder.
              </Trans>
            )
          }
          verifyingHint={<Trans>The server checks the zip and counts what is in it.</Trans>}
          leaveWhileVerifying={(file) =>
            t`The server goes on checking ${file} after you leave and adds it to the library when it finishes. If it refuses the file, you do not see why.`
          }
          describeResult={describeResult}
          onAdded={(added) => {
            queryClient.setQueryData(packagesQuery.queryKey, (list) =>
              list === undefined ? list : [...list.filter((item) => item.id !== added.id), added],
            );

            // A new driver package is for no model yet, so it opens at once to choose them.
            if (added.kind === "Drivers" && added.targets.length === 0) {
              setEditing(added);
            }
          }}
        />
      ) : null}

      {library.packages.isError ? (
        <Notice tone="fail">
          <Trans>The packages could not be loaded.</Trans>
        </Notice>
      ) : null}

      <Panel flush>
        {library.packages.isPending ? (
          <div className="flex flex-col gap-3 p-4">
            <Skeleton className="h-6 w-1/2" />
            <Skeleton className="h-6 w-2/3" />
          </div>
        ) : all.length === 0 ? (
          <EmptyState
            title={
              drivers ? <Trans>No driver packages yet</Trans> : <Trans>No file packages yet</Trans>
            }
          >
            {drivers ? (
              <Trans>
                Upload a zip of drivers and choose the models it is for. A task sequence with an
                Inject drivers step then adds them to those machines.
              </Trans>
            ) : (
              <Trans>
                Upload a zip of files, then choose it in a Run script step of a task sequence.
              </Trans>
            )}
          </EmptyState>
        ) : shown.length === 0 ? (
          <EmptyState title={<Trans>No package matches</Trans>} />
        ) : (
          <Table
            aria-label={drivers ? translate`Driver packages` : translate`File packages`}
            className="min-w-[860px] table-fixed"
          >
            <TableHeader>
              <TableColumn id="name" isRowHeader className="w-[32%] pl-4">
                <Trans>Package</Trans>
              </TableColumn>
              <TableColumn id="use">
                {drivers ? <Trans>For</Trans> : <Trans>Used by</Trans>}
              </TableColumn>
              <TableColumn id="size" className="w-36">
                <Trans>Size</Trans>
              </TableColumn>
              <TableColumn id="uploaded" className="w-44">
                <Trans>Uploaded</Trans>
              </TableColumn>
              <TableColumn id="actions" className="w-14 pr-4">
                <span className="sr-only">
                  <Trans>Actions</Trans>
                </span>
              </TableColumn>
            </TableHeader>
            <TableBody items={shown} dependencies={[now, canEdit, library.models, library.usersOf]}>
              {(item) => (
                <TableRow id={item.id} textValue={item.name}>
                  <TableCell className="pl-4">
                    <span className="flex min-w-0 flex-col">
                      <span className="truncate type-label text-ink">{item.name}</span>
                      <span className="truncate type-small text-muted">
                        {item.description ?? item.originalFileName ?? ""}
                      </span>
                    </span>
                  </TableCell>
                  <TableCell className="type-small">
                    {drivers ? (
                      <DriverTargets item={item} matches={library.matchesOf(item)} />
                    ) : (
                      <FileUsers users={library.usersOf(item)} />
                    )}
                  </TableCell>
                  <TableCell className="type-small">
                    <span className="flex flex-col">
                      <span>{formatBytes(item.sizeBytes)}</span>
                      <span className="text-muted">{contents(item)}</span>
                    </span>
                  </TableCell>
                  <TableCell className="type-small text-muted">
                    <Uploaded item={item} now={now} />
                  </TableCell>
                  <TableCell className="pr-4">
                    {canEdit ? (
                      <PackageMenu
                        item={item}
                        onEdit={() => {
                          setEditing(item);
                        }}
                        onDelete={() => {
                          remove.reset();
                          setDeleting(item);
                        }}
                      />
                    ) : null}
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
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

      <ConfirmDialog
        isOpen={deleting !== null}
        onOpenChange={(open) => {
          if (!open) {
            setDeleting(null);
          }
        }}
        title={deleting === null ? "" : <DeleteTitle name={deleting.name} />}
        confirmLabel={<Trans>Delete package</Trans>}
        danger
        isBusy={remove.isPending}
        error={remove.isError ? remove.error.message : undefined}
        onConfirm={() => {
          if (deleting !== null) {
            remove.mutate(deleting.id);
          }
        }}
      >
        <p>{deleting === null ? null : deletionConsequence(deleting, library.usersOf(deleting))}</p>
      </ConfirmDialog>
    </Page>
  );
}

export function DriversPage() {
  return <PackagesPage kind="Drivers" />;
}

export function FilesPage() {
  return <PackagesPage kind="Files" />;
}

function DeleteTitle({ name }: { name: string }) {
  return <Trans>Delete {name}?</Trans>;
}

function describeResult(file: string, outcome: UploadOutcome, item: PackageSummary): string {
  const name = item.name;

  return outcome === "duplicate"
    ? t`${file} is already in the library as ${name}.`
    : t`Added ${name} from ${file}.`;
}

function contents(item: PackageSummary): string {
  const unpacked = formatBytes(item.expandedBytes);

  const files = item.fileCount;

  return plural(files, {
    one: `# file, ${unpacked} unpacked`,
    other: `# files, ${unpacked} unpacked`,
  });
}

function matches(item: PackageSummary, needle: string): boolean {
  return [
    item.name,
    item.description,
    item.originalFileName,
    item.uploadedBy,
    ...item.targets.map(describeTarget),
  ].some((value) => value?.toLowerCase().includes(needle) === true);
}

function DriverTargets({ item, matches: count }: { item: PackageSummary; matches: number }) {
  if (item.targets.length === 0) {
    return (
      <span className="text-attention-text">
        <Trans>No model yet: no machine gets these drivers.</Trans>
      </span>
    );
  }

  const targets = item.targets.map(describeTarget).join(", ");

  return (
    <span className="flex min-w-0 flex-col">
      <span className="truncate text-ink">{targets}</span>
      <span className="text-muted">
        {count === 0
          ? t`No registered machine matches yet.`
          : plural(count, {
              one: "Matches # registered machine.",
              other: "Matches # registered machines.",
            })}
      </span>
    </span>
  );
}

function FileUsers({ users }: { users: readonly { id: string; name: string }[] | null }) {
  if (users === null) {
    return <span className="text-muted">…</span>;
  }

  if (users.length === 0) {
    return (
      <span className="text-muted">
        <Trans>No sequence names it</Trans>
      </span>
    );
  }

  return (
    <span className="flex min-w-0 flex-wrap gap-x-2">
      {users.map((sequence) => (
        <Link
          key={sequence.id}
          to="/deployment/sequences/$sequenceId"
          params={{ sequenceId: sequence.id }}
          className="truncate text-ink hover:underline"
        >
          {sequence.name}
        </Link>
      ))}
    </span>
  );
}

function Uploaded({ item, now }: { item: PackageSummary; now: number }) {
  const when = relativeTime(item.uploadedUtc, now);
  const by = item.uploadedBy;

  return (
    <span title={new Date(item.uploadedUtc).toLocaleString(formattingLocale())}>
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

function PackageMenu({
  item,
  onEdit,
  onDelete,
}: {
  item: PackageSummary;
  onEdit: () => void;
  onDelete: () => void;
}) {
  const { t: translate } = useLingui();
  const name = item.name;

  return (
    <MenuTrigger>
      <AriaButton
        aria-label={translate`Actions for ${name}`}
        className="flex size-7.5 cursor-pointer items-center justify-center rounded-key text-muted outline-none hover:bg-hover hover:text-ink focus-visible:outline-2 focus-visible:outline-focus"
      >
        <IconDots size={18} stroke={2} />
      </AriaButton>
      <Menu
        aria-label={translate`Actions for ${name}`}
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
