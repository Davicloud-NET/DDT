// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { useState } from "react";

import { currentUserQuery } from "@/auth/auth";
import { formattingLocale } from "@/i18n/i18n";
import { formatBytes } from "@/lib/format";
import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { liveListOptions } from "@/live/freshness";
import { useLiveStatus } from "@/live/useLiveStatus";
import { acknowledgeReplacedAnchor, serverCertificateQuery } from "@/server/serverCertificate";
import { Button } from "@/ui/Button";
import { EmptyState, Facts, Page, PageHeader, Panel, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { StateTag } from "@/ui/StateTag";

import { bootImageQuery, type BootImageView } from "./bootImage";

// The Windows PE boot image machines netboot into: what the last build put into it, whether the drivers flagged for
// Windows PE have changed since, and how to build it again. The build runs on a Windows machine with the ADK, so the
// page gives the command rather than building it.
export function BootImagePage() {
  const live = useLiveStatus();
  const boot = useQuery({ ...bootImageQuery, ...liveListOptions(live) });
  const user = useQuery(currentUserQuery).data ?? null;
  const administrator = user?.roles.includes("Administrator") === true;
  const now = useNow(60_000);

  return (
    <Page className="max-w-[72rem]">
      <PageHeader title={<Trans>Boot image</Trans>} />

      {administrator ? <ReplacedAnchor /> : null}

      {boot.isError ? (
        <Notice tone="fail">
          <Trans>The state of the boot image could not be read.</Trans>
        </Notice>
      ) : null}

      {boot.isPending ? (
        <Panel>
          <Skeleton className="h-6 w-1/2" />
          <Skeleton className="h-6 w-2/3" />
        </Panel>
      ) : boot.data !== undefined ? (
        <>
          <State view={boot.data} />
          <div className="grid items-start gap-4 lg:grid-cols-2">
            <LastBuild view={boot.data} now={now} />
            <Drivers view={boot.data} />
          </div>
          <BuildCommand view={boot.data} />
        </>
      ) : null}
    </Page>
  );
}

function State({ view }: { view: BootImageView }) {
  if (!view.stale) {
    return view.build === null ? null : (
      <Notice tone="info">
        <Trans>The boot image has every driver flagged for Windows PE.</Trans>
      </Notice>
    );
  }

  return (
    <Notice tone="attention">
      {view.build === null ? (
        <Trans>
          Drivers are flagged for Windows PE, but no boot image built with this version of the build
          script is in the boot directory. Build it again to add them.
        </Trans>
      ) : (
        <Trans>
          The drivers flagged for Windows PE have changed since the boot image was built. Build it
          again so that machines netboot with them.
        </Trans>
      )}
    </Notice>
  );
}

function LastBuild({ view, now }: { view: BootImageView; now: number }) {
  const build = view.build;
  const unknown = t`Not recorded`;

  return (
    <Panel title={<Trans>Last build</Trans>}>
      {build === null ? (
        <p className="text-ink-2">
          <Trans>
            The boot directory holds no record of a build. A build with the current script writes
            ddt-boot-image.json next to boot.wim, which DDT reads here.
          </Trans>
        </p>
      ) : (
        <Facts
          items={[
            {
              label: <Trans>Built</Trans>,
              value: (
                <span title={new Date(build.builtUtc).toLocaleString(formattingLocale())}>
                  {relativeTime(build.builtUtc, now)}
                </span>
              ),
            },
            { label: <Trans>Drivers</Trans>, value: String(build.drivers.length) },
            { label: <Trans>ADK</Trans>, value: build.adkVersion ?? unknown },
            { label: <Trans>Boot manager</Trans>, value: build.bootManager ?? unknown },
            { label: <Trans>Agent</Trans>, value: build.agentVersion ?? unknown },
          ]}
        />
      )}
    </Panel>
  );
}

function Drivers({ view }: { view: BootImageView }) {
  const built = new Set(
    view.build?.drivers.map((driver) => `${driver.packageId} ${driver.sha256}`),
  );

  return (
    <Panel title={<Trans>Drivers for Windows PE</Trans>} flush>
      {view.drivers.length === 0 ? (
        <EmptyState title={<Trans>No drivers flagged</Trans>}>
          <Trans>
            The boot image has Windows PE's own drivers only. Flag a driver package under{" "}
            <Link to="/library/drivers" className="underline">
              Library, Drivers
            </Link>{" "}
            when a machine cannot reach the network or see its disk in Windows PE.
          </Trans>
        </EmptyState>
      ) : (
        <ul className="flex flex-col">
          {view.drivers.map((driver) => (
            <li
              key={driver.packageId}
              className="flex items-center gap-3 border-b border-line-soft px-4 py-2.5 last:border-b-0"
            >
              <span className="flex min-w-0 flex-1 flex-col">
                <span className="truncate type-label text-ink">{driver.name}</span>
                <span className="truncate type-data text-muted">{driver.sha256.slice(0, 16)}</span>
              </span>
              <span className="type-small text-muted">{formatBytes(driver.sizeBytes)}</span>
              {built.has(`${driver.packageId} ${driver.sha256}`) ? (
                <StateTag tone="ok">
                  <Trans>In the image</Trans>
                </StateTag>
              ) : (
                <StateTag tone="attention">
                  <Trans>Not built yet</Trans>
                </StateTag>
              )}
            </li>
          ))}
        </ul>
      )}
    </Panel>
  );
}

// The command for the build machine, with this server's address. An API token lets the script download the flagged
// drivers; the root certificate is the one the agent pins.
function BuildCommand({ view }: { view: BootImageView }) {
  const { t: translate } = useLingui();
  const [copied, setCopied] = useState(false);
  const origin = window.location.origin;
  const drivers = view.drivers.length > 0;
  const command = [
    ".\\build\\Build-BootImage.ps1",
    "-AgentPath .\\artifacts\\agent\\ddt-agent.exe",
    `-ServerUrl ${origin}`,
    "-RootCertificatePath .\\ddt-root.pem",
    ...(drivers ? ["-ApiToken $env:DDT_API_TOKEN"] : []),
  ].join(" ");

  return (
    <Panel
      title={<Trans>Building it again</Trans>}
      actions={
        <Button
          size="sm"
          onPress={() => {
            void navigator.clipboard.writeText(command).then(() => {
              setCopied(true);
            });
          }}
        >
          {copied ? <Trans>Copied</Trans> : <Trans>Copy command</Trans>}
        </Button>
      }
    >
      <p className="text-ink-2">
        <Trans>
          Run this in an elevated PowerShell on a Windows machine with the Windows ADK, from a copy
          of DDT's repository. It writes boot.wim and the boot files into the destination, which the
          boot directory has to hold; machines netboot with the new image at once.
        </Trans>
      </p>
      <pre
        aria-label={translate`Build command`}
        className="overflow-x-auto rounded-key bg-console px-3.5 py-3 type-data whitespace-pre-wrap text-console-text"
      >
        {command}
      </pre>
      <ul className="flex list-disc flex-col gap-1 pl-5 type-small text-ink-2">
        <li>
          <Trans>
            The server address is this page's. Use the name machines reach the server by, if it is
            another.
          </Trans>
        </li>
        <li>
          <Trans>
            <a href="/api/about/root-certificate" className="underline">
              Download the root certificate
            </a>{" "}
            as ddt-root.pem. The agent trusts only the server certificates it signed.
          </Trans>
        </li>
        {drivers ? (
          <li>
            <Trans>
              The script downloads the flagged drivers with an API token of an administrator. Make
              one under Account and security, and put it into DDT_API_TOKEN.
            </Trans>
          </li>
        ) : null}
      </ul>
    </Panel>
  );
}

// After DDT replaced the self-signed certificate older boot images pin, it says so until an administrator confirms
// that every boot image was built again with its root.
function ReplacedAnchor() {
  const queryClient = useQueryClient();
  const certificate = useQuery(serverCertificateQuery).data ?? null;
  const acknowledge = useMutation({
    mutationFn: acknowledgeReplacedAnchor,
    onSuccess: () => {
      queryClient.setQueryData(serverCertificateQuery.queryKey, (current) =>
        current === undefined || current === null
          ? current
          : { ...current, anchorReplacedUtc: null },
      );
    },
  });

  if (certificate?.anchorReplacedUtc == null) {
    return null;
  }

  return (
    <Notice tone="attention">
      <span className="flex flex-col gap-2">
        <Trans>
          DDT replaced the self-signed server certificate that boot images built before it pin.
          Machines netbooting such an image cannot reach the server until it is built again with
          DDT's root certificate.
        </Trans>
        <span>
          <Button
            size="sm"
            isDisabled={acknowledge.isPending}
            onPress={() => {
              acknowledge.mutate();
            }}
          >
            <Trans>Every boot image is built again</Trans>
          </Button>
        </span>
      </span>
    </Notice>
  );
}
