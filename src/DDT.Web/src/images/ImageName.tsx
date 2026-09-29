// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { Button as AriaButton } from "react-aria-components";

import { isDeployable, type ImageSummary } from "./images";
import { imageSource } from "./imageView";

// An image's row header: the name, which opens its details, and the file it came from.
export function ImageName({
  image,
  onDetails,
}: {
  image: ImageSummary;
  onDetails: (id: string) => void;
}) {
  return (
    <span className="flex min-w-0 flex-col">
      <AriaButton
        onPress={() => {
          onDetails(image.id);
        }}
        className="w-fit max-w-full cursor-pointer truncate text-left type-label text-ink outline-none hover:underline focus-visible:outline-2 focus-visible:outline-focus"
      >
        {image.name}
      </AriaButton>
      <span className="truncate type-small text-muted">{imageSource(image)}</span>
      {!isDeployable(image) ? (
        <span className="type-small text-fail-text">
          <Trans>Not deployable: only x64 images can be installed.</Trans>
        </span>
      ) : null}
    </span>
  );
}
