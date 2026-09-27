// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Tokens;

namespace DDT.Server.Tokens;

public static class ApiTokenViews
{
    public static ApiTokenView From(ApiToken token, string userName)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new ApiTokenView(
            token.Id,
            token.Name,
            token.Role,
            token.UserId,
            userName,
            token.Hint,
            token.CreatedUtc,
            token.ExpiresUtc,
            token.LastUsedUtc,
            token.LastUsedAddress,
            token.RevokedUtc,
            token.RevokedByName);
    }
}
