// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { formatBytes } from "@/lib/format";
import { Uploaded } from "@/library/Uploaded";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";

import { ImageMenu } from "./ImageMenu";
import { ImageName } from "./ImageName";
import type { ImageSummary } from "./images";
import { ImageSecureBoot } from "./ImageSecureBoot";
import { installedLine, kindLine } from "./imageView";

interface ImagesTableProps {
  images: ImageSummary[];
  now: number;
  canEdit: boolean;
  mark: (id: string) => string;
  // Both must stay the same across renders, because the rows keep them.
  onDetails: (id: string) => void;
  onDelete: (image: ImageSummary) => void;
}

export function ImagesTable({ images, now, canEdit, mark, onDetails, onDelete }: ImagesTableProps) {
  const { t: translate } = useLingui();

  return (
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
      <TableBody items={images} dependencies={[now, canEdit, mark]}>
        {(image) => (
          <TableRow id={image.id} textValue={image.name} className={mark(image.id)}>
            <TableCell className="pl-4">
              <ImageName image={image} onDetails={onDetails} />
            </TableCell>
            <TableCell className="type-small text-ink-2">{kindLine(image)}</TableCell>
            <TableCell className="type-small">
              <span className="flex flex-col">
                <span>{formatBytes(image.sizeBytes)}</span>
                <span className="text-muted">{installedLine(image)}</span>
              </span>
            </TableCell>
            <TableCell>
              <ImageSecureBoot image={image} />
            </TableCell>
            <TableCell className="type-small text-muted">
              <Uploaded entry={image} now={now} />
            </TableCell>
            <TableCell className="pr-4">
              <ImageMenu
                image={image}
                canDelete={canEdit}
                onDetails={() => {
                  onDetails(image.id);
                }}
                onDelete={() => {
                  onDelete(image);
                }}
              />
            </TableCell>
          </TableRow>
        )}
      </TableBody>
    </Table>
  );
}
