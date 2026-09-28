// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Authentication;

public static class DdtAuthenticationSchemes
{
    // Policies name the machine scheme, so a machine token never satisfies a human policy, nor the reverse.
    public const string Machine = "DDT.Machine";

    // A user's API token, the ddt_ secret in an Authorization: Bearer header.
    public const string ApiToken = "DDT.ApiToken";

    // The API token when the request carries a bearer token, the session cookie otherwise, never both. So a token
    // request may skip the CSRF filters, and a cookie riding along with it grants nothing.
    public const string User = "DDT.User";
}
