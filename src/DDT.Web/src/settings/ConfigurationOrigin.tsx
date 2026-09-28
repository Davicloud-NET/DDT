// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { ServerSetting } from "./settings";

// Where a configuration value comes from, as the overview names its source: the environment, the command line or a
// file such as appsettings.json. A value that is not set but shown is the default the server applies.
export function ConfigurationOrigin({ setting }: { setting: ServerSetting }) {
  const source = setting.source;

  if (!setting.isSet) {
    return setting.value === null ? null : <Trans>default</Trans>;
  }

  switch (source) {
    case null:
      return null;
    case "environment variable":
      return <Trans>from an environment variable</Trans>;
    case "command line":
      return <Trans>from the command line</Trans>;
    case "configuration":
    case "a configuration file":
      return <Trans>from configuration</Trans>;
    default:
      return <Trans>from {source}</Trans>;
  }
}
