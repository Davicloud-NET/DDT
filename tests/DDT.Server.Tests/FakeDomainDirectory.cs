// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Deployments;

namespace DDT.Server.Tests;

public sealed class FakeDomainDirectory : IDomainDirectory
{
    private readonly List<DomainDirectoryRequest> _requests = [];

    public Func<DomainDirectoryRequest, DomainDirectoryFacts> Answer { get; set; } =
        request => new("LDAPS", "DC=corp,DC=example", request.OrganizationalUnit, CanCreateComputers: true, 10, 0);

    public IReadOnlyList<DomainDirectoryRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public Task<DomainDirectoryFacts> ReadAsync(DomainDirectoryRequest request, CancellationToken cancellationToken)
    {
        lock (_requests)
        {
            _requests.Add(request);
        }

        return Task.FromResult(Answer(request));
    }
}
