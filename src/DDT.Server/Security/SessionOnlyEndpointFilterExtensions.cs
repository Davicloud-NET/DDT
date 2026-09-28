// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace DDT.Server.Security;

public static class SessionOnlyEndpointFilterExtensions
{
    public static TBuilder RequireSession<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddEndpointFilter(new SessionOnlyEndpointFilter());
    }
}
