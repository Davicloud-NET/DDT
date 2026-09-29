// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";

import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";
import { Skeleton } from "@/ui/Skeleton";

import { directoryQuery } from "../users";
import { DirectoryOn } from "./DirectoryOn";

// The directory sign-in's group map, a group search and a user check, both using DDT's bind account. The server answers
// 409 while the directory is off or incomplete and 502 when it's unreachable, with the reason as the message.
export function DirectoryPanel() {
  const directory = useQuery(directoryQuery);

  return (
    <Panel title={<Trans>Directory groups</Trans>}>
      {directory.isPending ? (
        <>
          <Skeleton className="h-5 w-1/3" />
          <Skeleton className="h-5 w-1/2" />
        </>
      ) : directory.isError ? (
        <Notice tone="fail" title={<Trans>The directory settings could not be loaded.</Trans>}>
          {directory.error.message}
        </Notice>
      ) : directory.data.enabled ? (
        <DirectoryOn directory={directory.data} />
      ) : (
        <p className="max-w-[80ch] text-ink-2">
          <Trans>
            Sign-in through a directory is off. It is turned on and set up on the{" "}
            <Link to="/admin/sign-in" className="font-semibold text-ink underline">
              Sign-in
            </Link>{" "}
            page.
          </Trans>
        </p>
      )}
    </Panel>
  );
}
