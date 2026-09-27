// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Authentication;

public static class DdtAuthenticationSchemes
{
    // Machines authenticate with an opaque bearer token on their own scheme. Policies name the
    // scheme explicitly so a machine token can never satisfy a human policy, or the reverse.
    public const string Machine = "DDT.Machine";

    // A user's API token, the ddt_ secret in an Authorization: Bearer header.
    public const string ApiToken = "DDT.ApiToken";

    // What the human policies authenticate with: the API token when the request carries a bearer token, the session
    // cookie otherwise, never both. A request is thereby either a browser's or a script's, which is what lets a token
    // request skip the CSRF filters while a cookie that rides along with it grants nothing.
    public const string User = "DDT.User";
}
