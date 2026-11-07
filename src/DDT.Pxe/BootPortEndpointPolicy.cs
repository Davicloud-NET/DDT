// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;

namespace DDT.Pxe;

// Takes the boot files' route out of the choice on every port but the boot port. The web has pages whose paths begin
// with /boot, and a browser that loads one of them must get the web's page, not the gate's 404.
internal sealed class BootPortEndpointPolicy(int bootPort) : MatcherPolicy, IEndpointSelectorPolicy
{
    public override int Order => 0;

    public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints) => endpoints.Any(IsBootEndpoint);

    public Task ApplyAsync(HttpContext httpContext, CandidateSet candidates)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(candidates);

        if (httpContext.Connection.LocalPort == bootPort)
        {
            return Task.CompletedTask;
        }

        for (int index = 0; index < candidates.Count; index++)
        {
            if (candidates.IsValidCandidate(index) && IsBootEndpoint(candidates[index].Endpoint))
            {
                candidates.SetValidity(index, false);
            }
        }

        return Task.CompletedTask;
    }

    private static bool IsBootEndpoint(Endpoint? endpoint) => endpoint?.Metadata.GetMetadata<BootEndpointMarker>() is not null;
}
