// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { Link } from "@tanstack/react-router";

import { Notice } from "@/ui/Notice";

import { ChoiceSetting } from "../fields/ChoiceSetting";
import type { Choice } from "../fields/fieldBase";
import type { WriteRawImageStep } from "../sequences";
import { EMPTY_ID } from "../steps";
import { rawImageChoice, secureBootWarning } from "./imageChoices";
import type { KindFieldsProps } from "./kindFields";

// Writes a whole disk, such as a Linux cloud image.
export function WriteRawImageFields({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<WriteRawImageStep>) {
  const rawImages = catalog.images.filter((image) => image.kind === "RawDisk");
  const chosen = rawImages.find((image) => image.id === step.imageId);
  const warning = chosen === undefined ? null : secureBootWarning(chosen);
  // An image this step can't write, such as a Windows image chosen in an imported sequence, or a deleted one.
  const other =
    chosen === undefined ? catalog.images.find((image) => image.id === step.imageId) : undefined;
  const otherName = other?.name ?? "";
  const choices: Choice[] = [
    ...(step.imageId !== EMPTY_ID && chosen === undefined
      ? [
          {
            id: step.imageId,
            label: other === undefined ? t`Image deleted` : t`${otherName} (Windows image)`,
          },
        ]
      : []),
    ...rawImages.map(rawImageChoice),
  ];

  return (
    <>
      <p className="text-ink-2 sm:col-span-2">
        <Trans>
          Erases the whole disk and writes the image onto it. The machine then starts the image's{" "}
          <span className="type-data">\EFI\BOOT\BOOTX64.EFI</span>, which DDT puts first in the
          firmware's boot order.
        </Trans>
      </p>
      <ChoiceSetting
        label={<Trans>Raw disk image</Trans>}
        field="imageId"
        findings={findings}
        className="sm:col-span-2"
        hint={<Trans>Only images for x64 machines can be written.</Trans>}
        placeholder={t`Choose an image`}
        value={step.imageId === EMPTY_ID ? null : step.imageId}
        choices={choices}
        onChange={(imageId) => {
          onChange({ imageId });
        }}
      />
      {warning !== null && chosen !== undefined ? (
        <Notice tone="attention" className="sm:col-span-2">
          {warning}
          {chosen.bootDetail !== null ? <> {chosen.bootDetail}</> : null}
        </Notice>
      ) : null}
      {rawImages.length === 0 ? (
        <p className="type-small text-ink-2 sm:col-span-2">
          <Trans>
            The library has no raw disk images yet. Upload a distribution's cloud image under{" "}
            <Link to="/library/images" className="font-semibold text-ink underline">
              OS images
            </Link>
            .
          </Trans>
        </p>
      ) : null}
    </>
  );
}
