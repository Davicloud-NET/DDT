// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";

import { currentUserQuery } from "@/auth/auth";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { ImageUpload } from "@/images/ImageUpload";
import { deleteImage, imagesQuery, isDeployable, type ImageSummary } from "@/images/images";
import { formatBytes } from "@/lib/format";
import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";

import styles from "./ImagesPage.module.scss";

function matches(image: ImageSummary, needle: string): boolean {
  return [
    image.name,
    image.edition,
    image.architecture,
    image.version,
    image.language,
    image.originalFileName,
    image.uploadedBy,
    image.sha256,
  ].some((value) => value?.toLowerCase().includes(needle) === true);
}

export function ImagesPage() {
  const queryClient = useQueryClient();
  const images = useQuery(imagesQuery);
  const user = useQuery(currentUserQuery).data ?? null;
  const now = useNow(30_000);
  const filterId = useId();

  const [filter, setFilter] = useState("");
  const [deleteTarget, setDeleteTarget] = useState<ImageSummary | null>(null);

  const isAdministrator = user?.roles.includes("Administrator") === true;

  const remove = useMutation({
    mutationFn: (id: string) => deleteImage(id),
    onSuccess: () => {
      setDeleteTarget(null);
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: imagesQuery.queryKey });
    },
  });

  const list = images.data ?? [];
  const needle = filter.trim().toLowerCase();
  const shown = needle === "" ? list : list.filter((image) => matches(image, needle));

  return (
    <div className={styles.page}>
      <h1>Images</h1>

      {isAdministrator && <ImageUpload />}

      {images.isError && <p className={styles.error}>The image list could not be loaded.</p>}

      {images.isSuccess && list.length === 0 && (
        <section className={styles.empty}>
          <h2 className={styles.emptyTitle}>No images yet</h2>
          <p>
            {isAdministrator
              ? "Upload a WIM file to add its Windows images to the library."
              : "An administrator adds images by uploading WIM files here."}
          </p>
        </section>
      )}

      {list.length > 0 && (
        <>
          <div className={styles.filter}>
            <label htmlFor={filterId}>Filter</label>
            <input
              id={filterId}
              type="search"
              value={filter}
              placeholder="Name, edition, version, language or file"
              onChange={(event) => {
                setFilter(event.target.value);
              }}
            />
          </div>

          {shown.length === 0 ? (
            <p className={styles.secondary}>No image matches the filter.</p>
          ) : (
            <table className={styles.table}>
              <thead>
                <tr>
                  <th scope="col">Name</th>
                  <th scope="col">Edition</th>
                  <th scope="col">Architecture</th>
                  <th scope="col">Version</th>
                  <th scope="col">Language</th>
                  <th scope="col">Size</th>
                  <th scope="col">Installed</th>
                  <th scope="col">Uploaded</th>
                  <th scope="col">SHA-256</th>
                  {isAdministrator && <th scope="col">Actions</th>}
                </tr>
              </thead>
              <tbody>
                {shown.map((image) => (
                  <tr key={image.id}>
                    <td>
                      <div>{image.name}</div>
                      <div className={styles.secondary}>
                        {image.originalFileName ?? "Unknown file"}, index {image.wimIndex}
                      </div>
                    </td>
                    <td>{image.edition}</td>
                    <td>
                      <div>{image.architecture ?? "Unknown"}</div>
                      {!isDeployable(image) && (
                        <div className={styles.notDeployable}>
                          Not deployable: only x64 images can be installed.
                        </div>
                      )}
                    </td>
                    <td>{image.version}</td>
                    <td>{image.language}</td>
                    <td className={styles.number}>{formatBytes(image.sizeBytes)}</td>
                    <td className={styles.number}>{formatBytes(image.installedBytes)}</td>
                    <td title={new Date(image.uploadedUtc).toLocaleString()}>
                      <div>{relativeTime(image.uploadedUtc, now)}</div>
                      {image.uploadedBy !== null && (
                        <div className={styles.secondary}>by {image.uploadedBy}</div>
                      )}
                    </td>
                    <td className={styles.mono} title={image.sha256}>
                      {image.sha256.slice(0, 12)}
                    </td>
                    {isAdministrator && (
                      <td>
                        <button
                          type="button"
                          className={styles.delete}
                          aria-label={`Delete ${image.name}`}
                          onClick={() => {
                            remove.reset();
                            setDeleteTarget(image);
                          }}
                        >
                          Delete
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}

      {deleteTarget !== null && (
        <ConfirmDialog
          open
          onOpenChange={(open) => {
            if (!open) {
              setDeleteTarget(null);
            }
          }}
          title={`Delete ${deleteTarget.name}?`}
          consequence={`${deleteTarget.name} (${formatBytes(deleteTarget.sizeBytes)}) is removed from the library and can no longer be assigned to a machine. The WIM file is deleted from the server once no other image in the library comes from it.`}
          confirmLabel="Delete image"
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
