// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";

import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";
import { Skeleton } from "@/ui/Skeleton";

import { consoleLogoQuery } from "./consoleLogo";
import { LogoChange } from "./logo/LogoChange";
import { LogoDetails } from "./logo/LogoDetails";
import { LogoPreview } from "./logo/LogoPreview";

// The organisation's logo in the header of the console on the machine. A change applies at once and reaches machines at
// their next registration. Operators can see it, and administrators can change it.
export function ConsoleLogoPanel({ canChange }: { canChange: boolean }) {
  const logo = useQuery(consoleLogoQuery);

  return (
    <Panel title={<Trans>Logo on the console</Trans>}>
      <p className="max-w-[80ch] text-ink-2">
        <Trans>
          The console at the machine shows this logo at the right end of its header, in Windows PE
          and in DDT&apos;s session, at most 32 pixels high and 200 wide. The header is dark in both
          of the console&apos;s themes, so a logo in white or light colours on a transparent
          background suits it. Machines take a new logo when they next register.
        </Trans>
      </p>
      {logo.isError ? (
        <Notice tone="fail">
          <Trans>The console&apos;s logo could not be read.</Trans>
        </Notice>
      ) : logo.data === undefined ? (
        <Skeleton className="h-32 w-full" />
      ) : (
        <>
          <LogoPreview view={logo.data} />
          <LogoDetails view={logo.data} />
          {canChange ? <LogoChange view={logo.data} /> : null}
        </>
      )}
    </Panel>
  );
}
