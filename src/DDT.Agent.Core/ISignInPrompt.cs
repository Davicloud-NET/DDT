// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// Reads a line typed at the text console. TextMachineConsole asks every question with it, the sign-in's and the
// picker's.
public interface ISignInPrompt
{
    // False when nobody can type at this machine, for example when input is redirected.
    bool IsAvailable { get; }

    // Completes with the typed line, or with null once cancelled.
    Task<string?> ReadLineAsync(string label, bool secret, CancellationToken cancellationToken);
}
