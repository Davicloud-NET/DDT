// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { isOverdue } from "@/deployments/handOver";
import { formattingLocale } from "@/i18n/i18n";
import { formatDuration } from "@/lib/format";

interface LinuxHandOverWaitProps {
  // When the Linux boot environment handed the run over.
  sinceUtc: string;
  // "step 2 of 4: Write the cloud-init seed", or null before the agent reported a step.
  after: string | null;
  now: number;
}

// A Linux run after its hand-over, until the agent calls from the installed Linux. Nothing may ever come, and the
// server cannot tell why, so this says since when it waits and, after a first start's usual time, where to look.
export function LinuxHandOverWait({ sinceUtc, after, now }: LinuxHandOverWaitProps) {
  const when = new Date(sinceUtc).toLocaleString(formattingLocale());
  const ago = formatDuration(now - Date.parse(sinceUtc));
  const output = "/var/log/cloud-init-output.log";
  const journal = "journalctl -u ddt-agent";

  return (
    <div className="flex flex-col gap-1 py-1">
      <span className="type-heading text-run-text">
        <Trans>Waiting for the installed Linux</Trans>
      </span>
      {after !== null ? (
        <span className="type-small text-ink-2">
          <Trans>After {after}</Trans>
        </span>
      ) : null}
      <span className="type-small text-ink-2">
        <Trans>
          Handed over {when}, {ago} ago. The installed Linux has not called yet.
        </Trans>
      </span>
      <span className="type-small text-ink-2">
        {isOverdue(sinceUtc, now) ? (
          <Trans>
            That is longer than a first start usually takes. It may still be installing packages.
            Otherwise the machine did not start from its disk, the image has no cloud-init or no
            systemd, the user-data keeps cloud-init from running DDT's script, or that script
            failed. Look at the machine's own screen, and in the installed Linux at{" "}
            <span className="type-data">{output}</span> and{" "}
            <span className="type-data">{journal}</span>.
          </Trans>
        ) : (
          <Trans>
            It starts first, and cloud-init sets it up before DDT's agent starts there. Packages it
            installs take their time.
          </Trans>
        )}
      </span>
    </div>
  );
}
