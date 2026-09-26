// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useRouter, type ErrorComponentProps } from "@tanstack/react-router";

import { ApiError } from "@/lib/api";
import { Button } from "@/ui/Button";

import { StandaloneHeader } from "./StandaloneHeader";

// Shown when a page cannot load at all, most often because the server does not answer. Trying again reloads the
// route, so a server that is back takes the person straight to where they were going.
export function RouteError({ error }: ErrorComponentProps) {
  const router = useRouter();
  const unreachable = !(error instanceof ApiError) || error.status >= 500;

  return (
    <div className="flex min-h-full flex-col">
      <StandaloneHeader />
      <main className="flex justify-center px-4 pt-[12vh]">
        <div className="flex w-full max-w-120 flex-col gap-4 rounded-panel bg-panel p-7 shadow-panel">
          <h1 className="type-title">
            {unreachable ? (
              <Trans>The server does not answer</Trans>
            ) : (
              <Trans>This page could not load</Trans>
            )}
          </h1>
          <p className="text-ink-2">
            {unreachable ? (
              <Trans>
                DDT could not reach its server. Check that the server is running and that this
                computer can reach it, then try again.
              </Trans>
            ) : (
              <Trans>
                Something went wrong while loading this page. Try again; if it keeps failing, the
                server log says why.
              </Trans>
            )}
          </p>
          <Button
            variant="primary"
            className="self-start"
            onPress={() => {
              void router.invalidate();
            }}
          >
            <Trans>Try again</Trans>
          </Button>
        </div>
      </main>
    </div>
  );
}
