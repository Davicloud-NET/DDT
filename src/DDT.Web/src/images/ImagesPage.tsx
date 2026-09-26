// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconDots } from "@tabler/icons-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button as AriaButton, MenuTrigger } from "react-aria-components";

import { currentUserQuery } from "@/auth/auth";
import { formattingLocale } from "@/i18n/i18n";
import { formatBytes } from "@/lib/format";
import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { liveListOptions } from "@/live/freshness";
import { useLiveStatus } from "@/live/useLiveStatus";
import { SearchField } from "@/ui/Controls";
import { ConfirmDialog } from "@/ui/Dialog";
import { Drawer } from "@/ui/Drawer";
import { EmptyState, Facts, Page, PageHeader, Panel, Skeleton } from "@/ui/Layout";
import { Menu, MenuItem } from "@/ui/Menu";
import { Notice } from "@/ui/Notice";
import { StateTag, type StateTone } from "@/ui/StateTag";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";
import type { UploadOutcome } from "@/uploads/resumableUpload";
import { UploadPanel } from "@/uploads/UploadPanel";

import {
  bootCapabilityLabel,
  deleteImage,
  imagesQuery,
  isDeployable,
  kindLabel,
  secureBootWarning,
  type ImageSummary,
} from "./images";

const imageKinds = ["Image"] as const;

const bootTone: Record<NonNullable<ImageSummary["bootCapability"]>, StateTone> = {
  SecureBootOk: "ok",
  NotSigned: "attention",
  Unknown: "idle",
};

// The operating system images machines are deployed with: the Windows images of uploaded WIM and ESD files, and
// whole disk images such as Linux cloud images. The list is live.
export function ImagesPage() {
  const { t: translate } = useLingui();
  const queryClient = useQueryClient();
  const live = useLiveStatus();
  const images = useQuery({ ...imagesQuery, ...liveListOptions(live) });
  const user = useQuery(currentUserQuery).data ?? null;
  const now = useNow(30_000);
  const canEdit = user?.roles.includes("Administrator") === true;
  const [query, setQuery] = useState("");
  const [shownId, setShownId] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<ImageSummary | null>(null);

  const remove = useMutation({
    mutationFn: (id: string) => deleteImage(id),
    onSuccess: (_, id) => {
      queryClient.setQueryData(imagesQuery.queryKey, (list) =>
        list?.filter((image) => image.id !== id),
      );
      setDeleting(null);
    },
  });

  const list = images.data ?? [];
  const needle = query.trim().toLowerCase();
  const shown = needle === "" ? list : list.filter((image) => matches(image, needle));
  const details = list.find((image) => image.id === shownId) ?? null;

  return (
    <Page>
      <PageHeader title={<Trans>OS images</Trans>}>
        <div className="flex-1" />
        {list.length > 0 ? (
          <SearchField
            label={translate`Find an image`}
            placeholder={translate`Name, edition, version, language or file`}
            value={query}
            onChange={setQuery}
          />
        ) : null}
      </PageHeader>

      {canEdit ? (
        <UploadPanel<ImageSummary[]>
          kind="Image"
          kinds={imageKinds}
          what={<Trans>a WIM, ESD or disk image file</Trans>}
          accept={[".wim", ".esd", ".img", ".raw", ".gz", ".xz", ".zst", ".qcow2"]}
          hint={
            <Trans>
              Each x64 Windows image in a WIM or ESD file becomes an entry. A disk image, such as a
              Linux cloud image, becomes one: raw, compressed with gzip, zstd or xz, or qcow2.
              Convert VHDX, VMDK or VDI to raw with qemu-img first.
            </Trans>
          }
          verifyingHint={
            <Trans>
              The server checks the file, reads the images in it and compresses a disk image. This
              takes a few minutes for a large file.
            </Trans>
          }
          leaveWhileVerifying={(file) =>
            t`The server goes on checking ${file} after you leave and adds its images to the library when it finishes. If it refuses the file, you do not see why.`
          }
          describeResult={describeResult}
          onAdded={(added) => {
            queryClient.setQueryData(imagesQuery.queryKey, (existing) =>
              existing === undefined
                ? existing
                : [...existing.filter((image) => !added.some((a) => a.id === image.id)), ...added],
            );
          }}
        />
      ) : null}

      {images.isError ? (
        <Notice tone="fail">
          <Trans>The image list could not be loaded.</Trans>
        </Notice>
      ) : null}

      <Panel flush>
        {images.isPending ? (
          <div className="flex flex-col gap-3 p-4">
            <Skeleton className="h-6 w-1/2" />
            <Skeleton className="h-6 w-2/3" />
          </div>
        ) : list.length === 0 ? (
          <EmptyState title={<Trans>No images yet</Trans>}>
            {canEdit ? (
              <Trans>
                Upload a WIM file to add its Windows images, or a disk image such as a Linux cloud
                image. A task sequence then applies or writes one.
              </Trans>
            ) : (
              <Trans>
                An administrator adds images by uploading WIM files or disk images here.
              </Trans>
            )}
          </EmptyState>
        ) : shown.length === 0 ? (
          <EmptyState title={<Trans>No image matches</Trans>} />
        ) : (
          <Table aria-label={translate`OS images`} className="min-w-[900px] table-fixed">
            <TableHeader>
              <TableColumn id="name" isRowHeader className="w-[34%] pl-4">
                <Trans>Image</Trans>
              </TableColumn>
              <TableColumn id="kind">
                <Trans>Kind</Trans>
              </TableColumn>
              <TableColumn id="size" className="w-36">
                <Trans>Size</Trans>
              </TableColumn>
              <TableColumn id="boot" className="w-36">
                <Trans>Secure Boot</Trans>
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
            <TableBody items={shown} dependencies={[now, canEdit]}>
              {(image) => (
                <TableRow id={image.id} textValue={image.name}>
                  <TableCell className="pl-4">
                    <span className="flex min-w-0 flex-col">
                      <AriaButton
                        onPress={() => {
                          setShownId(image.id);
                        }}
                        className="w-fit max-w-full cursor-pointer truncate text-left type-label text-ink outline-none hover:underline focus-visible:outline-2 focus-visible:outline-focus"
                      >
                        {image.name}
                      </AriaButton>
                      <span className="truncate type-small text-muted">{source(image)}</span>
                      {!isDeployable(image) ? (
                        <span className="type-small text-fail-text">
                          <Trans>Not deployable: only x64 images can be installed.</Trans>
                        </span>
                      ) : null}
                    </span>
                  </TableCell>
                  <TableCell className="type-small text-ink-2">{kindLine(image)}</TableCell>
                  <TableCell className="type-small">
                    <span className="flex flex-col">
                      <span>{formatBytes(image.sizeBytes)}</span>
                      <span className="text-muted">{installedLine(image)}</span>
                    </span>
                  </TableCell>
                  <TableCell>
                    {image.kind === "RawDisk" && image.bootCapability !== null ? (
                      <StateTag tone={bootTone[image.bootCapability]}>
                        {bootCapabilityLabel(image)}
                      </StateTag>
                    ) : (
                      <span className="type-small text-muted">
                        <Trans>Microsoft's boot files</Trans>
                      </span>
                    )}
                  </TableCell>
                  <TableCell className="type-small text-muted">
                    <Uploaded image={image} now={now} />
                  </TableCell>
                  <TableCell className="pr-4">
                    <ImageMenu
                      image={image}
                      canDelete={canEdit}
                      onDetails={() => {
                        setShownId(image.id);
                      }}
                      onDelete={() => {
                        remove.reset();
                        setDeleting(image);
                      }}
                    />
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        )}
      </Panel>

      <Drawer
        isOpen={details !== null}
        onOpenChange={(open) => {
          if (!open) {
            setShownId(null);
          }
        }}
        title={details?.name ?? ""}
      >
        {details !== null ? <ImageFacts image={details} /> : null}
      </Drawer>

      <ConfirmDialog
        isOpen={deleting !== null}
        onOpenChange={(open) => {
          if (!open) {
            setDeleting(null);
          }
        }}
        title={deleting === null ? "" : <DeleteTitle name={deleting.name} />}
        confirmLabel={<Trans>Delete image</Trans>}
        danger
        isBusy={remove.isPending}
        error={remove.isError ? remove.error.message : undefined}
        onConfirm={() => {
          if (deleting !== null) {
            remove.mutate(deleting.id);
          }
        }}
      >
        <p>{deleting === null ? null : deleteConsequence(deleting)}</p>
      </ConfirmDialog>
    </Page>
  );
}

function DeleteTitle({ name }: { name: string }) {
  return <Trans>Delete {name}?</Trans>;
}

function describeResult(file: string, outcome: UploadOutcome, images: ImageSummary[]): string {
  const count = images.length;

  switch (outcome) {
    case "added":
      return plural(count, {
        one: `Added # image from ${file}.`,
        other: `Added # images from ${file}.`,
      });
    case "duplicate":
      return t`Every image in ${file} is already in the library.`;
    case "unclear":
      return plural(count, {
        one: `The library now holds # image from ${file}.`,
        other: `The library now holds # images from ${file}.`,
      });
  }
}

// A raw disk image is the whole file; a WIM holds several images by index.
function source(image: ImageSummary): string {
  const file = image.originalFileName ?? t`Unknown file`;
  const index = image.wimIndex;

  return image.kind === "RawDisk" ? file : t`${file}, index ${index}`;
}

function kindLine(image: ImageSummary): string {
  return [kindLabel(image.kind), image.architecture, image.version, image.language]
    .filter((part): part is string => part !== null && part !== "")
    .join(", ");
}

function installedLine(image: ImageSummary): string {
  const installed = formatBytes(image.installedBytes);

  return image.kind === "RawDisk" ? t`${installed} disk` : t`${installed} installed`;
}

function deleteConsequence(image: ImageSummary): string {
  const name = image.name;
  const size = formatBytes(image.sizeBytes);

  return image.kind === "RawDisk"
    ? t`${name} (${size}) is removed from the library and can no longer be used by a task sequence. Its compressed disk is deleted from the server.`
    : t`${name} (${size}) is removed from the library and can no longer be used by a task sequence. The WIM file is deleted from the server once no other image in the library comes from it.`;
}

function matches(image: ImageSummary, needle: string): boolean {
  return [
    image.name,
    kindLabel(image.kind),
    image.edition,
    image.architecture,
    image.version,
    image.language,
    image.originalFileName,
    image.uploadedBy,
    image.sha256,
  ].some((value) => value?.toLowerCase().includes(needle) === true);
}

function Uploaded({ image, now }: { image: ImageSummary; now: number }) {
  const when = relativeTime(image.uploadedUtc, now);
  const by = image.uploadedBy;

  return (
    <span title={new Date(image.uploadedUtc).toLocaleString(formattingLocale())}>
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

function ImageFacts({ image }: { image: ImageSummary }) {
  const warning = secureBootWarning(image);
  const unknown = t`Not stated`;

  return (
    <>
      <Facts
        items={[
          { label: <Trans>Kind</Trans>, value: kindLabel(image.kind) },
          ...(image.edition === null
            ? []
            : [{ label: <Trans>Edition</Trans>, value: image.edition }]),
          { label: <Trans>Architecture</Trans>, value: image.architecture ?? unknown },
          { label: <Trans>Version</Trans>, value: image.version ?? unknown },
          { label: <Trans>Language</Trans>, value: image.language ?? unknown },
          { label: <Trans>File</Trans>, value: source(image) },
          { label: <Trans>Size</Trans>, value: formatBytes(image.sizeBytes) },
          { label: <Trans>Installed</Trans>, value: installedLine(image) },
          { label: <Trans>SHA-256</Trans>, value: image.sha256, mono: true },
          ...(image.sourceSha256 === null
            ? []
            : [{ label: <Trans>Disk SHA-256</Trans>, value: image.sourceSha256, mono: true }]),
          ...(image.kind === "RawDisk"
            ? [{ label: <Trans>Secure Boot</Trans>, value: bootCapabilityLabel(image) ?? unknown }]
            : []),
        ]}
      />
      {image.kind === "RawDisk" && image.bootDetail !== null ? (
        <p className="type-small text-ink-2">{image.bootDetail}</p>
      ) : null}
      {warning !== null ? <Notice tone="attention">{warning}</Notice> : null}
      {!isDeployable(image) ? (
        <Notice tone="fail">
          <Trans>Not deployable: only x64 images can be installed.</Trans>
        </Notice>
      ) : null}
    </>
  );
}

function ImageMenu({
  image,
  canDelete,
  onDetails,
  onDelete,
}: {
  image: ImageSummary;
  canDelete: boolean;
  onDetails: () => void;
  onDelete: () => void;
}) {
  const { t: translate } = useLingui();
  const name = image.name;

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
          if (key === "details") {
            onDetails();
          } else if (key === "copy") {
            void navigator.clipboard.writeText(image.sha256);
          } else {
            onDelete();
          }
        }}
      >
        <MenuItem id="details">
          <Trans>Details</Trans>
        </MenuItem>
        <MenuItem id="copy">
          <Trans>Copy SHA-256</Trans>
        </MenuItem>
        {canDelete ? (
          <MenuItem id="delete" className="text-fail-text">
            <Trans>Delete</Trans>
          </MenuItem>
        ) : null}
      </Menu>
    </MenuTrigger>
  );
}
