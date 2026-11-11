// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { PxeForm } from "../networkBoot";
import { SettingText } from "../parts/SettingText";

// The boot server that a boot target's answer names, as cells in the target's field grid.
export function BootServerFields({
  form,
  path,
  http,
  editable,
}: {
  form: PxeForm;
  path: (field: string) => string;
  http: boolean;
  editable: boolean;
}) {
  return (
    <>
      <SettingText
        form={form}
        field={path("serverAddress")}
        canChange={editable}
        label={<Trans>Server address</Trans>}
        hint={
          http ? (
            <Trans>
              The IPv4 address the answer names as the boot server. Left empty, DDT's address on the
              interface the request arrived on.
            </Trans>
          ) : (
            <Trans>
              The IPv4 address machines load the boot file from. Left empty, DDT's address on the
              interface the request arrived on.
            </Trans>
          )
        }
        mono
      />
      <SettingText
        form={form}
        field={path("serverHostName")}
        canChange={editable}
        label={<Trans>Server host name</Trans>}
        hint={<Trans>ASCII, at most 63 characters. Left empty, its address.</Trans>}
        mono
      />
    </>
  );
}
