// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DDT.Contracts.Server;

namespace DDT.Server.Certificates;

// The certificate Kestrel serves, held in memory so a renewal reaches the next connection without a restart. DDT makes
// its own root, because boot images pin the root: a certificate issued from it can change without a new boot image.
// Any other certificate is an administrator's: served and loaded again when its files change, never replaced.
public sealed class ServerCertificates
{
    public static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);

    private const int MaxLockAttempts = 300;

    // What DDT generated before it had a root: self-signed, not a CA, and named like this.
    private const string LegacySubject = "CN=DDT";

    private static readonly TimeSpan s_lockRetry = TimeSpan.FromMilliseconds(100);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly bool _generate;
    private readonly TimeProvider _timeProvider;
    private SslStreamCertificateContext? _context;
    private string? _rootPem;
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

    public X509Certificate2? Current => Context?.TargetCertificate;

    // Read on every TLS handshake: the certificate with the intermediates its file holds, which clients such as the
    // agent do not download. The context it replaces is left to the garbage collector rather than disposed, because a
    // handshake that already picked it may still be using it.
    public SslStreamCertificateContext? Context => Volatile.Read(ref _context);

    // DDT's root while the served certificate comes from it, and null for an administrator's certificate.
    public string? RootCertificatePem => Volatile.Read(ref _rootPem);

    // Throws when there is nothing to serve at all, which at startup stops the host with the reason.
    public async Task<CertificateCheck> CheckAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // An administrator's certificate may sit where DDT cannot write, such as a read-only mount, so the lock is
            // taken only in a folder that holds DDT's root, or once a check finds something to write.
            if ((!_generate || !File.Exists(Files.RootPath)) && Check(_timeProvider.GetUtcNow(), locked: false) is { } check)
            {
                return check;
            }

            Directory.CreateDirectory(Files.Folder);

            using FileStream fileLock = await LockAsync(cancellationToken).ConfigureAwait(false);

            return Check(_timeProvider.GetUtcNow(), locked: true)!;
        }
        finally
        {
            _gate.Release();
        }
    }

    public ServerCertificateView? Describe()
    {
        if (Current is not { } current)
        {
            return null;
        }

        string? rootPem = RootCertificatePem;
        using X509Certificate2? root = rootPem is null ? null : X509Certificate2.CreateFromPem(rootPem);
        DateTimeOffset notAfter = Utc(current.NotAfter);
        FileInfo anchor = new(Files.ReplacedAnchorPath);

        return new ServerCertificateView(
            root is not null,
            current.Subject,
            current.GetCertHashString(HashAlgorithmName.SHA256),
            notAfter,
            root is null ? null : notAfter - RenewBefore,
            ServerNames.Of(current),
            root?.Subject,
            root?.GetCertHashString(HashAlgorithmName.SHA256),
            root is null ? null : Utc(root.NotAfter),
            anchor.Exists ? new DateTimeOffset(anchor.LastWriteTimeUtc) : null);
    }

    // The SHA-256 of the certificate older boot images pin, or null once no boot image is waiting for a rebuild.
    public string? ReplacedAnchorSha256()
    {
        if (!File.Exists(Files.ReplacedAnchorPath))
        {
            return null;
        }

        using X509Certificate2 anchor = X509Certificate2.CreateFromPem(File.ReadAllText(Files.ReplacedAnchorPath));

        return anchor.GetCertHashString(HashAlgorithmName.SHA256);
    }

    public void ForgetReplacedAnchor() => File.Delete(Files.ReplacedAnchorPath);

    // Null when something is due to be written but the files are not locked, to be checked again under the lock.
    private CertificateCheck? Check(DateTimeOffset now, bool locked)
    {
        PemPair? root = _generate ? ReadRoot() : null;
        FileStamp stamp = FileStamp.Of(Files);
        bool changed = _context is null || stamp != _stamp;
        string? problem = null;
        SslStreamCertificateContext? loaded = changed ? TryLoad(out problem) : _context;
        X509Certificate2? pair = loaded?.TargetCertificate;

        if (_generate && root is null && pair is null && !stamp.AnyExists)
        {
            return locked ? Replace(CreateRoot(now), null, CertificateAction.Created, now) : null;
        }

        // Its only names are its subject alternative names, which the new certificate keeps. The break for boot images
        // that pin it cannot be avoided: it is no CA, so nothing new can chain to it.
        if (_generate && root is null && pair is not null && IsLegacy(pair))
        {
            if (!locked)
            {
                return null;
            }

            root = CreateRoot(now);
            PemFiles.Write(Files.ReplacedAnchorPath, pair.ExportCertificatePem(), isKey: false);

            return Replace(root, pair, CertificateAction.Migrated, now);
        }

        if (root is not null)
        {
            if (pair is null)
            {
                return locked ? Replace(root, null, CertificateAction.Issued, now) : null;
            }

            using X509Certificate2 rootCertificate = X509Certificate2.CreateFromPem(root.CertificatePem);

            if (ChainsTo(pair, rootCertificate))
            {
                // Unless the root itself ends first, when renewing would only issue the same end date again.
                if (now >= Utc(pair.NotAfter) - RenewBefore && rootCertificate.NotAfter > pair.NotAfter)
                {
                    return locked ? Replace(root, pair, CertificateAction.Renewed, now) : null;
                }

                if (!ServerNames.Covers(pair, Names))
                {
                    return locked ? Replace(root, pair, CertificateAction.Reissued, now) : null;
                }

                return Serve(loaded!, stamp, root.CertificatePem, LoadAction(changed), now);
            }
        }

        if (pair is null)
        {
            if (_context is null)
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

            return new CertificateCheck(CertificateAction.LoadFailed, _context.TargetCertificate, RootCertificatePem is not null, false, problem);
        }

        return Serve(loaded!, stamp, null, LoadAction(changed), now);
    }

    private CertificateAction LoadAction(bool changed) =>
        !changed ? CertificateAction.Unchanged : _context is null ? CertificateAction.Loaded : CertificateAction.Reloaded;

    private PemPair CreateRoot(DateTimeOffset now)
    {
        PemPair root = ServerCertificateAuthority.CreateRoot(now);

        // The key first: a root certificate without its key could never issue again.
        PemFiles.Write(Files.RootKeyPath, root.KeyPem, isKey: true);
        PemFiles.Write(Files.RootPath, root.CertificatePem, isKey: false);

        return root;
    }

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

        SslStreamCertificateContext context = TryLoad(out string? problem)
            ?? throw new InvalidOperationException($"The certificate DDT just wrote to {Files.CertificatePath} does not load: {problem}");

        return Serve(context, FileStamp.Of(Files), root.CertificatePem, action, now);
    }

    // RootPem: the root the certificate comes from, or null for an administrator's certificate.
    private CertificateCheck Serve(SslStreamCertificateContext context, FileStamp stamp, string? rootPem, CertificateAction action, DateTimeOffset now)
    {
        X509Certificate2 certificate = context.TargetCertificate;
        Volatile.Write(ref _context, context);
        Volatile.Write(ref _rootPem, rootPem);
        _stamp = stamp;

        bool expiresSoon = rootPem is null
            && now >= Utc(certificate.NotAfter) - RenewBefore
            && certificate.Thumbprint != _warnedThumbprint;

        if (expiresSoon)
        {
            _warnedThumbprint = certificate.Thumbprint;
        }

        return new CertificateCheck(action, certificate, rootPem is not null, expiresSoon, null);
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

    // The certificate with its key, and the intermediates that follow it in its file, as Kestrel sends them from its
    // own PEM files.
    private SslStreamCertificateContext? TryLoad(out string? problem)
    {
        try
        {
            string certificatePem = File.ReadAllText(Files.CertificatePath);
            X509Certificate2 certificate = Servable(X509Certificate2.CreateFromPem(certificatePem, File.ReadAllText(Files.KeyPath)));
            X509Certificate2Collection chain = [];
            chain.ImportFromPem(certificatePem);
            problem = null;

            return SslStreamCertificateContext.Create(certificate, chain);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            problem = exception.Message;

            return null;
        }
    }

    // SChannel cannot serve a key that exists only in memory, as one read from PEM does, so on Windows it goes through
    // PKCS#12, as Kestrel does with its own PEM files.
    private static X509Certificate2 Servable(X509Certificate2 certificate)
    {
        if (!OperatingSystem.IsWindows())
        {
            return certificate;
        }

        using (certificate)
        {
            return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), null);
        }
    }

    private static bool IsLegacy(X509Certificate2 certificate) =>
        certificate.SubjectName.Name == LegacySubject
        && certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData)
        && certificate.Extensions.OfType<X509BasicConstraintsExtension>().All(constraints => !constraints.CertificateAuthority);

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

    private static DateTimeOffset Utc(DateTime local) => new(local.ToUniversalTime());

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
