// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// What the join account sees of its domain, read with its own credentials. The domain join check asks through this, so
// its verdicts are tested without a domain controller.
public interface IDomainDirectory
{
    // Throws DomainDirectoryException when the controller cannot be reached or refuses the sign-in.
    Task<DomainDirectoryFacts> ReadAsync(DomainDirectoryRequest request, CancellationToken cancellationToken);
}
