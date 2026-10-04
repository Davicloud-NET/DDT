// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation } from "@tanstack/react-query";
import { useState } from "react";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import { Button } from "@/ui/Button";
import { Checkbox } from "@/ui/Checkbox";
import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";
import { ListBoxItem, Select } from "@/ui/Select";

import { AdkState } from "./AdkState";
import { startBuild, type BootImageView } from "./bootImage";
import { keyboardLayoutOptions, serverLayout } from "./keyboardLayouts";

// Builds the boot image on the server, which the DDT Helper service of a Windows install does. The options start as
// the served build has them.
export function BuildPanel({ view }: { view: BootImageView }) {
  const { t: translate } = useLingui();
  const administrator = useIsAdministrator();
  const recorded = view.build?.keyboardLayout ?? null;
  const [layout, setLayout] = useState(recorded?.toLowerCase() ?? serverLayout);
  const [powerShell, setPowerShell] = useState(view.build?.powerShell ?? true);
  const build = useMutation({ mutationFn: startBuild });
  const running = view.job?.state === "Running";
  const adk = view.builder.adk;
  const address = view.builder.serverUrl;
  const ready = adk !== null && adk.installed && adk.supported;

  return (
    <Panel title={<Trans>Build on this server</Trans>}>
      <p className="text-ink-2">
        <Trans>
          A build takes the address {address}, the server's root certificate, its agent and the
          drivers flagged for Windows PE. Machines netboot the new image once it is done, and the
          build before it stays to go back to.
        </Trans>
      </p>
      {adk === null ? null : <AdkState adk={adk} isBusy={running} />}

      {administrator ? (
        <div className="flex flex-wrap items-end gap-x-6 gap-y-3">
          <Select
            label={<Trans>Keyboard layout</Trans>}
            hint={<Trans>For typing at a machine in Windows PE</Trans>}
            className="w-72"
            value={layout}
            onChange={(key) => {
              if (key !== null) {
                setLayout(String(key));
              }
            }}
          >
            <ListBoxItem id={serverLayout}>{translate`The server's own`}</ListBoxItem>
            {keyboardLayoutOptions(recorded).map((option) => (
              <ListBoxItem key={option.id} id={option.id}>
                {option.name}
              </ListBoxItem>
            ))}
          </Select>
          <Checkbox isSelected={powerShell} onChange={setPowerShell} className="pb-7">
            <Trans>With PowerShell, for steps that run a script in Windows PE</Trans>
          </Checkbox>
          <Button
            variant="primary"
            className="mb-6.5"
            isDisabled={!ready || running || build.isPending}
            onPress={() => {
              build.mutate({
                keyboardLayout: layout === serverLayout ? null : layout,
                skipPowerShell: !powerShell,
              });
            }}
          >
            <Trans>Build the boot image</Trans>
          </Button>
        </div>
      ) : (
        <p className="type-small text-muted">
          <Trans>Only administrators build the boot image.</Trans>
        </p>
      )}
      {build.isError ? <Notice tone="fail">{build.error.message}</Notice> : null}
    </Panel>
  );
}
