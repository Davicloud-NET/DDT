// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { Link } from "@tanstack/react-router";
import { useRef } from "react";

import { isDeployable, type ImageSummary } from "@/images/images";
import { Notice } from "@/ui/Notice";

import { ChoiceSetting, FlagSetting, TextSetting, type Choice } from "../fields";
import type { WriteCloudInitSeedStep, WriteRawImageStep } from "../sequences";
import { EMPTY_ID, seedPlaceholders } from "../steps";
import type { KindFieldsProps } from "./kindFields";

// The steps that write a whole disk, such as a Linux cloud image, and the seed cloud-init reads at its first start.

function rawImageChoice(image: ImageSummary): Choice {
  const architecture = image.architecture ?? t`another processor`;
  const signature =
    image.bootCapability === "SecureBootOk"
      ? t`Signed for Secure Boot`
      : image.bootCapability === "NotSigned"
        ? t`Not signed for Secure Boot`
        : t`Not known whether it is signed for Secure Boot`;

  return {
    id: image.id,
    label: image.name,
    description: isDeployable(image) ? signature : t`For ${architecture} only, cannot be written`,
    isDisabled: !isDeployable(image),
  };
}

// What an image that may not start with Secure Boot on means for the machines, or null for one that does.
function secureBootWarning(image: ImageSummary): string | null {
  switch (image.bootCapability) {
    case "NotSigned":
      return t`This image will not start with Secure Boot on. Turn Secure Boot off in the machine's firmware setup, or enroll your own key.`;
    case "Unknown":
    case null:
      return t`This image may not start with Secure Boot on, as DDT could not tell whether it is signed for it. Turn Secure Boot off in the machine's firmware setup, or enroll your own key.`;
    case "SecureBootOk":
      return null;
  }
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
  // An image this step cannot write, such as a Windows image chosen in an imported sequence, or one deleted.
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

const placeholders = seedPlaceholders.map((name) => `{{${name}}}`).join(", ");

export function WriteCloudInitSeedFields({
  step,
  findings,
  onChange,
}: KindFieldsProps<WriteCloudInitSeedStep>) {
  // What network-config held when it was turned off, while this step is shown.
  const lastNetworkConfig = useRef("version: 2\n");
  const example = 'hostname: "{{ComputerName}}"';

  return (
    <>
      <p className="text-ink-2 sm:col-span-2">
        <Trans>
          Adds a partition labelled CIDATA at the end of the disk, where cloud-init finds these
          files at the machine's first start. Everyone who can sign in to DDT can read them, so
          passwords go in hashed.
        </Trans>
      </p>
      <p className="type-small text-ink-2 sm:col-span-2">
        <Trans>
          DDT fills in <span className="type-data">{placeholders}</span> with the machine's values,
          and the name of any of the run's values, such as a variable of the sequence or a value a
          rule sets, with that value. Put them in double quotes, such as{" "}
          <span className="type-data">{example}</span>. Anything else in double braces stays as it
          is, for cloud-init's own templates.
        </Trans>
      </p>
      <TextSetting
        label="meta-data"
        field="metaData"
        findings={findings}
        className="sm:col-span-2"
        mono
        multiline
        rows={4}
        value={step.metaData}
        onChange={(metaData) => {
          onChange({ metaData });
        }}
      />
      <TextSetting
        label="user-data"
        field="userData"
        findings={findings}
        className="sm:col-span-2"
        hint={<Trans>A #cloud-config document, or a script that starts with #!.</Trans>}
        mono
        multiline
        rows={10}
        value={step.userData}
        onChange={(userData) => {
          onChange({ userData });
        }}
      />
      <FlagSetting
        label={<Trans>Write network-config</Trans>}
        field={step.networkConfig === null ? "networkConfig" : "networkConfig.switch"}
        findings={findings}
        className="sm:col-span-2"
        hint={<Trans>Without it, the image configures its network itself, usually by DHCP.</Trans>}
        value={step.networkConfig !== null}
        onChange={(write) => {
          if (!write && step.networkConfig !== null) {
            lastNetworkConfig.current = step.networkConfig;
          }

          onChange({ networkConfig: write ? lastNetworkConfig.current : null }, true);
        }}
      />
      {step.networkConfig !== null ? (
        <TextSetting
          label="network-config"
          field="networkConfig"
          findings={findings}
          className="sm:col-span-2"
          mono
          multiline
          rows={6}
          value={step.networkConfig}
          onChange={(networkConfig) => {
            onChange({ networkConfig });
          }}
        />
      ) : null}
    </>
  );
}
