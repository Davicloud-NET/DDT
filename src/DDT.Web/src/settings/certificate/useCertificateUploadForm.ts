// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { base64Of, putCertificate, uploadCertificate } from "../certificate";
import { useGuardedAction } from "../useGuardedAction";

export type CertificateFormat = "pem" | "pfx";

// The PEM pair or PFX being uploaded, and the server's refusal of it field by field.
export function useCertificateUploadForm(onClose: () => void) {
  const queryClient = useQueryClient();
  const [format, setFormat] = useState<CertificateFormat>("pem");
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
  // A refusal that is not about one of the form's fields.
  const general =
    refusal === null
      ? null
      : refusal.kind === "invalid"
        ? (fields.certificate ?? []).join(" ") || null
        : refusal.message;
  const ready =
    format === "pem" ? certificatePem.trim() !== "" && keyPem.trim() !== "" : pfx !== null;

  return {
    format,
    setFormat,
    certificatePem,
    setCertificatePem,
    keyPem,
    setKeyPem,
    pfx,
    setPfx,
    pfxPassword,
    setPfxPassword,
    action,
    close,
    errorsOf,
    general,
    ready,
  };
}
