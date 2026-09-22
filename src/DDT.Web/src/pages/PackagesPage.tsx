// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";

import { currentUserQuery } from "@/auth/auth";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { useNow } from "@/lib/useNow";
import { PackageRow } from "@/packages/PackageRow";
import { PackageUpload } from "@/packages/PackageUpload";
import {
  deletePackage,
  deletionConsequence,
  packagesQuery,
  type PackageSummary,
} from "@/packages/packages";
import { usePackageLibrary } from "@/packages/usePackageLibrary";

import styles from "./PackagesPage.module.scss";

export function PackagesPage() {
  const queryClient = useQueryClient();
  const library = usePackageLibrary();
  const user = useQuery(currentUserQuery).data ?? null;
  const now = useNow(30_000);
  const modelsListId = useId();
  const manufacturersListId = useId();

  const [deleteTarget, setDeleteTarget] = useState<PackageSummary | null>(null);

  const isAdministrator = user?.roles.includes("Administrator") === true;

  const remove = useMutation({
    mutationFn: (id: string) => deletePackage(id),
    onSuccess: () => {
      setDeleteTarget(null);
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: packagesQuery.queryKey });
    },
  });

  const { packages, models } = library;
  const list = packages.data ?? [];
  const manufacturers = [
    ...new Set(
      models.flatMap((model) => (model.manufacturer === null ? [] : [model.manufacturer])),
    ),
  ];

  return (
    <div className={styles.page}>
      <h1>Packages</h1>
      <p className={styles.intro}>
        A driver package holds the driver folder of hardware models; the Inject drivers step adds it
        to the machines whose model one of its targets names. A files package holds files that a Run
        script step unpacks before its script runs. Both run as SYSTEM on the machines, so only
        administrators change them.
      </p>

      {isAdministrator && <PackageUpload />}

      {packages.isError && <p className={styles.error}>The package list could not be loaded.</p>}

      {packages.isSuccess && list.length === 0 && (
        <section className={styles.empty}>
          <h2 className={styles.emptyTitle}>No packages yet</h2>
          <p>
            {isAdministrator
              ? "Upload a zip holding the driver folder of one hardware model, then set the manufacturer and model it is for; the Inject drivers step gives it to machines of that model. A files package is a zip a Run script step unpacks before its script runs."
              : "An administrator uploads driver and files packages here."}
          </p>
        </section>
      )}

      {list.length > 0 && (
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">Name</th>
              <th scope="col">Kind</th>
              <th scope="col">Contents</th>
              <th scope="col">Targets</th>
              <th scope="col">Matching machines</th>
              <th scope="col">Used by</th>
              <th scope="col">Uploaded</th>
              {isAdministrator && <th scope="col">Actions</th>}
            </tr>
          </thead>
          <tbody>
            {list.map((item) => (
              <PackageRow
                key={item.id}
                item={item}
                users={library.usersOf(item)}
                matches={library.matchesOf(item)}
                canEdit={isAdministrator}
                modelsListId={modelsListId}
                manufacturersListId={manufacturersListId}
                now={now}
                onDelete={() => {
                  remove.reset();
                  setDeleteTarget(item);
                }}
              />
            ))}
          </tbody>
        </table>
      )}

      <datalist id={modelsListId}>
        {models.map((model) => (
          <option key={`${model.manufacturer ?? ""}/${model.model}`} value={model.model} />
        ))}
      </datalist>
      <datalist id={manufacturersListId}>
        {manufacturers.map((manufacturer) => (
          <option key={manufacturer} value={manufacturer} />
        ))}
      </datalist>

      {deleteTarget !== null && (
        <ConfirmDialog
          open
          onOpenChange={(open) => {
            if (!open) {
              setDeleteTarget(null);
            }
          }}
          title={`Delete ${deleteTarget.name}?`}
          consequence={deletionConsequence(deleteTarget, library.usersOf(deleteTarget))}
          confirmLabel="Delete package"
          busy={remove.isPending}
          error={remove.isError ? remove.error.message : null}
          onConfirm={() => {
            remove.mutate(deleteTarget.id);
          }}
        />
      )}
    </div>
  );
}
