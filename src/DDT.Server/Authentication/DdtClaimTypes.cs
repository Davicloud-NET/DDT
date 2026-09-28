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

    // Stored as a claim on the account, so it travels in the cookie and the policies can refuse without a database
    // query. Its value isn't read.
    public const string MustChangePassword = "ddt:mustchangepassword";

    // The token's id and name, on a user's principal that an API token authenticated.
    public const string ApiTokenId = "ddt:apitoken";
    public const string ApiTokenName = "ddt:apitokenname";
}
