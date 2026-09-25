// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Images;

// Signer is the common name of the certificate that signed the file, for Trusted and SignedByOthers. Reason says, for
// the other two, what is missing or wrong, as the end of a sentence about the file.
public sealed record AuthenticodeResult(AuthenticodeStatus Status, ushort? Machine, string? Signer, string? Reason);
