// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;

namespace DDT.E2E;

// A request to the web API with a JSON body, which AdminApi sends.
internal sealed record JsonRequest<TBody>(HttpMethod Method, string Path, TBody Body, JsonTypeInfo<TBody> BodyType)
{
    public HttpRequestMessage ToMessage() =>
        new(Method, new Uri(Path, UriKind.Relative)) { Content = JsonContent.Create(Body, BodyType) };
}
