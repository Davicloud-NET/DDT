// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.AccessControl;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using DDT.Server.Certificates;

namespace DDT.Server.Tests;

// A certs folder of its own, laid out as build/compose.yaml configures the store.
public sealed class CertificateFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ddt-certs-" + Guid.NewGuid().ToString("N"));

    public CertificateFiles Files => new(System.IO.Path.Combine(Path, "ddt.pem"), System.IO.Path.Combine(Path, "ddt-key.pem"));

    public X509Certificate2 Root() => X509Certificate2.CreateFromPem(File.ReadAllText(Files.RootPath));

    public X509Certificate2 Certificate() => X509Certificate2.CreateFromPemFile(Files.CertificatePath, Files.KeyPath);

    // Puts a pair where the configuration points, as an administrator or an older DDT left it.
    public static void Write(CertificateFiles files, PemPair pair)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(pair);

        Directory.CreateDirectory(files.Folder);
        File.WriteAllText(files.CertificatePath, pair.CertificatePem);
        File.WriteAllText(files.KeyPath, pair.KeyPem);
    }

    // The chain the agent builds: DDT's root and nothing from the machine store, no revocation, no downloads.
    public static bool ChainsUnderTheAgentsPolicy(X509Certificate2 certificate, X509Certificate2 root, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        using X509Chain chain = new();
        chain.ChainPolicy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true,
            CustomTrustStore = { root },
            VerificationTime = at.UtcDateTime,
        };

        return chain.Build(certificate);
    }

    // Like a read-only mount: what is in the folder can be read, but nothing can be created in it. Root ignores the
    // mode on Linux, so there only a test run as another user shows it.
    public void DenyWrites()
    {
        if (OperatingSystem.IsWindows())
        {
            using WindowsIdentity user = WindowsIdentity.GetCurrent();
            DirectoryInfo folder = new(Path);
            DirectorySecurity security = folder.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                user.User!,
                FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories,
                AccessControlType.Deny));
            folder.SetAccessControl(security);
        }
        else
        {
            File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            // Deleting what is in a folder takes write access to it on Linux, though not on Windows.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Directory.Delete(Path, recursive: true);
        }
    }
}
