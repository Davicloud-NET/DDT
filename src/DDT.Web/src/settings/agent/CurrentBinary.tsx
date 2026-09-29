// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { formatBytes } from "@/lib/format";
import { fullTime, relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { ChangedBy } from "@/ui/ChangedBy";
import { CopyButton } from "@/ui/CopyButton";
import { Facts } from "@/ui/Facts";
import { Panel } from "@/ui/Panel";

import type { AgentBinaryView } from "../agentBinary";

import type { Binary } from "./binary";

export function CurrentBinary({ binary, view }: { binary: Binary; view: AgentBinaryView }) {
  const now = useNow(60_000);
  const sha256 = view.sha256;
  const uploadedWhen = view.uploadedUtc === null ? null : relativeTime(view.uploadedUtc, now);

  return (
    <Panel
      title={binary.title}
      actions={
        sha256 === null ? null : (
          <CopyButton size="sm" text={sha256}>
            <Trans>Copy SHA-256</Trans>
          </CopyButton>
        )
      }
    >
      <Facts
        items={[
          { label: binary.runsLabel, value: binary.runs[view.source] },
          ...(sha256 === null ? [] : [{ label: binary.hashLabel, value: sha256, mono: true }]),
          ...(view.size === null
            ? []
            : [{ label: binary.sizeLabel, value: formatBytes(view.size) }]),
          ...(uploadedWhen === null || view.uploadedUtc === null
            ? []
            : [
                {
                  label: <Trans>Uploaded</Trans>,
                  value: (
                    <ChangedBy
                      when={uploadedWhen}
                      by={view.uploadedBy}
                      title={fullTime(view.uploadedUtc)}
                    />
                  ),
                },
              ]),
        ]}
      />
      {sha256 === null ? null : (
        <p className="max-w-[80ch] type-small text-ink-2">
          <Trans>
            Compare the SHA-256 with the one published for the release you meant to install.
          </Trans>
        </p>
      )}
    </Panel>
  );
}
