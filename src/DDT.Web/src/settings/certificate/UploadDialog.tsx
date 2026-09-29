// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";
import { Notice } from "@/ui/Notice";
import { Tab, TabList, TabPanel, Tabs } from "@/ui/Tabs";

import { CertificateProof } from "./CertificateProof";
import { NewRootDialog } from "./NewRootDialog";
import { PemField } from "./PemField";
import { PfxFields } from "./PfxFields";
import { useCertificateUploadForm } from "./useCertificateUploadForm";

export function UploadDialog({ isOpen, onClose }: { isOpen: boolean; onClose: () => void }) {
  const { t } = useLingui();
  const form = useCertificateUploadForm(onClose);
  const action = form.action;
  const host = window.location.hostname;

  return (
    <>
      <Dialog
        isOpen={isOpen}
        onOpenChange={(next) => {
          if (!next) {
            form.close();
          }
        }}
        title={<Trans>Upload a certificate</Trans>}
        width="lg"
        isBusy={action.busy}
        footer={
          <>
            <Button onPress={form.close} isDisabled={action.busy}>
              <Trans>Cancel</Trans>
            </Button>
            <Button
              variant="primary"
              onPress={action.start}
              isDisabled={action.busy || !form.ready}
            >
              <Trans>Upload certificate</Trans>
            </Button>
          </>
        }
      >
        <p>
          <Trans>
            The certificate has to be valid now and name {host}, the name this page is reached by,
            and every server name. It is served at once, on trial until you keep it.
          </Trans>
        </p>
        <Tabs
          selectedKey={form.format}
          onSelectionChange={(key) => {
            form.setFormat(key === "pfx" ? "pfx" : "pem");
          }}
        >
          <TabList aria-label={t`Format`}>
            <Tab id="pem">
              <Trans>PEM files</Trans>
            </Tab>
            <Tab id="pfx">
              <Trans>PFX file</Trans>
            </Tab>
          </TabList>
          <TabPanel id="pem" className="flex flex-col gap-3">
            <PemField
              label={<Trans>Certificate, followed by its intermediates</Trans>}
              chooseLabel={t`Load a file with the certificate`}
              value={form.certificatePem}
              onChange={form.setCertificatePem}
              errors={form.errorsOf("certificatePem")}
            />
            <PemField
              label={<Trans>Private key</Trans>}
              chooseLabel={t`Load a file with the private key`}
              value={form.keyPem}
              onChange={form.setKeyPem}
              errors={form.errorsOf("keyPem")}
            />
          </TabPanel>
          <TabPanel id="pfx" className="flex flex-col gap-3">
            <PfxFields
              fileName={form.pfx?.name ?? null}
              onFile={form.setPfx}
              errors={form.errorsOf("pfx")}
              password={form.pfxPassword}
              onPasswordChange={form.setPfxPassword}
            />
          </TabPanel>
        </Tabs>
        {form.general === null ? null : <Notice tone="fail">{form.general}</Notice>}
      </Dialog>
      <CertificateProof action={action} confirmLabel={<Trans>Confirm and upload</Trans>} />
      <NewRootDialog action={action} generating={false} />
    </>
  );
}
