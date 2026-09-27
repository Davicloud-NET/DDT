// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { Link } from "@tanstack/react-router";
import { useContext } from "react";

import { isDeployable, type ImageSummary } from "@/images/images";

import { EditorLock } from "../editorLock";
import { ChoiceSetting, FlagSetting, NumberSetting, TextSetting, type Choice } from "../fields";
import type {
  ApplyImageStep,
  InjectDriversStep,
  JoinDomainStep,
  PartitionStep,
  WriteUnattendStep,
} from "../sequences";
import { EMPTY_ID } from "../steps";
import { DomainJoinCheck } from "./DomainJoinCheck";
import { orNull, type KindFieldsProps } from "./kindFields";

// The steps that install Windows: partition, apply the image, add drivers, write the answer file, join the domain.

const linkClass = "font-semibold text-ink underline hover:text-ink-2";

export function PartitionFields({ step, findings, onChange }: KindFieldsProps<PartitionStep>) {
  return (
    <>
      <p className="text-ink-2 sm:col-span-2">
        <Trans>
          Erases the whole disk and creates the EFI system, MSR, Windows and recovery partitions.
        </Trans>
      </p>
      <NumberSetting
        label={<Trans>System partition in MB</Trans>}
        field="systemPartitionMegabytes"
        findings={findings}
        hint={<Trans>From 260 to 4096 MB.</Trans>}
        minValue={260}
        maxValue={4096}
        value={step.systemPartitionMegabytes}
        onChange={(systemPartitionMegabytes) => {
          onChange({ systemPartitionMegabytes });
        }}
      />
      <NumberSetting
        label={<Trans>Recovery partition in MB</Trans>}
        field="recoveryPartitionMegabytes"
        findings={findings}
        hint={<Trans>From 300 to 65536 MB.</Trans>}
        minValue={300}
        maxValue={65536}
        value={step.recoveryPartitionMegabytes}
        onChange={(recoveryPartitionMegabytes) => {
          onChange({ recoveryPartitionMegabytes });
        }}
      />
    </>
  );
}

function windowsImageChoice(image: ImageSummary): Choice {
  const architecture = image.architecture ?? t`unknown architecture`;
  const facts = [image.language, architecture].filter((fact) => fact !== null).join(", ");

  return {
    id: image.id,
    label: image.name,
    description: isDeployable(image) ? facts : t`${facts}, cannot be installed`,
    isDisabled: !isDeployable(image),
  };
}

export function ApplyImageFields({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<ApplyImageStep>) {
  const windowsImages = catalog.images.filter((image) => image.kind === "Wim");
  const listed = windowsImages.some((image) => image.id === step.imageId);
  // An image this step cannot install, such as a raw disk image chosen in an imported sequence, or one deleted.
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
            <Link to="/library/images" className={linkClass}>
              OS images
            </Link>
            .
          </Trans>
        </p>
      ) : null}
    </>
  );
}

export function InjectDriversFields({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<InjectDriversStep>) {
  const count = catalog.packages.filter((item) => item.kind === "Drivers").length;

  return (
    <>
      <p className="text-ink-2 sm:col-span-2">
        <Trans>
          Adds the driver packages whose targets match the machine's model to the applied image. The
          packages are chosen when the sequence is assigned.
        </Trans>
      </p>
      <p className="type-small text-ink-2 sm:col-span-2">
        {count === 0
          ? t`No driver package is in the library yet.`
          : plural(count, {
              one: "# driver package is in the library, for the models its targets name.",
              other: "# driver packages are in the library, each for the models its targets name.",
            })}{" "}
        <Link to="/library/drivers" className={linkClass}>
          <Trans>Driver packages and their targets are under Drivers.</Trans>
        </Link>
      </p>
      <FlagSetting
        label={<Trans>Fail when no driver package matches the model</Trans>}
        field="requireMatch"
        findings={findings}
        className="sm:col-span-2"
        hint={<Trans>Otherwise the step is done without adding drivers.</Trans>}
        value={step.requireMatch}
        onChange={(requireMatch) => {
          onChange({ requireMatch });
        }}
      />
    </>
  );
}

export function WriteUnattendFields({
  step,
  findings,
  onChange,
}: KindFieldsProps<WriteUnattendStep>) {
  const serverDefault = t`Server default`;

  return (
    <>
      <p className="text-ink-2 sm:col-span-2">
        <Trans>
          Writes the answer file Windows setup reads at its first start. An empty setting takes the
          server's default.
        </Trans>
      </p>
      <TextSetting
        label={<Trans>Time zone</Trans>}
        field="timeZone"
        findings={findings}
        className="sm:col-span-2"
        hint={
          <Trans>
            A Windows time zone id as tzutil /l lists it, such as W. Europe Standard Time.
          </Trans>
        }
        placeholder={serverDefault}
        value={step.timeZone ?? ""}
        onChange={(text) => {
          onChange({ timeZone: orNull(text) });
        }}
      />
      <TextSetting
        label={<Trans>Language and region</Trans>}
        field="locale"
        findings={findings}
        hint={<Trans>Such as de-DE.</Trans>}
        placeholder={serverDefault}
        mono
        value={step.locale ?? ""}
        onChange={(text) => {
          onChange({ locale: orNull(text) });
        }}
      />
      <TextSetting
        label={<Trans>Keyboard</Trans>}
        field="keyboard"
        findings={findings}
        hint={<Trans>An input locale such as de-DE or 0407:00000407.</Trans>}
        placeholder={serverDefault}
        mono
        value={step.keyboard ?? ""}
        onChange={(text) => {
          onChange({ keyboard: orNull(text) });
        }}
      />
      <FlagSetting
        label={<Trans>Add the local administrator</Trans>}
        field="localAdministrator"
        findings={findings}
        className="sm:col-span-2"
        hint={
          <Trans>
            The account and its password are set on the Deployment defaults page, never in the
            sequence.
          </Trans>
        }
        value={step.localAdministrator}
        onChange={(localAdministrator) => {
          onChange({ localAdministrator });
        }}
      />
    </>
  );
}

export function JoinDomainFields({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<JoinDomainStep>) {
  const locked = useContext(EditorLock);

  return (
    <>
      <p className="text-ink-2 sm:col-span-2">
        {catalog.domainConfigured === false ? (
          <Trans>No domain is set on the Deployment defaults page, so this step cannot run.</Trans>
        ) : (
          <Trans>
            Joins the domain set on the Deployment defaults page, in Windows after the hand-over.
            The machine needs a computer name.
          </Trans>
        )}
      </p>
      <TextSetting
        label={<Trans>Organizational unit</Trans>}
        field="organizationalUnit"
        findings={findings}
        className="sm:col-span-2"
        hint={
          <Trans>
            Such as OU=Workstations,DC=example,DC=com. Empty takes the default from the Deployment
            defaults page.
          </Trans>
        }
        placeholder={t`The default`}
        mono
        value={step.organizationalUnit ?? ""}
        onChange={(text) => {
          onChange({ organizationalUnit: orNull(text) });
        }}
      />
      {catalog.domainConfigured === true && !locked ? (
        <DomainJoinCheck
          organizationalUnit={step.organizationalUnit ?? null}
          className="sm:col-span-2"
        />
      ) : null}
    </>
  );
}
