// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";

import { currentUserQuery } from "@/auth/auth";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { acknowledgeReplacedAnchor, serverCertificateQuery } from "@/server/serverCertificate";

import styles from "./ReplacedAnchorBanner.module.scss";

// Boot images built before DDT had its own root pin the self-signed certificate it replaced, and cannot reach
// the server any more. Only an administrator can rebuild them and end the notice, so only they see it.
export function ReplacedAnchorBanner() {
  const queryClient = useQueryClient();
  const user = useQuery(currentUserQuery).data ?? null;
  const isAdministrator = user?.roles.includes("Administrator") === true;
  const certificate = useQuery({ ...serverCertificateQuery, enabled: isAdministrator });

  const titleId = useId();
  const [confirming, setConfirming] = useState(false);

  const acknowledge = useMutation({
    mutationFn: acknowledgeReplacedAnchor,
    onSuccess: () => {
      setConfirming(false);
      queryClient.setQueryData(serverCertificateQuery.queryKey, (view) =>
        view === undefined || view === null ? view : { ...view, anchorReplacedUtc: null },
      );
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: serverCertificateQuery.queryKey });
    },
  });

  const replaced = certificate.data?.anchorReplacedUtc ?? null;

  if (!isAdministrator || replaced === null) {
    return null;
  }

  const rootSha256 = certificate.data?.rootSha256 ?? null;

  return (
    <section className={styles.banner} aria-labelledby={titleId}>
      <h2 id={titleId} className={styles.title}>
        Build every boot image again
      </h2>
      <p>
        On {new Date(replaced).toLocaleString()} DDT replaced its self-signed certificate with one
        from its own root. Boot images built before then pin the old certificate and can no longer
        reach this server. Build each of them again with <code>Build-BootImage.ps1</code> and{" "}
        <code>-RootCertificatePath</code> set to <code>ddt-root.pem</code>, which lies next to the
        server certificate (<code>/var/lib/ddt/certs/ddt-root.pem</code> in the container) and can
        be downloaded from <a href="/api/about/root-certificate">/api/about/root-certificate</a>.
        {rootSha256 !== null && (
          <>
            {" "}
            Its SHA-256 is <code className={styles.hash}>{rootSha256}</code>.
          </>
        )}{" "}
        Later renewals need no rebuild.
      </p>
      <div>
        <button
          type="button"
          className={styles.done}
          onClick={() => {
            acknowledge.reset();
            setConfirming(true);
          }}
        >
          Done
        </button>
      </div>

      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        title="Every boot image is built again?"
        consequence="This notice goes away for every administrator, and DDT deletes its copy of the replaced certificate. A boot image still built with the old certificate keeps failing to connect, with nothing here to say why."
        confirmLabel="Every boot image is built again"
        busy={acknowledge.isPending}
        error={acknowledge.isError ? acknowledge.error.message : null}
        onConfirm={() => {
          acknowledge.mutate();
        }}
      />
    </section>
  );
}
