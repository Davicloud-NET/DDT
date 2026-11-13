// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Checkbox } from "@/ui/Checkbox";

import { isChosen, type MdtApplication } from "./imports";

// The share's applications and bundles with a box each. One the library has by name already cannot be ticked, and one
// disabled in MDT starts unticked.
export function MdtApplicationChoices({
  applications,
  left,
  onToggle,
}: {
  applications: MdtApplication[];
  left: ReadonlySet<string>;
  onToggle: (id: string, chosen: boolean, byDefault: boolean) => void;
}) {
  return (
    <section className="flex flex-col gap-2">
      <h3 className="type-label text-ink">
        <Trans>Applications and bundles</Trans>
      </h3>
      {applications.length === 0 ? (
        <p className="type-small text-muted">
          <Trans>The share lists none.</Trans>
        </p>
      ) : null}
      {applications.map((application) => (
        <Checkbox
          key={application.id}
          isSelected={isChosen(application, left)}
          isDisabled={application.inLibrary}
          onChange={(chosen) => {
            onToggle(application.id, chosen, application.enabled);
          }}
        >
          <span className="flex min-w-0 flex-col">
            <span>{application.name}</span>
            <span className="type-small text-muted">
              {application.enabled ? null : (
                <>
                  <Trans>Disabled in MDT.</Trans>{" "}
                </>
              )}
              <Detail application={application} />
            </span>
          </span>
        </Checkbox>
      ))}
    </section>
  );
}

function Detail({ application }: { application: MdtApplication }) {
  if (application.inLibrary) {
    return <Trans>In the library already.</Trans>;
  }

  if (application.kind === "Bundle") {
    return <Trans>Bundle.</Trans>;
  }

  return application.hasFiles ? <Trans>With files.</Trans> : <Trans>Without files.</Trans>;
}
