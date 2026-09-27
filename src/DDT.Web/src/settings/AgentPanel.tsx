// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconUpload } from "@tabler/icons-react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";
import { DropZone, FileTrigger, Text, type FileDropItem } from "react-aria-components";

import { formatBytes } from "@/lib/format";
import { fullTime, relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { Button } from "@/ui/Button";
import { ProgressBar } from "@/ui/Controls";
import { cx } from "@/ui/cx";
import { ConfirmDialog } from "@/ui/Dialog";
import { Facts, Panel, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";

import {
  agentBinaryQuery,
  consoleBinaryQuery,
  maxAgentBytes,
  maxConsoleBytes,
  uploadAgent,
  uploadConsole,
  type AgentBinarySource,
  type AgentBinaryView,
} from "./agentBinary";
import { ConfigurationOrigin } from "./ConfigurationOrigin";
import { settingsOverviewQuery } from "./settings";
import { ReauthDialog } from "./SettingsParts";
import { useGuardedAction } from "./useGuardedAction";

// What sets the agent's panel apart from the console's: the file, where it goes, and what the page says about it.
interface Binary {
  query: typeof agentBinaryQuery;
  upload: (file: File) => Promise<AgentBinaryView>;
  maxBytes: number;
  accept: string[];
  configurationKey: string;
  unreadable: ReactNode;
  title: ReactNode;
  runsLabel: ReactNode;
  runs: Record<AgentBinarySource, ReactNode>;
  hashLabel: ReactNode;
  sizeLabel: ReactNode;
  configured: ReactNode;
  uploadTitle: ReactNode;
  warning: ReactNode;
  explanation: ReactNode;
  dropLabel: string;
  dropHere: ReactNode;
  dropHint: ReactNode;
  chooseFirst: () => string;
  tooLarge: (name: string, limit: string) => string;
  uploaded: (uploaded: string) => ReactNode;
  confirmTitle: (name: string) => ReactNode;
  confirmLabel: ReactNode;
  confirmBody: (name: string, size: string) => ReactNode;
  reauthReason: ReactNode;
}

// The agent every netbooting machine switches to: the agent in the boot image asks the server for the current one at
// each boot, downloads it if it differs and runs it instead. An upload therefore reaches machines at their next
// netboot without a new boot image, and runs as SYSTEM on each of them, so it needs the password again.
export function AgentPanel() {
  const { t: translate } = useLingui();

  return (
    <BinaryPanel
      binary={{
        query: agentBinaryQuery,
        upload: uploadAgent,
        maxBytes: maxAgentBytes,
        accept: [".exe"],
        configurationKey: "DDT:Agent:BinaryPath",
        unreadable: <Trans>The agent machines netboot with could not be read.</Trans>,
        title: <Trans>Agent for netbooting machines</Trans>,
        runsLabel: <Trans>Machines run</Trans>,
        runs: {
          Uploaded: <Trans>The agent uploaded here</Trans>,
          Configuration: <Trans>The file DDT:Agent:BinaryPath names in configuration</Trans>,
          None: <Trans>The agent in their boot image, since none was uploaded</Trans>,
        },
        hashLabel: <Trans>SHA-256</Trans>,
        sizeLabel: <Trans>Size</Trans>,
        configured: (
          <Trans>
            DDT:Agent:BinaryPath names the agent in configuration, so it cannot be uploaded here.
            Remove the key and restart DDT to upload the agent on this page.
          </Trans>
        ),
        uploadTitle: <Trans>Upload the agent</Trans>,
        warning: (
          <Trans>
            The agent you upload runs as SYSTEM on every machine that netboots from now on, before
            anybody authorized the machine. Upload only ddt-agent.exe from a DDT release or as
            Publish-Agent.ps1 builds it.
          </Trans>
        ),
        explanation: (
          <Trans>
            Machines take it at their next netboot: the agent in the boot image downloads it and
            runs it instead, so the boot image does not have to be built again. A machine that
            cannot download or start it goes on with the agent in its boot image.
          </Trans>
        ),
        dropLabel: translate`Drop the agent to upload it`,
        dropHere: <Trans>Drop ddt-agent.exe here, or choose it.</Trans>,
        dropHint: <Trans>A Windows executable of at most 128 MB.</Trans>,
        chooseFirst: () => t`Choose the agent first.`,
        tooLarge: (name, limit) =>
          t`${name} is larger than ${limit}, the most the server takes for the agent.`,
        uploaded: (uploaded) => (
          <Trans>
            Uploaded. Machines that netboot from now on run the agent with SHA-256 {uploaded}.
          </Trans>
        ),
        confirmTitle: (name) => <Trans>Upload {name} as the agent?</Trans>,
        confirmLabel: <Trans>Upload agent</Trans>,
        confirmBody: (name, size) => (
          <Trans>
            {name}, {size}, replaces the agent every machine that netboots from now on runs as
            SYSTEM.
          </Trans>
        ),
        reauthReason: (
          <Trans>
            The agent runs as SYSTEM on every machine that netboots, so uploading it needs your
            password again.
          </Trans>
        ),
      }}
    />
  );
}

// The graphical console those machines show, in Windows PE and as the shell of DDT's session in the installed Windows.
// The agent downloads it like itself and starts it instead of the one in the boot image, so a console that speaks a
// newer agent's protocol needs no new boot image either.
export function ConsolePanel() {
  const { t: translate } = useLingui();

  return (
    <BinaryPanel
      binary={{
        query: consoleBinaryQuery,
        upload: uploadConsole,
        maxBytes: maxConsoleBytes,
        accept: [".zip"],
        configurationKey: "DDT:Agent:ConsolePath",
        unreadable: <Trans>The console machines netboot with could not be read.</Trans>,
        title: <Trans>Console for netbooting machines</Trans>,
        runsLabel: <Trans>Machines show</Trans>,
        runs: {
          Uploaded: <Trans>The console uploaded here</Trans>,
          Configuration: <Trans>The zip DDT:Agent:ConsolePath names in configuration</Trans>,
          None: <Trans>The console in their boot image, since none was uploaded</Trans>,
        },
        hashLabel: <Trans>SHA-256 of ddt-console.exe</Trans>,
        sizeLabel: <Trans>Size of its files</Trans>,
        configured: (
          <Trans>
            DDT:Agent:ConsolePath names the console in configuration, so it cannot be uploaded here.
            Remove the key and restart DDT to upload the console on this page.
          </Trans>
        ),
        uploadTitle: <Trans>Upload the console</Trans>,
        warning: (
          <Trans>
            The console you upload runs as SYSTEM in Windows PE on every machine that netboots from
            now on, and as the shell of DDT&apos;s session in the installed Windows. Upload only a
            zip of the folder Publish-Console.ps1 writes, from a DDT release or as the script builds
            it.
          </Trans>
        ),
        explanation: (
          <Trans>
            Machines take it at their next netboot: the agent downloads it and shows it instead of
            the console in the boot image, in Windows PE and in DDT&apos;s session. A machine that
            cannot download it keeps the console in its boot image.
          </Trans>
        ),
        dropLabel: translate`Drop the console to upload it`,
        dropHere: <Trans>Drop the zip with ddt-console.exe here, or choose it.</Trans>,
        dropHint: (
          <Trans>
            A zip of ddt-console.exe, libSkiaSharp.dll and libHarfBuzzSharp.dll, of at most 128 MB.
          </Trans>
        ),
        chooseFirst: () => t`Choose the console first.`,
        tooLarge: (name, limit) =>
          t`${name} is larger than ${limit}, the most the server takes for the console.`,
        uploaded: (uploaded) => (
          <Trans>
            Uploaded. Machines that netboot from now on show the console whose ddt-console.exe has
            SHA-256 {uploaded}.
          </Trans>
        ),
        confirmTitle: (name) => <Trans>Upload {name} as the console?</Trans>,
        confirmLabel: <Trans>Upload console</Trans>,
        confirmBody: (name, size) => (
          <Trans>
            {name}, {size}, replaces the console every machine that netboots from now on runs as
            SYSTEM.
          </Trans>
        ),
        reauthReason: (
          <Trans>
            The console runs as SYSTEM on every machine that netboots, so uploading it needs your
            password again.
          </Trans>
        ),
      }}
    />
  );
}

function BinaryPanel({ binary }: { binary: Binary }) {
  const current = useQuery(binary.query);

  if (current.isError) {
    return <Notice tone="fail">{binary.unreadable}</Notice>;
  }

  if (current.data === undefined) {
    return <Skeleton className="h-64 w-full" />;
  }

  return (
    <>
      <Current binary={binary} view={current.data} />
      {current.data.source === "Configuration" ? (
        <Configured binary={binary} />
      ) : (
        <Upload binary={binary} />
      )}
    </>
  );
}

function Current({ binary, view }: { binary: Binary; view: AgentBinaryView }) {
  const now = useNow(60_000);
  const [copied, setCopied] = useState(false);
  const sha256 = view.sha256;
  const uploadedBy = view.uploadedBy;
  const uploadedWhen = view.uploadedUtc === null ? null : relativeTime(view.uploadedUtc, now);

  return (
    <Panel
      title={binary.title}
      actions={
        sha256 === null ? null : (
          <Button
            size="sm"
            onPress={() => {
              void navigator.clipboard.writeText(sha256).then(() => {
                setCopied(true);
              });
            }}
          >
            {copied ? <Trans>Copied</Trans> : <Trans>Copy SHA-256</Trans>}
          </Button>
        )
      }
    >
      <Facts
        items={[
          { label: binary.runsLabel, value: binary.runs[view.source] },
          ...(sha256 === null ? [] : [{ label: binary.hashLabel, value: sha256, mono: true }]),
          ...(view.size === null
            ? []
            : [{ label: binary.sizeLabel, value: formatBytes(view.size) }]),
          ...(uploadedWhen === null || view.uploadedUtc === null
            ? []
            : [
                {
                  label: <Trans>Uploaded</Trans>,
                  value: (
                    <span title={fullTime(view.uploadedUtc)}>
                      {uploadedBy === null ? (
                        uploadedWhen
                      ) : (
                        <Trans>
                          {uploadedWhen} by {uploadedBy}
                        </Trans>
                      )}
                    </span>
                  ),
                },
              ]),
        ]}
      />
      {sha256 === null ? null : (
        <p className="max-w-[80ch] type-small text-ink-2">
          <Trans>
            Compare the SHA-256 with the one published for the release you meant to install.
          </Trans>
        </p>
      )}
    </Panel>
  );
}

// The keys are kept for development, and the page cannot replace a file configuration names.
function Configured({ binary }: { binary: Binary }) {
  const overview = useQuery(settingsOverviewQuery);
  const setting = overview.data?.server.find((entry) => entry.key === binary.configurationKey);

  return (
    <Notice tone="attention" title={<Trans>Uploads are off</Trans>}>
      {binary.configured}
      {setting?.isSet === true && setting.source !== null ? (
        <span className="mt-1 block">
          <Trans>
            The key is set <ConfigurationOrigin setting={setting} />.
          </Trans>
        </span>
      ) : null}
    </Notice>
  );
}

function Upload({ binary }: { binary: Binary }) {
  const queryClient = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [uploaded, setUploaded] = useState<string | null>(null);

  const action = useGuardedAction({
    askFirst: true,
    send: () => {
      if (file === null) {
        throw new Error(binary.chooseFirst());
      }

      return binary.upload(file);
    },
    onDone: (answer) => {
      queryClient.setQueryData(binary.query.queryKey, answer);
      setUploaded(answer.sha256);
      setFile(null);
    },
  });

  const pick = (chosen: File | undefined) => {
    if (chosen === undefined || action.busy) {
      return;
    }

    const name = chosen.name;

    action.reset();
    setUploaded(null);
    setProblem(null);

    if (chosen.size === 0) {
      setProblem(t`${name} is empty.`);
    } else if (chosen.size > binary.maxBytes) {
      setProblem(binary.tooLarge(name, formatBytes(binary.maxBytes)));
    } else {
      setFile(chosen);
      setConfirming(true);
    }
  };

  const name = file?.name ?? "";
  const reason = action.error;
  const size = file === null ? "" : formatBytes(file.size);

  return (
    <Panel title={binary.uploadTitle}>
      <Notice tone="attention">{binary.warning}</Notice>
      <p className="max-w-[80ch] text-ink-2">{binary.explanation}</p>

      {action.busy ? (
        <ProgressBar label={<Trans>Uploading {name}</Trans>} />
      ) : (
        <DropZone
          aria-label={binary.dropLabel}
          onDrop={(event) => {
            const item = event.items.find(
              (candidate): candidate is FileDropItem => candidate.kind === "file",
            );

            if (item !== undefined) {
              void item.getFile().then(pick);
            }
          }}
          className={({ isDropTarget }) =>
            cx(
              "flex flex-wrap items-center gap-x-4 gap-y-2 rounded-key bg-well px-4 py-3.5 shadow-[inset_0_0_0_1px_var(--color-line)] outline-none",
              isDropTarget && "shadow-[inset_0_0_0_2px_var(--color-focus)]",
            )
          }
        >
          <IconUpload aria-hidden="true" size={22} stroke={1.75} className="text-muted" />
          <span className="flex min-w-0 flex-1 flex-col gap-0.5">
            <Text slot="label" className="type-label text-ink">
              {binary.dropHere}
            </Text>
            <span className="type-small text-muted">{binary.dropHint}</span>
          </span>
          <FileTrigger
            acceptedFileTypes={binary.accept}
            onSelect={(list) => {
              pick(list === null ? undefined : Array.from(list)[0]);
            }}
          >
            <Button>
              <Trans>Choose a file</Trans>
            </Button>
          </FileTrigger>
        </DropZone>
      )}

      {problem !== null ? <Notice tone="fail">{problem}</Notice> : null}
      {reason !== null ? (
        <Notice tone="fail">
          <Trans>
            {name} was not uploaded. {reason}
          </Trans>
        </Notice>
      ) : null}
      {uploaded !== null ? <Notice>{binary.uploaded(uploaded)}</Notice> : null}

      <ConfirmDialog
        isOpen={confirming}
        onOpenChange={(open) => {
          if (!open) {
            setConfirming(false);
            setFile(null);
          }
        }}
        title={binary.confirmTitle(name)}
        confirmLabel={binary.confirmLabel}
        onConfirm={() => {
          setConfirming(false);
          action.start();
        }}
      >
        <p>{binary.confirmBody(name, size)}</p>
      </ConfirmDialog>

      <ReauthDialog
        isOpen={action.needsReauth}
        onAccepted={action.retryAfterReauth}
        onCancel={() => {
          action.cancelReauth();
          setFile(null);
        }}
        confirmLabel={<Trans>Confirm and upload</Trans>}
        reason={binary.reauthReason}
      />
    </Panel>
  );
}
