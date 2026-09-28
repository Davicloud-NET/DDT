// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Deployments;

// What the web UI needs to say what an assignment does before it is sent. ZeroTouchEnabled: an assignment to a machine
// that is not waiting carries over to its next netboot from a listed network, with web approval off. ServerUtc lets
// the browser correct its clock, because the server decides by its own clock whether a machine is still at its prompt.
public sealed record DeploymentOptionsView(bool DomainConfigured, bool RequireWebApproval, bool ZeroTouchEnabled, DateTimeOffset ServerUtc);
