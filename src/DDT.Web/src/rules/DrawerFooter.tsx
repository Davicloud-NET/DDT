// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import type { ReactNode } from "react";

import { Button } from "@/ui/Button";

interface DrawerFooterProps {
  saveLabel: ReactNode;
  deleteLabel: ReactNode;
  isSaveDisabled: boolean;
  // False for a new thing, and for one someone deleted meanwhile.
  canDelete: boolean;
  isBusy: boolean;
  onSave: () => void;
  onCancel: () => void;
  onDelete: () => void;
}

// The keys under the form of a rule's, a machine role's or an account's drawer, for someone who may change it.
export function DrawerFooter({
  saveLabel,
  deleteLabel,
  isSaveDisabled,
  canDelete,
  isBusy,
  onSave,
  onCancel,
  onDelete,
}: DrawerFooterProps) {
  return (
    <>
      <Button variant="primary" isDisabled={isSaveDisabled} onPress={onSave}>
        {saveLabel}
      </Button>
      <Button variant="secondary" isDisabled={isBusy} onPress={onCancel}>
        <Trans>Cancel</Trans>
      </Button>
      {canDelete ? (
        <Button
          variant="quiet"
          className="ml-auto text-fail-text hover:text-fail-text"
          isDisabled={isBusy}
          onPress={onDelete}
        >
          {deleteLabel}
        </Button>
      ) : null}
    </>
  );
}
