// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import { removeByIds } from "@/lib/listCache";
import { useNow } from "@/lib/useNow";
import { DeleteDialog } from "@/library/DeleteDialog";
import { useDeletion } from "@/library/useDeletion";
import { liveListOptions } from "@/live/freshness";
import { useLiveMarks } from "@/live/useLiveMarks";
import { useLiveStatus } from "@/live/useLiveStatus";
import { Drawer } from "@/ui/Drawer";
import { EmptyState } from "@/ui/EmptyState";
import { ListSkeleton } from "@/ui/ListSkeleton";
import { Notice } from "@/ui/Notice";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";
import { Panel } from "@/ui/Panel";
import { SearchField } from "@/ui/SearchField";
import { useListSearch } from "@/ui/useListSearch";

import { ImageFacts } from "./ImageFacts";
import { deleteImage, imagesQuery, type ImageSummary } from "./images";
import { ImagesTable } from "./ImagesTable";
import { ImageUploadPanel } from "./ImageUploadPanel";
import { deleteConsequence, matchesImage } from "./imageView";
import { NoImages } from "./NoImages";

// The operating system images that machines are deployed with. These are the Windows images in WIM and ESD files,
// and whole disk images such as Linux cloud images. A new image joins the list, and a changed one flashes.
export function ImagesPage() {
  const { t: translate } = useLingui();
  const queryClient = useQueryClient();
  const live = useLiveStatus();
  const images = useQuery({ ...imagesQuery, ...liveListOptions(live) });
  const now = useNow(30_000);
  const canEdit = useIsAdministrator();
  const mark = useLiveMarks({
    queryKey: imagesQuery.queryKey,
    items: (list) => list,
    id: (image) => image.id,
    signature: (image) => [image.name, image.bootCapability].join("|"),
    tone: () => "idle",
  });
  const [shownId, setShownId] = useState<string | null>(null);
  const deletion = useDeletion<ImageSummary>(deleteImage, (id) => {
    queryClient.setQueryData(imagesQuery.queryKey, (list) => removeByIds(list, [id]));
  });

  const list = images.data ?? [];
  const { query, setQuery, shown } = useListSearch(list, matchesImage);
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

      {canEdit ? <ImageUploadPanel /> : null}

      {images.isError ? (
        <Notice tone="fail">
          <Trans>The image list could not be loaded.</Trans>
        </Notice>
      ) : null}

      <Panel flush>
        {images.isPending ? (
          <ListSkeleton />
        ) : list.length === 0 ? (
          <NoImages canEdit={canEdit} />
        ) : shown.length === 0 ? (
          <EmptyState title={<Trans>No image matches</Trans>} />
        ) : (
          <ImagesTable
            images={shown}
            now={now}
            canEdit={canEdit}
            mark={mark}
            onDetails={setShownId}
            onDelete={deletion.ask}
          />
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

      <DeleteDialog
        deletion={deletion}
        confirmLabel={<Trans>Delete image</Trans>}
        consequence={deleteConsequence}
      />
    </Page>
  );
}
