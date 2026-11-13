// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { DrawerFooter } from "@/rules/DrawerFooter";
import { DrawerNotices } from "@/rules/DrawerNotices";
import { DrawerTitle } from "@/rules/DrawerTitle";
import { Drawer } from "@/ui/Drawer";

import type { ApplicationEdit } from "./applicationEdit";
import type { ApplicationView } from "./applications";
import { AboutFields } from "./drawer/AboutFields";
import { DetectionField } from "./drawer/DetectionField";
import { InstallFields } from "./drawer/InstallFields";
import { OrderedPicker } from "./drawer/OrderedPicker";
import { RunFields } from "./drawer/RunFields";
import { useApplicationForm } from "./drawer/useApplicationForm";
import { UsedBySection } from "./drawer/UsedBySection";

interface ApplicationDrawerProps {
  // Null for a new application.
  application: ApplicationView | null;
  seed: ApplicationEdit;
  // The library, for the applications it may depend on.
  library: readonly ApplicationView[];
  onClose: () => void;
  onDelete: (application: ApplicationView) => void;
}

export function ApplicationDrawer(props: ApplicationDrawerProps) {
  const { application, seed, library, onClose, onDelete } = props;
  const { t } = useLingui();
  const form = useApplicationForm({ application, seed, onClose });
  const { base, edit, busy, theirs } = form;
  const who = theirs?.updatedBy ?? null;
  const others = library.filter((candidate) => candidate.id !== base?.id);

  return (
    <Drawer
      isOpen
      wide
      onOpenChange={(open) => {
        if (!open && !busy) {
          onClose();
        }
      }}
      title={
        <DrawerTitle over={t`Application`}>
          {base === null ? <Trans>New application</Trans> : base.name}
        </DrawerTitle>
      }
      footer={
        <DrawerFooter
          saveLabel={<Trans>Save application</Trans>}
          deleteLabel={<Trans>Delete application</Trans>}
          isSaveDisabled={busy || edit.name.trim() === "" || form.gone}
          canDelete={base !== null && !form.gone}
          isBusy={busy}
          onSave={form.submit}
          onCancel={onClose}
          onDelete={() => {
            if (base !== null) {
              onClose();
              onDelete(base);
            }
          }}
        />
      }
    >
      <DrawerNotices
        savedMeanwhile={
          theirs === null
            ? null
            : who === null
              ? t`Someone else saved this application while you were editing it.`
              : t`${who} saved this application while you were editing it.`
        }
        gone={
          form.gone ? (
            <Trans>Someone deleted this application while you were editing it.</Trans>
          ) : null
        }
        loose={form.loose}
        refused={form.refused}
        isBusy={busy}
        onTakeTheirs={form.takeTheirs}
        onKeepMine={form.keepMine}
      />

      <AboutFields form={form} />
      <InstallFields form={form} />
      <DetectionField
        detection={edit.detection}
        findings={form.findings}
        onChange={(detection) => {
          form.change({ detection });
        }}
      />
      <OrderedPicker
        label={<Trans>Depends on</Trans>}
        field="dependsOn"
        ids={edit.dependsOn}
        options={others}
        addLabel={t`Add a dependency`}
        findings={form.findings}
        onChange={(dependsOn) => {
          form.change({ dependsOn });
        }}
      />
      <RunFields form={form} />
      {base !== null ? <UsedBySection uses={base.usedBy} /> : null}
    </Drawer>
  );
}
