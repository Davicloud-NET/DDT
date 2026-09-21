// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DDT.Server.Certificates;

// The certificate Kestrel serves, held in memory so a renewal reaches the next connection without a restart. DDT makes
// its own root, because boot images pin the root: a certificate issued from it can change without a new boot image.
// Any other certificate is an administrator's: served and loaded again when its files change, never replaced.
public sealed class ServerCertificates
{
    public static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);

    private const int MaxLockAttempts = 300;

    private static readonly TimeSpan s_lockRetry = TimeSpan.FromMilliseconds(100);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly bool _generate;
    private readonly TimeProvider _timeProvider;
    private X509Certificate2? _current;
    private FileStamp? _stamp;
    private string? _warnedThumbprint;

    // Generate: DDT may create a root and issue from it. Without it, DDT only serves the files as they are.
    public ServerCertificates(CertificateFiles files, IReadOnlyList<string> names, bool generate, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(timeProvider);

        Files = files;
        Names = names;
        _generate = generate;
        _timeProvider = timeProvider;
    }

    public CertificateFiles Files { get; }

    public IReadOnlyList<string> Names { get; }

    // Read on every TLS handshake. The certificate it replaces is left to the garbage collector rather than disposed,
    // because a handshake that already picked it may still be using it.
    public X509Certificate2? Current => Volatile.Read(ref _current);

    // Throws when there is nothing to serve at all, which at startup stops the host with the reason.
    public async Task<CertificateCheck> CheckAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(Files.Folder);

            using FileStream fileLock = await LockAsync(cancellationToken).ConfigureAwait(false);

            return Check(_timeProvider.GetUtcNow());
        }
        finally
        {
            _gate.Release();
        }
    }

    private CertificateCheck Check(DateTimeOffset now)
    {
        PemPair? root = _generate ? ReadRoot() : null;
        FileStamp stamp = FileStamp.Of(Files);
        bool changed = _current is null || stamp != _stamp;
        string? problem = null;
        X509Certificate2? pair = changed ? TryLoad(out problem) : _current;

        if (_generate && root is null && pair is null && !stamp.AnyExists)
        {
            root = ServerCertificateAuthority.CreateRoot(now);

            // The key first: a root certificate without its key could never issue again.
            PemFiles.Write(Files.RootKeyPath, root.KeyPem, isKey: true);
            PemFiles.Write(Files.RootPath, root.CertificatePem, isKey: false);

            return Replace(root, null, CertificateAction.Created, now);
        }

        if (root is not null)
        {
            if (pair is null)
            {
                return Replace(root, null, CertificateAction.Issued, now);
            }

            using X509Certificate2 rootCertificate = X509Certificate2.CreateFromPem(root.CertificatePem);

            if (ChainsTo(pair, rootCertificate))
            {
                // Unless the root itself ends first, when renewing would only issue the same end date again.
                if (now >= new DateTimeOffset(pair.NotAfter) - RenewBefore && rootCertificate.NotAfter > pair.NotAfter)
                {
                    return Replace(root, pair, CertificateAction.Renewed, now);
                }

                if (!ServerNames.Covers(pair, Names))
                {
                    return Replace(root, pair, CertificateAction.Reissued, now);
                }

                return Serve(pair, stamp, managed: true, LoadAction(changed), now);
            }
        }

        if (pair is null)
        {
            if (_current is null)
            {
                throw new InvalidOperationException(
                    $"Cannot load the server certificate {Files.CertificatePath} with its key {Files.KeyPath}: {problem} " +
                    (_generate
                        ? "Fix or replace both files, or delete both to have DDT issue its own."
                        : "Fix or replace both files, or delete both and set DDT:Https:GenerateSelfSignedCertificate to true " +
                            "to have DDT issue its own."));
            }

            // Not tried again until the files change once more.
            _stamp = stamp;

            return new CertificateCheck(CertificateAction.LoadFailed, _current, false, false, problem);
        }

        return Serve(pair, stamp, managed: false, LoadAction(changed), now);
    }

    private CertificateAction LoadAction(bool changed) =>
        !changed ? CertificateAction.Unchanged : _current is null ? CertificateAction.Loaded : CertificateAction.Reloaded;

    // Names in the replaced certificate stay, so a renewal never takes away a name something still uses. Addresses are
    // the host's current ones.
    private CertificateCheck Replace(PemPair root, X509Certificate2? replaced, CertificateAction action, DateTimeOffset now)
    {
        IEnumerable<string> names = replaced is null
            ? Names
            : Names.Concat(ServerNames.Of(replaced).Where(name => !IPAddress.TryParse(name, out _)))
                .Distinct(StringComparer.OrdinalIgnoreCase);

        PemPair issued = ServerCertificateAuthority.Issue(root, names, ServerNames.LocalAddresses(), now);

        if (replaced is not null)
        {
            File.Move(Files.CertificatePath, Files.PreviousCertificatePath, overwrite: true);
            File.Move(Files.KeyPath, Files.PreviousKeyPath, overwrite: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Files.KeyPath))!);
        PemFiles.Write(Files.KeyPath, issued.KeyPem, isKey: true);
        PemFiles.Write(Files.CertificatePath, issued.CertificatePem, isKey: false);

        X509Certificate2 certificate = TryLoad(out string? problem)
            ?? throw new InvalidOperationException($"The certificate DDT just wrote to {Files.CertificatePath} does not load: {problem}");

        return Serve(certificate, FileStamp.Of(Files), managed: true, action, now);
    }

    private CertificateCheck Serve(X509Certificate2 certificate, FileStamp stamp, bool managed, CertificateAction action, DateTimeOffset now)
    {
        Volatile.Write(ref _current, certificate);
        _stamp = stamp;

        bool expiresSoon = !managed
            && now >= new DateTimeOffset(certificate.NotAfter) - RenewBefore
            && certificate.Thumbprint != _warnedThumbprint;

        if (expiresSoon)
        {
            _warnedThumbprint = certificate.Thumbprint;
        }

        return new CertificateCheck(action, certificate, managed, expiresSoon, null);
    }

    private PemPair? ReadRoot()
    {
        if (!File.Exists(Files.RootPath))
        {
            return null;
        }

        try
        {
            PemPair root = new(File.ReadAllText(Files.RootPath), File.ReadAllText(Files.RootKeyPath));

            using (X509Certificate2.CreateFromPem(root.CertificatePem, root.KeyPem))
            {
                return root;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // A new root would break every boot image, so DDT never makes one over an existing root on its own.
            throw new InvalidOperationException(
                $"DDT's root certificate {Files.RootPath} cannot be used with its key {Files.RootKeyPath}: {exception.Message} " +
                "Restore the key from a backup. Only if it is lost, delete both root files and the server certificate: DDT " +
                "then makes a new root, and every boot image has to be built again.",
                exception);
        }
    }

    private X509Certificate2? TryLoad(out string? problem)
    {
        try
        {
            X509Certificate2 certificate = X509Certificate2.CreateFromPemFile(Files.CertificatePath, Files.KeyPath);
            problem = null;

            if (!OperatingSystem.IsWindows())
            {
                return certificate;
            }

            // SChannel cannot serve a key that exists only in memory, as one read from PEM does, so on Windows it goes
            // through PKCS#12, as Kestrel does with its own PEM files.
            using (certificate)
            {
                return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), null);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            problem = exception.Message;

            return null;
        }
    }

    // An expired certificate still came from the root, and is renewed rather than taken for someone else's.
    private static bool ChainsTo(X509Certificate2 certificate, X509Certificate2 root)
    {
        using X509Chain chain = new();
        chain.ChainPolicy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true,
            CustomTrustStore = { root },
            VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid,
        };

        return chain.Build(certificate);
    }

    // Other DDT processes on the same store hold it only for a check, which takes well under a second.
    private async Task<FileStream> LockAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(Files.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < MaxLockAttempts)
            {
                await Task.Delay(s_lockRetry, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
