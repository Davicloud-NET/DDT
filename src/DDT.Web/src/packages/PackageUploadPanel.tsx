// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { useQueryClient } from "@tanstack/react-query";

import { UploadPanel } from "@/uploads/UploadPanel";

import { packagesQuery, type PackageKind, type PackageSummary } from "./packages";
import { describeResult } from "./packageView";

// Takes a zip of drivers or files; the package the server answers with joins the list.
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
          <Trans>
            A zip of driver folders with their .inf files. Choose the hardware models it is for once
            it is added.
          </Trans>
        ) : (
          <Trans>
            A zip of the files a Run script step needs. The step finds them unpacked in its working
            folder.
          </Trans>
        )
      }
      verifyingHint={<Trans>The server checks the zip and counts what is in it.</Trans>}
      leaveWhileVerifying={(file) =>
        t`The server goes on checking ${file} after you leave and adds it to the library when it finishes. If it refuses the file, you do not see why.`
      }
      describeResult={describeResult}
      onAdded={(added) => {
        queryClient.setQueryData(packagesQuery.queryKey, (list) =>
          list === undefined ? list : [...list.filter((item) => item.id !== added.id), added],
        );

        // A new driver package is for no model yet, so it opens at once to choose them.
        if (added.kind === "Drivers" && added.targets.length === 0) {
          onEdit(added);
        }
      }}
    />
  );
}
