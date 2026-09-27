// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Authentication;

public static class DdtClaimTypes
{
    public const string Actor = "ddt:actor";
    public const string MachineActor = "machine";
    public const string TokenGeneration = "ddt:tokengen";
    public const string TokenPurpose = "ddt:tokenpurpose";

    // Kept as a claim of the account, so that it travels in the cookie and the policies can refuse on it without a
    // query. Its value is not read.
    public const string MustChangePassword = "ddt:mustchangepassword";

    // On a user's principal authenticated by an API token: the token's id and name.
    public const string ApiTokenId = "ddt:apitoken";
    public const string ApiTokenName = "ddt:apitokenname";
}
