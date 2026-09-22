// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";

import type { UploadKind } from "@/images/images";
import { percentOf } from "@/lib/format";
import type { UploadOutcome } from "@/uploads/resumableUpload";
import { UploadPanel } from "@/uploads/UploadPanel";

import { packagesQuery, type PackageKind, type PackageSummary } from "./packages";

import styles from "./PackageUpload.module.scss";

const packageKinds = ["Drivers", "Files"] as const;

const kindNames: Record<PackageKind, string> = {
  Drivers: "a driver package",
  Files: "a files package",
};

function kindName(kind: UploadKind): string {
  return kind === "Image" ? "an image" : kindNames[kind];
}

function describeResult(fileName: string, outcome: UploadOutcome, item: PackageSummary): string {
  switch (outcome) {
    case "added":
      return `Added ${item.name} from ${fileName}.`;
    case "duplicate":
      return `${fileName} is in the library already, as ${item.name}.`;
    case "unclear":
      return `The library now holds ${item.name} from ${fileName}.`;
  }
}

// A zip of driver folders or of files. The kind is part of what the server recognises the file by, so an
// unfinished upload resumes only as the kind it was started as.
export function PackageUpload() {
  const queryClient = useQueryClient();
  const kindId = useId();
  const [kind, setKind] = useState<PackageKind>("Drivers");

  return (
    <UploadPanel<PackageSummary>
      kind={kind}
      kinds={packageKinds}
      fileLabel="Zip file"
      accept=".zip"
      hint={
        kind === "Drivers"
          ? "The driver folder of one hardware model, with its INF files. Set the models it is for once it is uploaded."
          : "Files a Run script step unpacks before its script runs, as the script's working directory."
      }
      verifyingHint="The server checks every entry of the zip. This takes a moment for a large package."
      leaveWhileVerifying={(fileName) =>
        `The server goes on checking ${fileName} after you leave and adds the package to the library when it finishes. If it refuses the file, you do not see why.`
      }
      resumeText={(session) =>
        `Select ${session.fileName} again as ${kindName(session.kind ?? "Image")} to resume (${String(percentOf(session.offset, session.length))}%).`
      }
      describeResult={describeResult}
      onAdded={() => {
        void queryClient.invalidateQueries({ queryKey: packagesQuery.queryKey });
      }}
    >
      <div className={styles.kind}>
        <label htmlFor={kindId}>Kind of package</label>
        <select
          id={kindId}
          value={kind}
          onChange={(event) => {
            setKind(event.target.value as PackageKind);
          }}
        >
          <option value="Drivers">Drivers, for machines of the models it targets</option>
          <option value="Files">Files, for Run script steps</option>
        </select>
      </div>
    </UploadPanel>
  );
}
