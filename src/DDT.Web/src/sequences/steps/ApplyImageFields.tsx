// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Link } from "@tanstack/react-router";

import { isDeployable, type ImageSummary } from "@/images/images";

import { FormField } from "../FormField";
import { fieldMessages } from "../problems";
import type { ApplyImageStep } from "../sequences";
import { EMPTY_ID } from "../steps";
import type { KindFieldsProps } from "./kindFields";

import styles from "../form.module.scss";

function imageLabel(image: ImageSummary): string {
  const facts = [image.language, image.architecture ?? "unknown architecture"].filter(
    (fact) => fact !== null,
  );

  return `${image.name} (${facts.join(", ")})${isDeployable(image) ? "" : ", not x64"}`;
}

export function ApplyImageFields({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<ApplyImageStep>) {
  const windowsImages = catalog.images.filter((image) => image.kind === "Wim");
  const chosen = windowsImages.some((image) => image.id === step.imageId);
  // A raw disk image chosen elsewhere, such as in an imported sequence, which this step cannot install.
  const rawImage = chosen
    ? undefined
    : catalog.images.find((image) => image.id === step.imageId && image.kind === "RawDisk");

  return (
    <>
      <FormField
        label="Image"
        messages={fieldMessages(findings, "imageId")}
        hint="Only x64 images can be installed."
      >
        {(control) => (
          <select
            {...control}
            value={step.imageId}
            onChange={(event) => {
              onChange({ imageId: event.target.value });
            }}
          >
            {step.imageId === EMPTY_ID && <option value={EMPTY_ID}>Choose an image</option>}
            {step.imageId !== EMPTY_ID && !chosen && (
              <option value={step.imageId}>
                {rawImage === undefined ? "Image deleted" : `${rawImage.name} (raw disk image)`}
              </option>
            )}
            {windowsImages.map((image) => (
              <option key={image.id} value={image.id} disabled={!isDeployable(image)}>
                {imageLabel(image)}
              </option>
            ))}
          </select>
        )}
      </FormField>
      {windowsImages.length === 0 && (
        <p className={styles.hint}>
          The library has no Windows images yet. Upload a WIM file on the{" "}
          <Link to="/images">Images page</Link>.
        </p>
      )}
    </>
  );
}
