// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;

namespace DDT.Server.Settings;

// The result of a change to the server certificate. View is set once the new pair is served, the other fields say why
// not. Problem names a field of the request, NewRoot asks for the confirmation certificate.newRoot, and Refusal means
// the change conflicts with the server's state.
internal sealed record CertificateChange
{
    public CertificateView? View { get; init; }

    public SettingProblem? Problem { get; init; }

    public ServerMessage? NewRoot { get; init; }

    public ServerMessage? Refusal { get; init; }
}
