// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconUpload } from "@tabler/icons-react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { DropZone, FileTrigger, Text, type FileDropItem } from "react-aria-components";

import { formatBytes } from "@/lib/format";
import { fullTime, relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { Button } from "@/ui/Button";
import { ProgressBar } from "@/ui/Controls";
import { cx } from "@/ui/cx";
import { ConfirmDialog } from "@/ui/Dialog";
import { Facts, Panel, Skeleton } from "@/ui/Layout";
import { Logo } from "@/ui/Logo";
import { Notice } from "@/ui/Notice";

import {
  consoleLogoImage,
  consoleLogoQuery,
  maxLogoBytes,
  removeConsoleLogo,
  uploadConsoleLogo,
  type ConsoleLogoView,
} from "./consoleLogo";
import { useGuardedAction } from "./useGuardedAction";

// The organisation's logo on the console at the machine, at the right end of its header, which is dark in both of the
// console's themes. An upload or a removal applies at once and reaches machines at their next registration; operators
// see it, administrators change it.
export function ConsoleLogoPanel({ canChange }: { canChange: boolean }) {
  const logo = useQuery(consoleLogoQuery);

  return (
    <Panel title={<Trans>Logo on the console</Trans>}>
      <p className="max-w-[80ch] text-ink-2">
        <Trans>
          The console at the machine shows this logo at the right end of its header, in Windows PE
          and in DDT&apos;s session, at most 32 pixels high and 200 wide. The header is dark in both
          of the console&apos;s themes, so a logo in white or light colours on a transparent
          background suits it. Machines take a new logo when they next register.
        </Trans>
      </p>
      {logo.isError ? (
        <Notice tone="fail">
          <Trans>The console&apos;s logo could not be read.</Trans>
        </Notice>
      ) : logo.data === undefined ? (
        <Skeleton className="h-32 w-full" />
      ) : (
        <>
          <Preview view={logo.data} />
          <Details view={logo.data} />
          {canChange ? <Change view={logo.data} /> : null}
        </>
      )}
    </Panel>
  );
}

// The console's header as it looks with the logo, on the header's own colour.
function Preview({ view }: { view: ConsoleLogoView }) {
  const { t: translate } = useLingui();

  return (
    <div
      role="img"
      aria-label={translate`The console's header with the logo`}
      className="flex h-14 items-center gap-3 overflow-hidden rounded-panel bg-frame px-6 text-frame-text"
    >
      <Logo size={22} />
      <span className="type-wordmark">DDT</span>
      <span className="flex-1" />
      <span className="truncate type-small text-frame-muted max-sm:hidden">
        <Trans>Connected to the server</Trans>
      </span>
      {view.sha256 === null ? (
        <span className="type-small text-frame-muted">
          <Trans>No logo</Trans>
        </span>
      ) : (
        <>
          <span aria-hidden="true" className="mx-2 h-6 w-px bg-frame-line" />
          <img
            src={consoleLogoImage(view.sha256)}
            alt=""
            className="max-h-8 max-w-[200px] object-contain"
          />
        </>
      )}
    </div>
  );
}

function Details({ view }: { view: ConsoleLogoView }) {
  const now = useNow(60_000);

  if (view.sha256 === null) {
    return null;
  }

  const width = String(view.width ?? 0);
  const height = String(view.height ?? 0);
  const uploadedBy = view.uploadedBy;
  const uploadedWhen = view.uploadedUtc === null ? null : relativeTime(view.uploadedUtc, now);

  return (
    <Facts
      items={[
        {
          label: <Trans>Picture</Trans>,
          value: (
            <Trans>
              PNG, {width} by {height} pixels
            </Trans>
          ),
        },
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
  );
}

function Change({ view }: { view: ConsoleLogoView }) {
  const { t: translate } = useLingui();
  const queryClient = useQueryClient();
  // The file being sent, which the upload reads when it starts; its name stays for the answer.
  const chosen = useRef<File | null>(null);
  const [name, setName] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [done, setDone] = useState<"uploaded" | "removed" | null>(null);
  const [confirmingRemoval, setConfirmingRemoval] = useState(false);

  const settle = (answer: ConsoleLogoView) => {
    queryClient.setQueryData(consoleLogoQuery.queryKey, answer);
  };

  const upload = useGuardedAction({
    send: () => {
      if (chosen.current === null) {
        throw new Error(t`Choose the logo first.`);
      }

      return uploadConsoleLogo(chosen.current);
    },
    onDone: (answer) => {
      settle(answer);
      setDone("uploaded");
      chosen.current = null;
    },
  });

  const removal = useGuardedAction({
    send: () => removeConsoleLogo(),
    onDone: (answer) => {
      settle(answer);
      setDone("removed");
    },
  });

  const busy = upload.busy || removal.busy;

  // A chosen file is sent at once; a wrong one is replaced or removed just as quickly.
  const pick = (file: File | undefined) => {
    if (file === undefined || busy) {
      return;
    }

    const name = file.name;
    const limit = formatBytes(maxLogoBytes);

    upload.reset();
    removal.reset();
    setDone(null);
    setProblem(null);

    if (file.type !== "image/png" && !name.toLowerCase().endsWith(".png")) {
      setProblem(t`${name} is not a PNG image. Upload the logo as a PNG file.`);
    } else if (file.size === 0) {
      setProblem(t`${name} is empty.`);
    } else if (file.size > maxLogoBytes) {
      setProblem(t`${name} is larger than ${limit}, the most the server takes for the logo.`);
    } else {
      chosen.current = file;
      setName(name);
      upload.start();
    }
  };

  const reason = upload.error ?? removal.error;

  return (
    <>
      {busy ? (
        <ProgressBar
          label={upload.busy ? <Trans>Uploading {name}</Trans> : <Trans>Removing the logo</Trans>}
        />
      ) : (
        <div className="flex flex-wrap items-center gap-3">
          <DropZone
            aria-label={translate`Drop the logo to upload it`}
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
                "flex min-w-0 flex-1 flex-wrap items-center gap-x-4 gap-y-2 rounded-key bg-well px-4 py-3.5 shadow-[inset_0_0_0_1px_var(--color-line)] outline-none",
                isDropTarget && "shadow-[inset_0_0_0_2px_var(--color-focus)]",
              )
            }
          >
            <IconUpload aria-hidden="true" size={22} stroke={1.75} className="text-muted" />
            <span className="flex min-w-0 flex-1 flex-col gap-0.5">
              <Text slot="label" className="type-label text-ink">
                {view.sha256 === null ? (
                  <Trans>Drop the logo here, or choose it.</Trans>
                ) : (
                  <Trans>Drop another logo here to replace it, or choose one.</Trans>
                )}
              </Text>
              <span className="type-small text-muted">
                <Trans>A PNG of at most 512 KB and 2048 by 2048 pixels.</Trans>
              </span>
            </span>
            <FileTrigger
              acceptedFileTypes={["image/png"]}
              onSelect={(list) => {
                pick(list === null ? undefined : Array.from(list)[0]);
              }}
            >
              <Button>
                <Trans>Choose a file</Trans>
              </Button>
            </FileTrigger>
          </DropZone>
          {view.sha256 === null ? null : (
            <Button
              variant="quiet"
              onPress={() => {
                upload.reset();
                removal.reset();
                setDone(null);
                setProblem(null);
                setConfirmingRemoval(true);
              }}
            >
              <Trans>Remove the logo</Trans>
            </Button>
          )}
        </div>
      )}

      {problem !== null ? <Notice tone="fail">{problem}</Notice> : null}
      {reason !== null ? (
        <Notice tone="fail">
          {upload.error !== null ? (
            <Trans>
              {name} was not uploaded. {reason}
            </Trans>
          ) : (
            <Trans>The logo was not removed. {reason}</Trans>
          )}
        </Notice>
      ) : null}
      {done === "uploaded" ? (
        <Notice>
          <Trans>Uploaded. Machines show the logo when they next register.</Trans>
        </Notice>
      ) : null}
      {done === "removed" ? (
        <Notice>
          <Trans>Removed. Machines show no logo when they next register.</Trans>
        </Notice>
      ) : null}

      <ConfirmDialog
        isOpen={confirmingRemoval}
        onOpenChange={setConfirmingRemoval}
        title={<Trans>Remove the logo?</Trans>}
        confirmLabel={<Trans>Remove logo</Trans>}
        onConfirm={() => {
          setConfirmingRemoval(false);
          removal.start();
        }}
      >
        <p>
          <Trans>
            Machines show no logo on their console from their next registration. Upload it again to
            bring it back.
          </Trans>
        </p>
      </ConfirmDialog>
    </>
  );
}
