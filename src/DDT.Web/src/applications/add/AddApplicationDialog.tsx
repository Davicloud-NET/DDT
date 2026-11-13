// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { IconPackage, IconTerminal2 } from "@tabler/icons-react";

import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";

import {
  newApplicationEdit,
  uploadedApplicationEdit,
  type ApplicationEdit,
} from "../applicationEdit";
import { ApplicationUploadPanel } from "./ApplicationUploadPanel";

interface AddApplicationDialogProps {
  onChosen: (seed: ApplicationEdit) => void;
  onClose: () => void;
}

// A new application starts with how it installs: uploaded files, a winget package, or only a command.
export function AddApplicationDialog({ onChosen, onClose }: AddApplicationDialogProps) {
  return (
    <Dialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={<Trans>Add application</Trans>}
      footer={
        <Button variant="secondary" onPress={onClose}>
          <Trans>Cancel</Trans>
        </Button>
      }
    >
      <div className="flex flex-col gap-3">
        <ApplicationUploadPanel
          onUploaded={(upload) => {
            onChosen(uploadedApplicationEdit(upload));
          }}
        />
        <div className="flex flex-wrap gap-2">
          <Button
            onPress={() => {
              onChosen(newApplicationEdit("Winget"));
            }}
          >
            <IconPackage aria-hidden="true" size={14} stroke={2} />
            <Trans>winget package</Trans>
          </Button>
          <Button
            onPress={() => {
              onChosen(newApplicationEdit("Command"));
            }}
          >
            <IconTerminal2 aria-hidden="true" size={14} stroke={2} />
            <Trans>Command only</Trans>
          </Button>
        </div>
      </div>
    </Dialog>
  );
}
