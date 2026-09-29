// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Certificates;

public enum CertificateConfirmation
{
    Confirmed,
    NothingToConfirm,

    // The connection that asked was served another pair, so it proves nothing about the new one.
    NotServedTheNewPair,
}
