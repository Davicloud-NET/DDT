// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { Link } from "@tanstack/react-router";

import { ChoiceSetting } from "../fields/ChoiceSetting";
import type { Choice } from "../fields/fieldBase";
import type { ApplyImageStep } from "../sequences";
import { EMPTY_ID } from "../steps";
import { windowsImageChoice } from "./imageChoices";
import type { KindFieldsProps } from "./kindFields";
import { STEP_LINK_CLASS } from "./stepLinkClass";

export function ApplyImageFields({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<ApplyImageStep>) {
  const windowsImages = catalog.images.filter((image) => image.kind === "Wim");
  const listed = windowsImages.some((image) => image.id === step.imageId);
  // An image this step can't install, such as a raw disk image chosen in an imported sequence, or a deleted one.
  const other = catalog.images.find((image) => image.id === step.imageId);
  const otherName = other?.name ?? "";
  const choices: Choice[] = [
    ...(step.imageId !== EMPTY_ID && !listed
      ? [
          {
            id: step.imageId,
            label: other === undefined ? t`Image deleted` : t`${otherName} (raw disk image)`,
          },
        ]
      : []),
    ...windowsImages.map(windowsImageChoice),
  ];

  return (
    <>
      <ChoiceSetting
        label={<Trans>Image</Trans>}
        field="imageId"
        findings={findings}
        className="sm:col-span-2"
        hint={<Trans>Only x64 images can be installed.</Trans>}
        placeholder={t`Choose an image`}
        value={step.imageId === EMPTY_ID ? null : step.imageId}
        choices={choices}
        onChange={(imageId) => {
          onChange({ imageId });
        }}
      />
      {windowsImages.length === 0 ? (
        <p className="type-small text-ink-2 sm:col-span-2">
          <Trans>
            The library has no Windows images yet. Upload a WIM file under{" "}
            <Link to="/library/images" className={STEP_LINK_CLASS}>
              OS images
            </Link>
            .
          </Trans>
        </p>
      ) : null}
    </>
  );
}
