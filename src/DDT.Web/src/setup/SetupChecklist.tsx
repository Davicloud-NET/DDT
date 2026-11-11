// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { useState } from "react";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import { Button } from "@/ui/Button";
import { Panel } from "@/ui/Panel";

import { canNetboot, checklistQuery, dismiss, isComplete, isDismissed } from "./checklist";
import { ChecklistStep } from "./ChecklistStep";

// The way from a new server to its first deployment, for administrators. Each step ticks itself when the server sees it
// done. On the first page it can be dismissed; the Server page keeps it until every step is done.
export function SetupChecklist({ dismissible = false }: { dismissible?: boolean }) {
  const administrator = useIsAdministrator();
  const checklist = useQuery({ ...checklistQuery, enabled: administrator }).data;
  const [dismissed, setDismissed] = useState(isDismissed);

  if (checklist === undefined || isComplete(checklist) || (dismissible && dismissed)) {
    return null;
  }

  return (
    <Panel
      title={<Trans>Before the first deployment</Trans>}
      actions={
        dismissible ? (
          <Button
            size="sm"
            variant="quiet"
            onPress={() => {
              dismiss();
              setDismissed(true);
            }}
          >
            <Trans>Dismiss</Trans>
          </Button>
        ) : null
      }
    >
      <ol className="flex flex-col gap-2.5">
        <ChecklistStep done={checklist.password}>
          <Trans>
            <Link to="/account" className="underline">
              Change the first password
            </Link>
            .
          </Trans>
        </ChecklistStep>
        <ChecklistStep done={null}>
          <Trans>
            Trust{" "}
            <a href="/api/about/root-certificate" className="underline">
              DDT's root certificate
            </a>{" "}
            in the browsers that manage it.
          </Trans>
        </ChecklistStep>
        <ChecklistStep done={checklist.netboot}>
          <Trans>
            <Link to="/boot/network" className="underline">
              Choose the network card
            </Link>{" "}
            DDT answers netboot on.
          </Trans>
        </ChecklistStep>
        <ChecklistStep done={canNetboot(checklist)}>
          {checklist.bootImage || checklist.linuxBoot !== true ? (
            <Trans>
              <Link to="/boot/image" className="underline">
                Build the boot image
              </Link>
              .
            </Trans>
          ) : (
            <Trans>
              To install Windows,{" "}
              <Link to="/boot/image" className="underline">
                build the boot image
              </Link>
              .
            </Trans>
          )}
        </ChecklistStep>
        <ChecklistStep done={checklist.image}>
          <Trans>
            <Link to="/library/images" className="underline">
              Add a Windows image
            </Link>
            .
          </Trans>
        </ChecklistStep>
        <ChecklistStep done={checklist.sequence}>
          <Trans>
            <Link to="/deployment/sequences" className="underline">
              Make a task sequence
            </Link>{" "}
            from the Install Windows template.
          </Trans>
        </ChecklistStep>
        <ChecklistStep done={checklist.machine}>
          <Trans>Netboot a machine.</Trans>
        </ChecklistStep>
      </ol>
    </Panel>
  );
}
