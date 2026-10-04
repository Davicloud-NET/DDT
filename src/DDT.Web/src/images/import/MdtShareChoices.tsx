// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { formatBytes } from "@/lib/format";
import { Checkbox } from "@/ui/Checkbox";

import type { MdtDriverGroup, MdtImageFile, MdtShareView } from "./imports";
import { MdtNotImported } from "./MdtNotImported";

// The share's image files and driver groups with a box each, and below them what DDT has no place for yet.
export function MdtShareChoices({
  view,
  left,
  onToggle,
}: {
  view: MdtShareView;
  left: ReadonlySet<string>;
  onToggle: (id: string, chosen: boolean) => void;
}) {
  return (
    <div className="flex flex-col gap-4">
      <p className="type-data text-muted">{view.path}</p>

      <section className="flex flex-col gap-2">
        <h3 className="type-label text-ink">
          <Trans>Operating systems</Trans>
        </h3>
        {view.imageFiles.length === 0 ? (
          <p className="type-small text-muted">
            <Trans>The share lists none.</Trans>
          </p>
        ) : null}
        {view.imageFiles.map((file) => (
          <Checkbox
            key={file.file}
            isSelected={file.found && !left.has(file.file)}
            isDisabled={!file.found}
            onChange={(chosen) => {
              onToggle(file.file, chosen);
            }}
          >
            <ImageFile file={file} />
          </Checkbox>
        ))}
      </section>

      <section className="flex flex-col gap-2">
        <h3 className="type-label text-ink">
          <Trans>Driver groups, each as a driver package</Trans>
        </h3>
        {view.driverGroups.length === 0 ? (
          <p className="type-small text-muted">
            <Trans>The share has no folder of Out-of-Box Drivers with drivers in it.</Trans>
          </p>
        ) : null}
        {view.driverGroups.map((group) => (
          <Checkbox
            key={group.id}
            isSelected={!left.has(group.id)}
            onChange={(chosen) => {
              onToggle(group.id, chosen);
            }}
          >
            <DriverGroup group={group} />
          </Checkbox>
        ))}
      </section>

      <MdtNotImported notImported={view.notImported} />
    </div>
  );
}

function ImageFile({ file }: { file: MdtImageFile }) {
  const name = file.file;

  return (
    <span className="flex min-w-0 flex-col">
      <span>{file.names.join(", ")}</span>
      <span className="type-small text-muted">
        {file.found ? (
          `${name}, ${formatBytes(file.sizeBytes)}`
        ) : (
          <Trans>{name} is no longer in the share.</Trans>
        )}
      </span>
    </span>
  );
}

function DriverGroup({ group }: { group: MdtDriverGroup }) {
  const drivers = group.drivers;
  const maker = group.manufacturer ?? "";
  const model = group.model;

  return (
    <span className="flex min-w-0 flex-col">
      <span>{group.name}</span>
      <span className="type-small text-muted">
        {plural(drivers, { one: "# driver", other: "# drivers" })}, {formatBytes(group.sizeBytes)}.{" "}
        {model === null ? (
          <Trans>For no model yet: set its models under Library, Drivers.</Trans>
        ) : (
          <Trans>
            For {maker} {model}.
          </Trans>
        )}
      </span>
    </span>
  );
}
