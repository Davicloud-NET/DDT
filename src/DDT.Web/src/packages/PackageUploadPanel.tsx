// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQueryClient } from "@tanstack/react-query";

import type { ImportFile } from "@/images/import/imports";
import { UploadPanel } from "@/uploads/UploadPanel";

import { packagesQuery, type PackageKind, type PackageSummary } from "./packages";
import { describeWindowsResult, isLanguageIso } from "./packageUploadResult";

const WINDOWS_FILES = [".zip", ".msu", ".cab", ".iso"];

// Takes a zip of drivers or files, or a Windows package's files. The package in the server's answer is added to the
// list. A Languages and Optional Features ISO stays on the server, and its languages are chosen next.
export function PackageUploadPanel({
  kind,
  onEdit,
  onLanguageIso,
}: {
  kind: PackageKind;
  onEdit: (item: PackageSummary) => void;
  onLanguageIso?: ((file: ImportFile) => void) | undefined;
}) {
  const queryClient = useQueryClient();
  const windows = kind === "WindowsPackage";

  return (
    <UploadPanel<PackageSummary | ImportFile>
      kind={kind}
      kinds={[kind]}
      what={windows ? <Trans>a .msu, .cab, zip or ISO file</Trans> : <Trans>a zip file</Trans>}
      accept={windows ? WINDOWS_FILES : [".zip"]}
      hint={<UploadHint kind={kind} />}
      describeResult={describeWindowsResult}
      onAdded={(added) => {
        if (isLanguageIso(added)) {
          onLanguageIso?.(added);

          return;
        }

        queryClient.setQueryData(packagesQuery.queryKey, (list) =>
          list === undefined ? list : [...list.filter((item) => item.id !== added.id), added],
        );

        // A new driver package has no models yet, so its dialog opens at once to pick them.
        if (added.kind === "Drivers" && added.targets.length === 0) {
          onEdit(added);
        }
      }}
    />
  );
}

function UploadHint({ kind }: { kind: PackageKind }) {
  switch (kind) {
    case "Drivers":
      return <Trans>Driver folders with their .inf files.</Trans>;
    case "WindowsPackage":
      return (
        <Trans>
          Updates from the Microsoft Update Catalog, cabinets, or the Languages and Optional
          Features ISO.
        </Trans>
      );
    default:
      return <Trans>A Run script step finds them unpacked in its working folder.</Trans>;
  }
}
