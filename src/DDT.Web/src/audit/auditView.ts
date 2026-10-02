// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n, type MessageDescriptor } from "@lingui/core";
import { msg } from "@lingui/core/macro";

import type { AuditActorKind } from "./audit";

// The action groups of the audit log's filter. Each matches the start of an action name.
export const actionGroups: { prefix: string; label: MessageDescriptor }[] = [
  { prefix: "", label: msg`Every action` },
  { prefix: "machine.", label: msg`Machines` },
  { prefix: "deployment.", label: msg`Runs` },
  { prefix: "sequence.", label: msg`Task sequences` },
  { prefix: "rule.", label: msg`Rules` },
  { prefix: "role.", label: msg`Machine roles` },
  { prefix: "account.", label: msg`Accounts for steps` },
  { prefix: "image.", label: msg`OS images` },
  { prefix: "package.", label: msg`Packages` },
  { prefix: "boot-image.", label: msg`Boot image` },
  { prefix: "user.", label: msg`Users` },
  { prefix: "token.", label: msg`API tokens` },
  { prefix: "settings.", label: msg`Settings` },
  { prefix: "certificate.", label: msg`Certificates` },
  { prefix: "domain.", label: msg`Domain join` },
];

const actionLabels: Record<string, MessageDescriptor> = {
  "machine.registered": msg`Machine registered`,
  "machine.reregistered": msg`Machine registered again`,
  "machine.signed-in": msg`Signed in at a machine`,
  "machine.approved": msg`Machine approved`,
  "machine.rejected": msg`Machine rejected`,
  "machine.removed": msg`Machine removed`,
  "deployment.assigned": msg`Sequence assigned`,
  "deployment.cancelled": msg`Run stopped`,
  "deployment.started": msg`Run started`,
  "deployment.resumed": msg`Run went on`,
  "deployment.done": msg`Run done`,
  "deployment.failed": msg`Run failed`,
  "deployment.secret-read": msg`Password read by a run`,
  "deployment.run-token-refused": msg`Run token refused`,
  "sequence.created": msg`Sequence created`,
  "sequence.changed": msg`Sequence changed`,
  "sequence.deleted": msg`Sequence deleted`,
  "rule.created": msg`Rule added`,
  "rule.changed": msg`Rule changed`,
  "rule.deleted": msg`Rule deleted`,
  "rule.reordered": msg`Rules reordered`,
  "role.created": msg`Machine role added`,
  "role.changed": msg`Machine role changed`,
  "role.deleted": msg`Machine role deleted`,
  "account.created": msg`Account for steps added`,
  "account.changed": msg`Account for steps changed`,
  "account.deleted": msg`Account for steps deleted`,
  "account.refused": msg`Account for steps not saved`,
  "image.uploaded": msg`Image uploaded`,
  "image.import-started": msg`Import from the server started`,
  "image.deleted": msg`Image deleted`,
  "package.uploaded": msg`Package uploaded`,
  "package.changed": msg`Package changed`,
  "package.deleted": msg`Package deleted`,
  "boot-image.build-started": msg`Boot image build started`,
  "boot-image.build-chosen": msg`Boot image build chosen to serve`,
  "boot-image.adk-install-started": msg`Windows ADK install started`,
  "boot-image.builder-downloaded": msg`Boot image builder downloaded`,
  "boot-image.uploaded": msg`Boot image uploaded by a builder`,
  "user.created": msg`Account created`,
  "user.changed": msg`Account changed`,
  "user.disabled": msg`Account disabled`,
  "user.enabled": msg`Account enabled`,
  "user.deleted": msg`Account deleted`,
  "user.password-reset": msg`Password reset`,
  "user.two-factor-reset": msg`Second factor reset`,
  "token.created": msg`API token made`,
  "token.revoked": msg`API token revoked`,
  "certificate.anchor-acknowledged": msg`Certificate change acknowledged`,
  "domain.join-checked": msg`Domain join checked`,
};

// A known action gets a translated label. An action this page doesn't know yet shows the server's name for it.
export function actionLabel(action: string): string {
  const label = actionLabels[action];

  return label === undefined ? action : i18n._(label);
}

export function actorKindLabel(kind: AuditActorKind): string {
  switch (kind) {
    case "User":
      return i18n._(msg`Person`);
    case "Machine":
      return i18n._(msg`Machine`);
    case "Token":
      return i18n._(msg`API token`);
    case "System":
      return i18n._(msg`DDT`);
  }
}
