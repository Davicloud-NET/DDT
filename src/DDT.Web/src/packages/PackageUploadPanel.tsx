// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQueryClient } from "@tanstack/react-query";

import { UploadPanel } from "@/uploads/UploadPanel";

import { packagesQuery, type PackageKind, type PackageSummary } from "./packages";
import { describeResult } from "./packageView";

// Takes a zip of drivers or files. The package in the server's answer is added to the list.
export function PackageUploadPanel({
  kind,
  onEdit,
}: {
  kind: PackageKind;
  onEdit: (item: PackageSummary) => void;
}) {
  const queryClient = useQueryClient();
  const drivers = kind === "Drivers";

  return (
    <UploadPanel<PackageSummary>
      kind={kind}
      kinds={[kind]}
      what={<Trans>a zip file</Trans>}
      accept={[".zip"]}
      hint={
        drivers ? (
          <Trans>Driver folders with their .inf files.</Trans>
        ) : (
          <Trans>A Run script step finds them unpacked in its working folder.</Trans>
        )
      }
      describeResult={describeResult}
      onAdded={(added) => {
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
