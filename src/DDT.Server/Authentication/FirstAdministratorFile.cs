// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Certificates;
using DDT.Server.Configuration;
using DDT.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace DDT.Server.Authentication;

// First administrator's password until it's changed. Key-file access, unlike the event log.
public sealed class FirstAdministratorFile(IOptions<DdtOptions> options)
{
    private const string UserNamePrefix = "User name: ";
    private const string PasswordPrefix = "Password: ";

    public string Path => System.IO.Path.Combine(options.Value.StorePath, "first-admin.txt");

    public void Write(string userName, string password) =>
        PemFiles.Write(Path, $"{UserNamePrefix}{userName}{Environment.NewLine}{PasswordPrefix}{password}{Environment.NewLine}", isKey: true);

    public (string UserName, string Password)? Read()
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        string[] lines = File.ReadAllLines(Path);
        string? userName = lines.FirstOrDefault(line => line.StartsWith(UserNamePrefix, StringComparison.Ordinal))?[UserNamePrefix.Length..];
        string? password = lines.FirstOrDefault(line => line.StartsWith(PasswordPrefix, StringComparison.Ordinal))?[PasswordPrefix.Length..];

        return userName is null || password is null ? null : (userName, password);
    }

    // Once the password no longer signs its user in.
    public async Task DeleteIfChangedAsync(UserManager<DdtUser> users)
    {
        ArgumentNullException.ThrowIfNull(users);

        if (!File.Exists(Path))
        {
            return;
        }

        if (Read() is not { } first
            || await users.FindByNameAsync(first.UserName).ConfigureAwait(false) is not { } user
            || !await users.CheckPasswordAsync(user, first.Password).ConfigureAwait(false))
        {
            Delete();
        }
    }

    public void Delete() => File.Delete(Path);
}
