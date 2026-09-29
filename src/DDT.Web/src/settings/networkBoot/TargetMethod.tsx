// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Button } from "@/ui/Button";

// The method the architecture decides, and a way to switch back to it while the stored method differs.
export function TargetMethod({
  method,
  stored,
  errors,
  editable,
  onUse,
}: {
  method: "Tftp" | "Http";
  stored: string | null;
  errors: string[];
  editable: boolean;
  onUse: () => void;
}) {
  const wrongMethod = stored?.toLowerCase() !== method.toLowerCase();

  return (
    <div className="flex flex-wrap items-center gap-3">
      <span className="type-small text-ink-2">
        {method === "Http" ? (
          <Trans>Its firmware loads the boot file over HTTP, from a URL.</Trans>
        ) : (
          <Trans>Its firmware loads the boot file over TFTP.</Trans>
        )}
      </span>
      {wrongMethod ? (
        <>
          <span className="type-small text-fail-text">
            {errors.length > 0 ? (
              errors.join(" ")
            ) : (
              <Trans>The stored method does not suit this architecture.</Trans>
            )}
          </span>
          {editable ? (
            <Button
              size="sm"
              onPress={() => {
                onUse();
              }}
            >
              {method === "Http" ? <Trans>Use HTTP</Trans> : <Trans>Use TFTP</Trans>}
            </Button>
          ) : null}
        </>
      ) : null}
    </div>
  );
}
