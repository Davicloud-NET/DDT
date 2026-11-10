// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { selectOrdinal } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { isOverdue } from "@/deployments/handOver";
import { formattingLocale } from "@/i18n/i18n";
import { formatDuration } from "@/lib/format";

interface LinuxHandOverWaitProps {
  // When the Linux boot environment handed the run over, or sent the machine back to its disk the last time.
  sinceUtc: string;
  // How often the machine has started from the network again since the hand-over. 0 when it has not.
  returns?: number;
  // "step 2 of 4: Write the cloud-init seed", or null before the agent reported a step.
  after: string | null;
  // On the server's clock, as sinceUtc is.
  now: number;
}

// A Linux run after its hand-over, until the agent calls from the installed Linux. Nothing may ever come, and the
// server cannot tell why, so this says since when it waits and, after a first start's usual time, where to look. A
// machine that started from the network again has shown that its first start did not get there.
export function LinuxHandOverWait({ sinceUtc, returns = 0, after, now }: LinuxHandOverWaitProps) {
  const when = new Date(sinceUtc).toLocaleString(formattingLocale());
  const ago = formatDuration(now - Date.parse(sinceUtc));
  const output = "/var/log/cloud-init-output.log";
  const journal = "journalctl -u ddt-agent";
  const nth = selectOrdinal(returns, { one: "#st", two: "#nd", few: "#rd", other: "#th" });

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
      <span className="max-w-[80ch] type-small text-ink-2">
        {returns > 0 ? (
          <Trans>
            The machine started from the network again {when}, {ago} ago, for the {nth} time. The
            installed Linux has not called since.
          </Trans>
        ) : (
          <Trans>
            Handed over {when}, {ago} ago. The installed Linux has not called yet.
          </Trans>
        )}
      </span>
      {returns > 0 ? (
        <span className="max-w-[80ch] type-small text-ink-2">
          <Trans>
            DDT ends the run if the machine keeps coming back. Look at the machine's own screen, and
            in the installed Linux at <span className="type-data">{output}</span> and{" "}
            <span className="type-data whitespace-nowrap">{journal}</span>.
          </Trans>
        </span>
      ) : isOverdue(sinceUtc, now) ? (
        <span className="max-w-[80ch] type-small text-ink-2">
          <Trans>
            That is longer than a first start usually takes. Look at the machine's own screen, and
            in the installed Linux at <span className="type-data">{output}</span> and{" "}
            <span className="type-data whitespace-nowrap">{journal}</span>.
          </Trans>
        </span>
      ) : null}
    </div>
  );
}
