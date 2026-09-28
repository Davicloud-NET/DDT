// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DDT.Contracts.Server;
using DDT.Server.Images;

namespace DDT.Server.Certificates;

// The certificate Kestrel serves: DDT's own, from the root boot images pin, so it can change without a new boot image,
// or an administrator's, loaded again when its files change and never replaced.
public sealed class ServerCertificates
{
    public static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);

    public static readonly TimeSpan ConfirmWithin = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly bool _generate;
    private readonly TimeProvider _timeProvider;
    private readonly CertificateFileLock _fileLock;
    private readonly CertificateRoot _root;
    private readonly ServedCertificate _served = new();
    private readonly ProvisionalPairs _provisional;

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
        _fileLock = new CertificateFileLock(files, timeProvider);
        _root = new CertificateRoot(files);
        _provisional = new ProvisionalPairs(files, _served, _root, _fileLock, timeProvider, thumbprint => RollBackAsync(thumbprint, CancellationToken.None));
    }

    // Raised after a provisional pair went back to the pair before it.
    public event EventHandler<CertificateRolledBackEventArgs>? RolledBack;

    public CertificateFiles Files { get; }

    public IReadOnlyList<string> Names { get; }

    public X509Certificate2? Current => Context?.TargetCertificate;

    // The certificate with the intermediates its file holds, which clients such as the agent do not download.
    public SslStreamCertificateContext? Context => _served.Context;

    // DDT's root while the served certificate comes from it, and null for an administrator's certificate.
    public string? RootCertificatePem => _served.RootPem;

    // Null while nothing waits for a confirmation.
    public ProvisionalCertificate? Provisional => _provisional.Current;

    // DDT can make or has a root to issue from.
    public bool CanGenerate => _generate;

    public bool HasRoot => _root.Exists;

    // Throws when there is nothing to serve at all, which at startup stops the host with the reason.
    public async Task<CertificateCheck> CheckAsync(CancellationToken cancellationToken)
    {
        await using (await _gate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            await _provisional.ResumeAsync(cancellationToken).ConfigureAwait(false);
            await _provisional.RecoverPreviousAsync(cancellationToken).ConfigureAwait(false);

            // An administrator's certificate may sit where DDT cannot write, such as a read-only mount, so the lock is
            // taken only in a folder that holds DDT's root, or once a check finds something to write.
            if ((!_generate || !File.Exists(Files.RootPath)) && Check(_timeProvider.GetUtcNow(), locked: false) is { } check)
            {
                return check;
            }

            Directory.CreateDirectory(Files.Folder);

            using FileStream fileLock = await _fileLock.TakeAsync(cancellationToken).ConfigureAwait(false);

            return Check(_timeProvider.GetUtcNow(), locked: true)!;
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
        DateTimeOffset notAfter = CertificateChains.Utc(current.NotAfter);
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
            root is null ? null : CertificateChains.Utc(root.NotAfter),
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

    // Whether the certificate comes from DDT's root, which boot images pin, so they accept it with no rebuild.
    public bool ChainsToRoot(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return _root.Issued(certificate);
    }

    // An administrator's pair, checked by the caller, served at once and provisionally.
    public async Task<CertificateCheck> InstallAsync(PemPair pair, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pair);

        await using (await _gate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            Directory.CreateDirectory(Files.Folder);

            using FileStream fileLock = await _fileLock.TakeAsync(cancellationToken).ConfigureAwait(false);

            return _provisional.Install(pair, _timeProvider.GetUtcNow());
        }
    }

    // A pair from DDT's root for the names given, the root made first when there is none yet, served provisionally.
    public async Task<CertificateCheck> GenerateAsync(IEnumerable<string> names, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(names);

        if (!_generate)
        {
            throw new InvalidOperationException("DDT:Https:GenerateSelfSignedCertificate is false, so DDT issues no certificate.");
        }

        await using (await _gate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            Directory.CreateDirectory(Files.Folder);

            using FileStream fileLock = await _fileLock.TakeAsync(cancellationToken).ConfigureAwait(false);

            DateTimeOffset now = _timeProvider.GetUtcNow();
            PemPair root = _root.Read() ?? _root.Create(now);

            return _provisional.Install(ServerCertificateAuthority.Issue(root, names, ServerNames.LocalAddresses(), now), now);
        }
    }

    // Only from a connection that was served the provisional pair: its browser accepted it.
    public async Task<CertificateConfirmation> ConfirmAsync(string? servedThumbprint, CancellationToken cancellationToken)
    {
        await using (await _gate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            return _provisional.Confirm(servedThumbprint);
        }
    }

    // The pair before the provisional one goes back into place, as its files and in memory.
    public async Task<CertificateCheck?> RollBackAsync(string thumbprint, CancellationToken cancellationToken)
    {
        CertificateCheck? check;

        await using (await _gate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            // Confirmed, or replaced by another pair, in the meantime.
            if (_provisional.Current?.Thumbprint != thumbprint)
            {
                return null;
            }

            using FileStream fileLock = await _fileLock.TakeAsync(cancellationToken).ConfigureAwait(false);

            check = _provisional.RestorePrevious(_timeProvider.GetUtcNow());
            _provisional.End();
        }

        if (check is not null)
        {
            RolledBack?.Invoke(this, new CertificateRolledBackEventArgs(thumbprint, check.Certificate));
        }

        return check;
    }

    // Null when something is due to be written but the files are not locked, to be checked again under the lock.
    private CertificateCheck? Check(DateTimeOffset now, bool locked)
    {
        PemPair? root = _generate ? _root.Read() : null;
        FileStamp stamp = FileStamp.Of(Files);
        bool changed = _served.Context is null || stamp != _served.Stamp;
        string? problem = null;
        SslStreamCertificateContext? loaded = changed ? CertificateChains.TryLoad(Files, out problem) : _served.Context;

        if (_generate && root is null && loaded is null && !stamp.AnyExists)
        {
            return locked ? Replace(_root.Create(now), null, CertificateAction.Created, now) : null;
        }

        // Its only names are its subject alternative names, which the new certificate keeps. The break for boot images
        // that pin it cannot be avoided: it is no CA, so nothing new can chain to it.
        if (_generate && root is null && loaded is not null && CertificateChains.IsLegacy(loaded.TargetCertificate))
        {
            return locked ? Migrate(loaded.TargetCertificate, now) : null;
        }

        if (root is not null)
        {
            using X509Certificate2 rootCertificate = X509Certificate2.CreateFromPem(root.CertificatePem);

            // Only DDT's own pair is issued again when it does not load. An administrator's half copied pair, or one
            // whose key needs a password, stays as it is.
            bool fromRoot = loaded is null
                ? !stamp.AnyExists || CertificateFileChainsTo(rootCertificate)
                : CertificateChains.ChainsTo(loaded.TargetCertificate, rootCertificate);

            if (fromRoot)
            {
                return CheckOwnRoot(root, rootCertificate, new LoadedPair(loaded, stamp, changed), locked, now);
            }
        }

        return loaded is null ? LoadFailed(stamp, problem) : _served.Serve(loaded, stamp, null, LoadAction(changed), now);
    }

    private CertificateCheck? CheckOwnRoot(PemPair root, X509Certificate2 rootCertificate, LoadedPair loaded, bool locked, DateTimeOffset now)
    {
        if (loaded.Context is not { } context)
        {
            return locked ? Replace(root, null, CertificateAction.Issued, now) : null;
        }

        X509Certificate2 pair = context.TargetCertificate;

        // Unless the root itself ends first, when renewing would only issue the same end date again.
        if (now >= CertificateChains.Utc(pair.NotAfter) - RenewBefore && rootCertificate.NotAfter > pair.NotAfter)
        {
            return locked ? Replace(root, pair, CertificateAction.Renewed, now) : null;
        }

        if (!ServerNames.Covers(pair, Names))
        {
            return locked ? Replace(root, pair, CertificateAction.Reissued, now) : null;
        }

        return _served.Serve(context, loaded.Stamp, root.CertificatePem, LoadAction(loaded.Changed), now);
    }

    // The certificate served before stays, and the files are not tried again until they change once more.
    private CertificateCheck LoadFailed(FileStamp stamp, string? problem)
    {
        if (_served.Context is not { } context)
        {
            throw new InvalidOperationException(
                $"Cannot load the server certificate {Files.CertificatePath} with its key {Files.KeyPath}: {problem} " +
                (_generate
                    ? "Fix or replace both files, or delete both to have DDT issue its own."
                    : "Fix or replace both files, or delete both and set DDT:Https:GenerateSelfSignedCertificate to true " +
                        "to have DDT issue its own."));
        }

        _served.Stamp = stamp;

        return new CertificateCheck(CertificateAction.LoadFailed, context.TargetCertificate, RootCertificatePem is not null, false, problem);
    }

    private CertificateCheck Migrate(X509Certificate2 legacy, DateTimeOffset now)
    {
        PemPair root = _root.Create(now);
        PemFiles.Write(Files.ReplacedAnchorPath, legacy.ExportCertificatePem(), isKey: false);

        return Replace(root, legacy, CertificateAction.Migrated, now);
    }

    private CertificateAction LoadAction(bool changed) =>
        !changed ? CertificateAction.Unchanged : _served.Context is null ? CertificateAction.Loaded : CertificateAction.Reloaded;

    // Names in the replaced certificate stay, so a renewal never takes away a name something still uses. Addresses are
    // the host's current ones.
    private CertificateCheck Replace(PemPair root, X509Certificate2? replaced, CertificateAction action, DateTimeOffset now)
    {
        IEnumerable<string> names = replaced is null
            ? Names
            : Names.Concat(ServerNames.Of(replaced).Where(name => !IPAddress.TryParse(name, out _)))
                .Distinct(StringComparer.OrdinalIgnoreCase);

        PemPair issued = ServerCertificateAuthority.Issue(root, names, ServerNames.LocalAddresses(), now);

        CertificateChains.KeepAsPrevious(Files.CertificatePath, Files.PreviousCertificatePath);
        CertificateChains.KeepAsPrevious(Files.KeyPath, Files.PreviousKeyPath);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Files.KeyPath))!);
        PemFiles.Write(Files.KeyPath, issued.KeyPem, isKey: true);
        PemFiles.Write(Files.CertificatePath, issued.CertificatePem, isKey: false);

        SslStreamCertificateContext context = CertificateChains.TryLoad(Files, out string? problem)
            ?? throw new InvalidOperationException($"The certificate DDT just wrote to {Files.CertificatePath} does not load: {problem}");

        return _served.Serve(context, FileStamp.Of(Files), root.CertificatePem, action, now);
    }

    // The certificate file on its own, without the key it does not load with.
    private bool CertificateFileChainsTo(X509Certificate2 root)
    {
        try
        {
            using X509Certificate2 certificate = X509Certificate2.CreateFromPem(File.ReadAllText(Files.CertificatePath));

            return CertificateChains.ChainsTo(certificate, root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return false;
        }
    }

    // Changed: the files differ from those the served pair came from, or nothing is served yet.
    private sealed record LoadedPair(SslStreamCertificateContext? Context, FileStamp Stamp, bool Changed);
}
