// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconUpload } from "@tabler/icons-react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
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

import { agentBinaryQuery, maxAgentBytes, uploadAgent, type AgentBinaryView } from "./agentBinary";
import { ConfigurationOrigin } from "./ConfigurationOrigin";
import { settingsOverviewQuery } from "./settings";
import { ReauthDialog } from "./SettingsParts";
import { useGuardedAction } from "./useGuardedAction";

// The agent every netbooting machine switches to: the agent in the boot image asks the server for the current one at
// each boot, downloads it if it differs and runs it instead. An upload therefore reaches machines at their next
// netboot without a new boot image, and runs as SYSTEM on each of them, so it needs the password again.
export function AgentPanel() {
  const agent = useQuery(agentBinaryQuery);

  if (agent.isError) {
    return (
      <Notice tone="fail">
        <Trans>The agent machines netboot with could not be read.</Trans>
      </Notice>
    );
  }

  if (agent.data === undefined) {
    return <Skeleton className="h-64 w-full" />;
  }

  return (
    <>
      <Current view={agent.data} />
      {agent.data.source === "Configuration" ? <Configured /> : <Upload />}
    </>
  );
}

function Current({ view }: { view: AgentBinaryView }) {
  const now = useNow(60_000);
  const [copied, setCopied] = useState(false);
  const sha256 = view.sha256;
  const uploadedBy = view.uploadedBy;
  const uploadedWhen = view.uploadedUtc === null ? null : relativeTime(view.uploadedUtc, now);

  return (
    <Panel
      title={<Trans>Agent for netbooting machines</Trans>}
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
          {
            label: <Trans>Machines run</Trans>,
            value:
              view.source === "Uploaded" ? (
                <Trans>The agent uploaded here</Trans>
              ) : view.source === "Configuration" ? (
                <Trans>The file DDT:Agent:BinaryPath names in configuration</Trans>
              ) : (
                <Trans>The agent in their boot image, since none was uploaded</Trans>
              ),
          },
          ...(sha256 === null
            ? []
            : [{ label: <Trans>SHA-256</Trans>, value: sha256, mono: true }]),
          ...(view.size === null
            ? []
            : [{ label: <Trans>Size</Trans>, value: formatBytes(view.size) }]),
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

// DDT:Agent:BinaryPath is kept for development, and the page cannot replace a file configuration names.
function Configured() {
  const overview = useQuery(settingsOverviewQuery);
  const setting = overview.data?.server.find((entry) => entry.key === "DDT:Agent:BinaryPath");

  return (
    <Notice tone="attention" title={<Trans>Uploads are off</Trans>}>
      <Trans>
        DDT:Agent:BinaryPath names the agent in configuration, so it cannot be uploaded here. Remove
        the key and restart DDT to upload the agent on this page.
      </Trans>
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

function Upload() {
  const { t: translate } = useLingui();
  const queryClient = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [uploaded, setUploaded] = useState<string | null>(null);

  const action = useGuardedAction({
    askFirst: true,
    send: () => {
      if (file === null) {
        throw new Error(t`Choose the agent first.`);
      }

      return uploadAgent(file);
    },
    onDone: (answer) => {
      queryClient.setQueryData(agentBinaryQuery.queryKey, answer);
      setUploaded(answer.sha256);
      setFile(null);
    },
  });

  const pick = (chosen: File | undefined) => {
    if (chosen === undefined || action.busy) {
      return;
    }

    const name = chosen.name;
    const limit = formatBytes(maxAgentBytes);

    action.reset();
    setUploaded(null);
    setProblem(null);

    if (chosen.size === 0) {
      setProblem(t`${name} is empty.`);
    } else if (chosen.size > maxAgentBytes) {
      setProblem(t`${name} is larger than ${limit}, the most the server takes for the agent.`);
    } else {
      setFile(chosen);
      setConfirming(true);
    }
  };

  const name = file?.name ?? "";
  const reason = action.error;
  const size = file === null ? "" : formatBytes(file.size);

  return (
    <Panel title={<Trans>Upload the agent</Trans>}>
      <Notice tone="attention">
        <Trans>
          The agent you upload runs as SYSTEM on every machine that netboots from now on, before
          anybody authorized the machine. Upload only ddt-agent.exe from a DDT release or as
          Publish-Agent.ps1 builds it.
        </Trans>
      </Notice>
      <p className="max-w-[80ch] text-ink-2">
        <Trans>
          Machines take it at their next netboot: the agent in the boot image downloads it and runs
          it instead, so the boot image does not have to be built again. A machine that cannot
          download or start it goes on with the agent in its boot image.
        </Trans>
      </p>

      {action.busy ? (
        <ProgressBar label={<Trans>Uploading {name}</Trans>} />
      ) : (
        <DropZone
          aria-label={translate`Drop the agent to upload it`}
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
              <Trans>Drop ddt-agent.exe here, or choose it.</Trans>
            </Text>
            <span className="type-small text-muted">
              <Trans>A Windows executable of at most 128 MB.</Trans>
            </span>
          </span>
          <FileTrigger
            acceptedFileTypes={[".exe"]}
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
      {uploaded !== null ? (
        <Notice>
          <Trans>
            Uploaded. Machines that netboot from now on run the agent with SHA-256 {uploaded}.
          </Trans>
        </Notice>
      ) : null}

      <ConfirmDialog
        isOpen={confirming}
        onOpenChange={(open) => {
          if (!open) {
            setConfirming(false);
            setFile(null);
          }
        }}
        title={<Trans>Upload {name} as the agent?</Trans>}
        confirmLabel={<Trans>Upload agent</Trans>}
        onConfirm={() => {
          setConfirming(false);
          action.start();
        }}
      >
        <p>
          <Trans>
            {name}, {size}, replaces the agent every machine that netboots from now on runs as
            SYSTEM.
          </Trans>
        </p>
      </ConfirmDialog>

      <ReauthDialog
        isOpen={action.needsReauth}
        onAccepted={action.retryAfterReauth}
        onCancel={() => {
          action.cancelReauth();
          setFile(null);
        }}
        confirmLabel={<Trans>Confirm and upload</Trans>}
        reason={
          <Trans>
            The agent runs as SYSTEM on every machine that netboots, so uploading it needs your
            password again.
          </Trans>
        }
      />
    </Panel>
  );
}
