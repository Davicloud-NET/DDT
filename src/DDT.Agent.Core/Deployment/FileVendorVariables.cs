// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// Vendor variables as files in a folder, named as efivarfs names them. They stand in for the firmware of a dry run's
// machine, which has none.
public sealed class FileVendorVariables(string folder) : IVendorVariables
{
    public static string PathFor(string folder, string name, Guid vendor) => Path.Combine(folder, $"{name}-{vendor:D}");

    public byte[]? Read(string name, Guid vendor) =>
        Try(name, "read", () => File.Exists(PathFor(folder, name, vendor)) ? File.ReadAllBytes(PathFor(folder, name, vendor)) : null);

    public void Write(string name, Guid vendor, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);

        Try<object?>(name, "written", () =>
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(PathFor(folder, name, vendor), value);

            return null;
        });
    }

    public void Delete(string name, Guid vendor) =>
        Try<object?>(name, "deleted", () =>
        {
            // Deleting a file in a folder that is not there throws, where deleting a missing file does not
            if (File.Exists(PathFor(folder, name, vendor)))
            {
                File.Delete(PathFor(folder, name, vendor));
            }

            return null;
        });

    private static T Try<T>(string name, string done, Func<T> act)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        try
        {
            return act();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DeploymentStepException($"The firmware variable {name} cannot be {done} ({exception.Message}).", exception);
        }
    }
}
