// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { SecretAction } from "@/settings/settings";
import { Button } from "@/ui/Button";

interface PasswordActionsProps {
  action: SecretAction;
  stored: boolean;
  onChange: (action: SecretAction) => void;
}

// The keys that replace, clear or keep a saved account's password.
export function PasswordActions({ action, stored, onChange }: PasswordActionsProps) {
  return (
    <span className="flex gap-2">
      {action.action === "Keep" ? (
        <>
          <Button
            size="sm"
            onPress={() => {
              onChange({ action: "Set", value: "" });
            }}
          >
            {stored ? <Trans>Replace</Trans> : <Trans>Set</Trans>}
          </Button>
          {stored ? (
            <Button
              size="sm"
              variant="quiet"
              onPress={() => {
                onChange({ action: "Clear" });
              }}
            >
              <Trans>Clear</Trans>
            </Button>
          ) : null}
        </>
      ) : (
        <Button
          size="sm"
          variant="quiet"
          onPress={() => {
            onChange({ action: "Keep" });
          }}
        >
          {stored ? <Trans>Keep the stored one</Trans> : <Trans>Leave it unset</Trans>}
        </Button>
      )}
    </span>
  );
}
