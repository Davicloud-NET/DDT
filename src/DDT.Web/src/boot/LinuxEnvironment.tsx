// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { formattingLocale } from "@/i18n/i18n";
import { Facts } from "@/ui/Facts";
import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";

import {
  keymapName,
  linuxBootProblem,
  linuxSourcesUrl,
  snapshotDate,
  type LinuxBootView,
} from "./linuxBoot";
import { LinuxSecureBootLine } from "./LinuxSecureBootLine";

// The Linux boot environment, which comes with DDT and is not built here: what it is made of, which machines start it
// with Secure Boot on, and which task sequences run in it.
export function LinuxEnvironment({ linux }: { linux: LinuxBootView }) {
  const problem = linuxBootProblem(linux);
  const unknown = t`Not recorded`;

  return (
    <Panel title={<Trans>Linux boot environment</Trans>}>
      {problem === null ? null : <Notice tone="attention">{problem}</Notice>}
      <p className="max-w-[80ch] text-ink-2">
        <Trans>
          It comes with DDT, so there is nothing to build. Task sequences that write a Linux image
          run in it, and Windows sequences run in Windows PE.
        </Trans>
      </p>
      {linux.kernel === null && linux.shim === null ? null : (
        <Facts
          items={[
            { label: <Trans>Kernel</Trans>, value: linux.kernel ?? unknown, mono: true },
            { label: <Trans>shim</Trans>, value: linux.shim ?? unknown, mono: true },
            { label: <Trans>GRUB</Trans>, value: linux.grub ?? unknown, mono: true },
            {
              label: <Trans>Debian packages as of</Trans>,
              value:
                linux.snapshot === null
                  ? unknown
                  : snapshotDate(linux.snapshot, formattingLocale()),
            },
            { label: <Trans>Boot file</Trans>, value: linux.bootFile, mono: true },
            {
              label: <Trans>Keyboard layouts</Trans>,
              value:
                linux.keymaps.length === 0 ? unknown : linux.keymaps.map(keymapName).join(", "),
            },
          ]}
        />
      )}
      <LinuxSecureBootLine signedUnder={linux.shimSignedUnder} />
      <p className="type-small text-ink-2">
        <Trans>
          Its source code is published with every release of DDT:{" "}
          <a href={linuxSourcesUrl} className="underline">
            the source packages it was built from
          </a>
          .
        </Trans>
      </p>
    </Panel>
  );
}
