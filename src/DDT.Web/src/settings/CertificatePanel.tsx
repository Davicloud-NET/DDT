// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";
import { FileTrigger } from "react-aria-components";

import { formattingLocale } from "@/i18n/i18n";
import { ApiError } from "@/lib/api";
import { fullTime, relativeTimeAhead } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";
import { Facts, Panel, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { Tab, TabList, TabPanel, Tabs } from "@/ui/Tabs";
import { TextField } from "@/ui/TextField";

import {
  base64Of,
  certificateQuery,
  confirmCertificate,
  generateCertificate,
  putCertificate,
  uploadCertificate,
  type CertificateSettings,
  type CertificateView,
} from "./certificate";
import { settingsOverviewQuery, type SettingsOverview } from "./settings";
import { ReauthDialog, SettingLines, SettingsSection } from "./SettingsParts";
import { useGuardedAction, type GuardedAction } from "./useGuardedAction";
import { useSettingsForm } from "./useSettingsForm";

// The certificate the server serves, the names it has to carry, and the two ways to replace it: Generate issues one
// from DDT's root, Upload installs one the administrator brings. A new pair is served at once but on trial, and DDT
// goes back to the pair before unless it is confirmed from a connection that was served it, so a certificate the
// browser refuses cannot lock anybody out.
export function CertificatePanel() {
  const certificate = useQuery(certificateQuery);
  const overview = useQuery(settingsOverviewQuery);

  if (certificate.isError) {
    return (
      <Notice tone="fail">
        <Trans>The server certificate could not be read.</Trans>
      </Notice>
    );
  }

  if (certificate.data === undefined) {
    return <Skeleton className="h-64 w-full" />;
  }

  const view = certificate.data;

  return (
    <>
      {view.provisionalUntil !== null ? (
        <Provisional key={view.provisionalUntil} until={view.provisionalUntil} />
      ) : null}
      <Served view={view} configuration={overview.data?.server ?? null} />
      <ServerNames />
    </>
  );
}

function Provisional({ until }: { until: string }) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const now = useNow(5_000);
  const deadline = new Date(until).toLocaleTimeString(formattingLocale());
  const left = relativeTimeAhead(until, now);
  const confirm = useMutation({
    mutationFn: confirmCertificate,
    onSuccess: (answer) => {
      putCertificate(queryClient, answer);
    },
  });

  return (
    <Notice
      tone="attention"
      title={<Trans>The new certificate is on trial until {deadline}</Trans>}
      actions={
        <Button
          size="sm"
          variant="primary"
          isDisabled={confirm.isPending}
          onPress={() => {
            confirm.mutate();
          }}
        >
          <Trans>Keep the new certificate</Trans>
        </Button>
      }
    >
      <Trans>
        This page's next request gets the new certificate. Once the page works with it, keep it
        here. If nobody keeps it by {deadline}, {left}, DDT goes back to the certificate before, so
        a certificate your browser refuses cannot lock you out.
      </Trans>
      {confirm.isError ? (
        <span className="mt-1.5 block font-semibold">
          {confirm.error instanceof ApiError
            ? confirm.error.message
            : t`The server could not be reached with the new certificate, so this browser probably does not trust it. At ${deadline} DDT goes back to the certificate before.`}
        </span>
      ) : null}
    </Notice>
  );
}

function Served({
  view,
  configuration,
}: {
  view: CertificateView;
  configuration: SettingsOverview["server"] | null;
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

function CertificateFacts({ view, now }: { view: CertificateView; now: number }) {
  const served = view.served;

  if (served === null) {
    return null;
  }

  const root = served.rootSubject;
  const renews = served.renewsUtc === null ? null : relativeTimeAhead(served.renewsUtc, now);
  const expires = relativeTimeAhead(served.notAfter, now);
  const until = fullTime(served.notAfter);

  return (
    <Facts
      items={[
        { label: <Trans>Subject</Trans>, value: served.subject, mono: true },
        { label: <Trans>Names</Trans>, value: served.names.join(", "), mono: true },
        {
          label: <Trans>Issued by</Trans>,
          value:
            served.managedByDdt && root !== null ? (
              <Trans>DDT's root, {root}</Trans>
            ) : (
              <Trans>Another root: an administrator's certificate, which DDT does not renew</Trans>
            ),
        },
        ...(served.rootSha256 === null
          ? []
          : [{ label: <Trans>Root SHA-256</Trans>, value: served.rootSha256, mono: true }]),
        { label: <Trans>SHA-256</Trans>, value: served.sha256, mono: true },
        {
          label: <Trans>Valid until</Trans>,
          value: (
            <>
              {until} <span className="text-muted">({expires})</span>
            </>
          ),
        },
        ...(renews === null
          ? []
          : [{ label: <Trans>Renewal</Trans>, value: <Trans>DDT renews it {renews}.</Trans> }]),
        ...(view.servedHere === null
          ? []
          : [
              {
                label: <Trans>This page</Trans>,
                value: view.servedHere ? (
                  <Trans>Was served this certificate.</Trans>
                ) : (
                  <Trans>Was served the certificate before; its next request gets this one.</Trans>
                ),
              },
            ]),
      ]}
    />
  );
}

// Why the page cannot change the certificate, from what configuration says about its files.
function NotManageable({
  view,
  configuration,
}: {
  view: CertificateView;
  configuration: SettingsOverview["server"] | null;
}) {
  const isSet = (key: string) =>
    configuration?.find((setting) => setting.key === key)?.isSet === true;
  let reason: ReactNode;

  if (configuration === null) {
    reason = view.notManageable;
  } else if (isSet("Kestrel:Certificates:Default:Password")) {
    reason = (
      <Trans>
        Kestrel:Certificates:Default:Password is set in configuration, so the certificate is managed
        by hand and this page only reads it. To manage it here, keep the certificate and its key as
        PEM files without a password, name them in Kestrel:Certificates:Default:Path and KeyPath,
        remove the password, and restart DDT.
      </Trans>
    );
  } else if (
    !isSet("Kestrel:Certificates:Default:Path") ||
    !isSet("Kestrel:Certificates:Default:KeyPath")
  ) {
    reason = (
      <Trans>
        Kestrel:Certificates:Default:Path and KeyPath are not both set in configuration, so DDT has
        no files to keep a certificate in. Name the PEM files there, where DDT may write, and
        restart DDT to manage the certificate here.
      </Trans>
    );
  } else {
    reason = view.notManageable;
  }

  return (
    <Notice tone="attention" title={<Trans>This page cannot change the certificate</Trans>}>
      {reason}
    </Notice>
  );
}

// Issuing needs the password, and making a new root needs a confirmation as well.
function GenerateDialog({
  view,
  isOpen,
  onClose,
}: {
  view: CertificateView;
  isOpen: boolean;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const action = useGuardedAction({
    send: generateCertificate,
    onDone: (answer) => {
      putCertificate(queryClient, answer);
      onClose();
    },
  });

  const close = () => {
    action.reset();
    onClose();
  };

  return (
    <>
      <Dialog
        isOpen={isOpen}
        onOpenChange={(next) => {
          if (!next) {
            close();
          }
        }}
        title={<Trans>Generate a new certificate?</Trans>}
        isBusy={action.busy}
        footer={
          <>
            <Button onPress={close} isDisabled={action.busy}>
              <Trans>Cancel</Trans>
            </Button>
            <Button variant="primary" onPress={action.start} isDisabled={action.busy}>
              <Trans>Generate certificate</Trans>
            </Button>
          </>
        }
      >
        <p>
          <Trans>
            DDT issues a certificate from its root for localhost, this computer's name and
            addresses, and the server names below, and serves it at once, on trial until you keep
            it.
          </Trans>
        </p>
        {view.hasRoot ? null : (
          <p>
            <Trans>
              DDT has no root yet, so it makes one. Every boot image then has to be built again with
              it, and every browser that manages DDT has to trust it.
            </Trans>
          </p>
        )}
        {action.error === null ? null : <Notice tone="fail">{action.error}</Notice>}
      </Dialog>
      <CertificateProof action={action} confirmLabel={<Trans>Confirm and generate</Trans>} />
      <NewRootDialog action={action} generating />
    </>
  );
}

type Format = "pem" | "pfx";

function UploadDialog({ isOpen, onClose }: { isOpen: boolean; onClose: () => void }) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const [format, setFormat] = useState<Format>("pem");
  const [certificatePem, setCertificatePem] = useState("");
  const [keyPem, setKeyPem] = useState("");
  const [pfx, setPfx] = useState<File | null>(null);
  const [pfxPassword, setPfxPassword] = useState("");

  // The key is kept no longer than the dialog is open.
  const forget = () => {
    setCertificatePem("");
    setKeyPem("");
    setPfx(null);
    setPfxPassword("");
  };

  const action = useGuardedAction({
    send: async (confirm: string[]) =>
      uploadCertificate(
        format === "pfx" && pfx !== null
          ? { pfx: await base64Of(pfx), pfxPassword: pfxPassword === "" ? null : pfxPassword }
          : { certificatePem, keyPem },
        confirm,
      ),
    onDone: (answer) => {
      putCertificate(queryClient, answer);
      forget();
      onClose();
    },
  });

  const close = () => {
    action.reset();
    forget();
    onClose();
  };

  const refusal = action.refusal;
  const fields = refusal?.kind === "invalid" ? refusal.fields : {};
  const errorsOf = (field: string) => fields[field] ?? [];
  // A refusal that is not about one of the fields below.
  const general =
    refusal === null
      ? null
      : refusal.kind === "invalid"
        ? (fields.certificate ?? []).join(" ") || null
        : refusal.message;
  const ready =
    format === "pem" ? certificatePem.trim() !== "" && keyPem.trim() !== "" : pfx !== null;
  const host = window.location.hostname;
  const pfxName = pfx?.name ?? null;

  return (
    <>
      <Dialog
        isOpen={isOpen}
        onOpenChange={(next) => {
          if (!next) {
            close();
          }
        }}
        title={<Trans>Upload a certificate</Trans>}
        width="lg"
        isBusy={action.busy}
        footer={
          <>
            <Button onPress={close} isDisabled={action.busy}>
              <Trans>Cancel</Trans>
            </Button>
            <Button variant="primary" onPress={action.start} isDisabled={action.busy || !ready}>
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
          selectedKey={format}
          onSelectionChange={(key) => {
            setFormat(key === "pfx" ? "pfx" : "pem");
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
              value={certificatePem}
              onChange={setCertificatePem}
              errors={errorsOf("certificatePem")}
            />
            <PemField
              label={<Trans>Private key</Trans>}
              chooseLabel={t`Load a file with the private key`}
              value={keyPem}
              onChange={setKeyPem}
              errors={errorsOf("keyPem")}
            />
          </TabPanel>
          <TabPanel id="pfx" className="flex flex-col gap-3">
            <span className="flex flex-wrap items-center gap-3">
              <FileTrigger
                acceptedFileTypes={[".pfx", ".p12"]}
                onSelect={(files) => {
                  setPfx(files?.[0] ?? null);
                }}
              >
                <Button size="sm">
                  <Trans>Choose a PFX file</Trans>
                </Button>
              </FileTrigger>
              <span className="type-small text-ink-2">
                {pfxName ?? <Trans>No file chosen</Trans>}
              </span>
            </span>
            {errorsOf("pfx").length > 0 ? (
              <span className="type-small text-fail-text">{errorsOf("pfx").join(" ")}</span>
            ) : null}
            <TextField
              label={<Trans>PFX password</Trans>}
              type="password"
              autoComplete="off"
              value={pfxPassword}
              onChange={setPfxPassword}
              className="max-w-80"
            />
          </TabPanel>
        </Tabs>
        {general === null ? null : <Notice tone="fail">{general}</Notice>}
      </Dialog>
      <CertificateProof action={action} confirmLabel={<Trans>Confirm and upload</Trans>} />
      <NewRootDialog action={action} generating={false} />
    </>
  );
}

function PemField({
  label,
  chooseLabel,
  value,
  onChange,
  errors,
}: {
  label: ReactNode;
  chooseLabel: string;
  value: string;
  onChange: (value: string) => void;
  errors: string[];
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <TextField
        label={label}
        multiline
        rows={5}
        mono
        spellCheck="false"
        autoComplete="off"
        placeholder="-----BEGIN ..."
        value={value}
        onChange={onChange}
        isInvalid={errors.length > 0}
        errorMessage={errors.join(" ")}
      />
      <span>
        <FileTrigger
          acceptedFileTypes={[".pem", ".crt", ".cer", ".key"]}
          onSelect={(files) => {
            const file = files?.[0];

            if (file !== undefined) {
              void file.text().then(onChange);
            }
          }}
        >
          <Button size="sm" variant="quiet" aria-label={chooseLabel}>
            <Trans>Load a file</Trans>
          </Button>
        </FileTrigger>
      </span>
    </div>
  );
}

function CertificateProof({
  action,
  confirmLabel,
}: {
  action: GuardedAction;
  confirmLabel: ReactNode;
}) {
  return (
    <ReauthDialog
      isOpen={action.needsReauth}
      onAccepted={action.retryAfterReauth}
      onCancel={action.cancelReauth}
      confirmLabel={confirmLabel}
      reason={
        <Trans>
          The server certificate decides what browsers and machines trust, so replacing it needs
          your password again.
        </Trans>
      }
    />
  );
}

// certificate.newRoot: boot images pin DDT's root, so a pair from another root, or a new root, strands every
// netbooting machine until its boot image is built again.
function NewRootDialog({ action, generating }: { action: GuardedAction; generating: boolean }) {
  return (
    <Dialog
      isOpen={action.warnings !== null}
      onOpenChange={(next) => {
        if (!next) {
          action.cancelWarnings();
        }
      }}
      title={
        generating ? (
          <Trans>Make a new root?</Trans>
        ) : (
          <Trans>Install a certificate from another root?</Trans>
        )
      }
      footer={
        <>
          <Button onPress={action.cancelWarnings}>
            <Trans>Cancel</Trans>
          </Button>
          <Button variant="primary" onPress={action.confirmWarnings}>
            {generating ? <Trans>Make a new root</Trans> : <Trans>Install it anyway</Trans>}
          </Button>
        </>
      }
    >
      <p>
        {generating ? (
          <Trans>
            DDT has no root yet, so Generate makes one. Boot images pin the root, so every boot
            image has to be built again with the new one, and every browser that manages DDT has to
            trust it. Until then, netbooting machines cannot reach the server.
          </Trans>
        ) : (
          <Trans>
            This certificate does not come from DDT's root, which boot images pin. Every boot image
            has to be built again with its root, and every browser that manages DDT has to trust
            that root. Until then, netbooting machines cannot reach the server.
          </Trans>
        )}
      </p>
    </Dialog>
  );
}

// The names the server is reached by, which Generate issues for and an upload has to cover.
function ServerNames() {
  const form = useSettingsForm<CertificateSettings>("certificate");

  if (form.view === null) {
    return form.query.isError ? (
      <Notice tone="fail">
        <Trans>These settings could not be loaded.</Trans>
      </Notice>
    ) : (
      <Skeleton className="h-40 w-full" />
    );
  }

  return (
    <SettingsSection
      form={form}
      canChange
      title={<Trans>Server names</Trans>}
      description={
        <Trans>
          The names and addresses browsers and machines reach this server by. Generate issues the
          certificate for them, and an uploaded certificate has to name each of them, so a change
          takes effect with the next certificate. DDT's automatic renewal keeps every name of the
          certificate it renews.
        </Trans>
      }
    >
      <SettingLines
        form={form}
        field="subjectAlternativeNames"
        canChange
        label={<Trans>Names and addresses</Trans>}
        hint={
          <Trans>
            One per line, such as ddt.corp.example or 10.0.0.5. Generate adds localhost and this
            computer's name and addresses by itself.
          </Trans>
        }
      />
    </SettingsSection>
  );
}
