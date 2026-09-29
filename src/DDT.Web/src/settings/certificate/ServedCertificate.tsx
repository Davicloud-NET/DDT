// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useState } from "react";

import { useNow } from "@/lib/useNow";
import { Button } from "@/ui/Button";
import { Panel } from "@/ui/Panel";

import type { CertificateView } from "../certificate";
import type { ServerSetting } from "../settings";

import { CertificateFacts } from "./CertificateFacts";
import { GenerateDialog } from "./GenerateDialog";
import { NotManageable } from "./NotManageable";
import { UploadDialog } from "./UploadDialog";

export function ServedCertificate({
  view,
  configuration,
}: {
  view: CertificateView;
  configuration: ServerSetting[] | null;
}) {
  const now = useNow(60_000);
  const [open, setOpen] = useState<"generate" | "upload" | null>(null);
  const served = view.served;
  const close = () => {
    setOpen(null);
  };

  return (
    <Panel
      title={<Trans>Served certificate</Trans>}
      actions={
        view.manageable ? (
          <span className="flex flex-wrap justify-end gap-2">
            <Button
              size="sm"
              isDisabled={!view.canGenerate}
              onPress={() => {
                setOpen("generate");
              }}
            >
              <Trans>Generate certificate</Trans>
            </Button>
            <Button
              size="sm"
              onPress={() => {
                setOpen("upload");
              }}
            >
              <Trans>Upload certificate</Trans>
            </Button>
          </span>
        ) : null
      }
    >
      {served === null ? null : <CertificateFacts view={view} now={now} />}

      {view.manageable ? (
        <div className="flex max-w-[80ch] flex-col gap-2 type-small text-ink-2">
          <p>
            <Trans>
              A new certificate is served at once, on trial: this page's next request gets it, and
              you keep it here. If nobody keeps it within 5 minutes, DDT goes back to the
              certificate before.
            </Trans>
          </p>
          <p>
            <Trans>
              Machines and browsers that trust DDT's root keep trusting any certificate issued from
              it, so Generate needs nothing else. A certificate from another root has to be trusted
              anew: every boot image has to be built again with that root, and every browser that
              manages DDT has to trust it.
            </Trans>
          </p>
          {view.canGenerate ? null : (
            <p>
              <Trans>
                DDT:Https:GenerateSelfSignedCertificate is false in configuration, so DDT issues no
                certificate here. Upload one instead.
              </Trans>
            </p>
          )}
        </div>
      ) : (
        <NotManageable view={view} configuration={configuration} />
      )}

      <GenerateDialog view={view} isOpen={open === "generate"} onClose={close} />
      <UploadDialog isOpen={open === "upload"} onClose={close} />
    </Panel>
  );
}
