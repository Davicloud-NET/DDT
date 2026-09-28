// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { MenuItem } from "@/ui/Menu";
import { RowActionsMenu } from "@/ui/RowActionsMenu";

import type { ImageSummary } from "./images";

export function ImageMenu({
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
    <RowActionsMenu
      label={translate`Actions for ${name}`}
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
    </RowActionsMenu>
  );
}
