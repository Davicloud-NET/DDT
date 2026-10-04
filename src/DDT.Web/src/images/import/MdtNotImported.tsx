// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { MdtShareView } from "./imports";

// What an MDT deployment share holds that DDT has no place for yet, by name, so nobody thinks it came along.
export function MdtNotImported({ notImported }: { notImported: MdtShareView["notImported"] }) {
  const sequences = notImported.taskSequences.join(", ");
  const applications = notImported.applications.join(", ");
  const sections = notImported.rules.join(", ");

  if (sequences === "" && applications === "" && sections === "") {
    return null;
  }

  return (
    <section className="flex flex-col gap-1 type-small text-ink-2">
      <h3 className="type-label text-ink">
        <Trans>Not imported yet</Trans>
      </h3>
      {sequences === "" ? null : (
        <p>
          <Trans>Task sequences: {sequences}. Build them again from a template.</Trans>
        </p>
      )}
      {applications === "" ? null : (
        <p>
          <Trans>Applications: {applications}.</Trans>
        </p>
      )}
      {sections === "" ? null : (
        <p>
          <Trans>
            CustomSettings.ini, with the sections {sections}. Its settings become rules and
            deployment defaults in DDT.
          </Trans>
        </p>
      )}
    </section>
  );
}
