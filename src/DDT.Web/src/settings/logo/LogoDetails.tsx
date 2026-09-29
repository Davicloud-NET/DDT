// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { formatBytes } from "@/lib/format";
import { fullTime, relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { ChangedBy } from "@/ui/ChangedBy";
import { Facts } from "@/ui/Facts";

import type { ConsoleLogoView } from "../consoleLogo";

export function LogoDetails({ view }: { view: ConsoleLogoView }) {
  const now = useNow(60_000);

  if (view.sha256 === null) {
    return null;
  }

  const width = String(view.width ?? 0);
  const height = String(view.height ?? 0);
  const uploadedWhen = view.uploadedUtc === null ? null : relativeTime(view.uploadedUtc, now);

  return (
    <Facts
      items={[
        {
          label: <Trans>Picture</Trans>,
          value: (
            <Trans>
              PNG, {width} by {height} pixels
            </Trans>
          ),
        },
        ...(view.size === null
          ? []
          : [{ label: <Trans>Size</Trans>, value: formatBytes(view.size) }]),
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
  );
}
