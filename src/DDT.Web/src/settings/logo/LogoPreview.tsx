// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Logo } from "@/ui/Logo";

import { consoleLogoImage, type ConsoleLogoView } from "../consoleLogo";

// The console's header as it looks with the logo, on the header's own colour.
export function LogoPreview({ view }: { view: ConsoleLogoView }) {
  const { t: translate } = useLingui();

  return (
    <div
      role="img"
      aria-label={translate`The console's header with the logo`}
      className="flex h-14 items-center gap-3 overflow-hidden rounded-panel bg-frame px-6 text-frame-text"
    >
      <Logo size={22} />
      <span className="type-wordmark">DDT</span>
      <span className="flex-1" />
      <span className="truncate type-small text-frame-muted max-sm:hidden">
        <Trans>Connected to the server</Trans>
      </span>
      {view.sha256 === null ? (
        <span className="type-small text-frame-muted">
          <Trans>No logo</Trans>
        </span>
      ) : (
        <>
          <span aria-hidden="true" className="mx-2 h-6 w-px bg-frame-line" />
          <img
            src={consoleLogoImage(view.sha256)}
            alt=""
            className="max-h-8 max-w-[200px] object-contain"
          />
        </>
      )}
    </div>
  );
}
