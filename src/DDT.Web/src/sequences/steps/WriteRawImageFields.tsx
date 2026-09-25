// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Link } from "@tanstack/react-router";

import { isDeployable, secureBootWarning, type ImageSummary } from "@/images/images";

import { FormField } from "../FormField";
import { fieldMessages } from "../problems";
import type { WriteRawImageStep } from "../sequences";
import { EMPTY_ID } from "../steps";
import type { KindFieldsProps } from "./kindFields";

import styles from "../form.module.scss";

function imageLabel(image: ImageSummary): string {
  const facts = [
    image.bootCapability === "SecureBootOk" ? "Secure Boot" : "not for Secure Boot",
    isDeployable(image) ? null : `${image.architecture ?? "unknown"} only`,
  ].filter((fact) => fact !== null);

  return `${image.name} (${facts.join(", ")})`;
}

export function WriteRawImageFields({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<WriteRawImageStep>) {
  const rawImages = catalog.images.filter((image) => image.kind === "RawDisk");
  const chosen = rawImages.find((image) => image.id === step.imageId);
  const warning = chosen === undefined ? null : secureBootWarning(chosen);
  // A Windows image chosen elsewhere, such as in an imported sequence, which this step cannot write.
  const windowsImage =
    chosen === undefined
      ? catalog.images.find((image) => image.id === step.imageId && image.kind === "Wim")
      : undefined;

  return (
    <>
      <p className={styles.explain}>
        Erases the whole disk and writes the image onto it. The machine then starts the image's
        \EFI\BOOT\BOOTX64.EFI, which DDT puts first in the firmware's boot order.
      </p>
      <FormField
        label="Raw disk image"
        messages={fieldMessages(findings, "imageId")}
        hint="Only images for x64 machines can be written."
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
            {step.imageId !== EMPTY_ID && chosen === undefined && (
              <option value={step.imageId}>
                {windowsImage === undefined
                  ? "Image deleted"
                  : `${windowsImage.name} (Windows image)`}
              </option>
            )}
            {rawImages.map((image) => (
              <option key={image.id} value={image.id} disabled={!isDeployable(image)}>
                {imageLabel(image)}
              </option>
            ))}
          </select>
        )}
      </FormField>
      {warning !== null && (
        <p className={styles.hint} role="note">
          {warning} {chosen?.bootDetail}
        </p>
      )}
      {rawImages.length === 0 && (
        <p className={styles.hint}>
          The library has no raw disk images yet. Upload a distribution's cloud image on the{" "}
          <Link to="/images">Images page</Link>.
        </p>
      )}
    </>
  );
}
