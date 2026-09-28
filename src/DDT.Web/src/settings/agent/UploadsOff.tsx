// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";

import { Notice } from "@/ui/Notice";

import { ConfigurationOrigin } from "../ConfigurationOrigin";
import { settingsOverviewQuery } from "../settings";

import type { Binary } from "./binary";

// The keys are kept for development, and the page cannot replace a file configuration names.
export function UploadsOff({ binary }: { binary: Binary }) {
  const overview = useQuery(settingsOverviewQuery);
  const setting = overview.data?.server.find((entry) => entry.key === binary.configurationKey);

  return (
    <Notice tone="attention" title={<Trans>Uploads are off</Trans>}>
      {binary.configured}
      {setting?.isSet === true && setting.source !== null ? (
        <span className="mt-1 block">
          <Trans>
            The key is set <ConfigurationOrigin setting={setting} />.
          </Trans>
        </span>
      ) : null}
    </Notice>
  );
}
