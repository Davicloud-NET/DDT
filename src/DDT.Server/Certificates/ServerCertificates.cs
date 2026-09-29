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

// The certificate Kestrel serves. DDT's own is issued from the root that boot images pin, so it can change without a
// new boot image. An administrator's is reloaded when its files change and is never replaced.
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

    // With generate set, DDT may create a root and issue certificates from it. Without it, DDT only serves the files as
    // they are.
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

    // Raised after a provisional pair was rolled back to the pair before it.
    public event EventHandler<CertificateRolledBackEventArgs>? RolledBack;

    public CertificateFiles Files { get; }

    public IReadOnlyList<string> Names { get; }

    public X509Certificate2? Current => Context?.TargetCertificate;

    // The certificate plus the intermediates from its file. Clients such as the agent don't download intermediates.
    public SslStreamCertificateContext? Context => _served.Context;

    // Holds DDT's root while the served certificate comes from it. It's null for an administrator's certificate.
    public string? RootCertificatePem => _served.RootPem;

    // Null while nothing waits for a confirmation.
    public ProvisionalCertificate? Provisional => _provisional.Current;

    // True when DDT may issue certificates, from its existing root or from a new one it creates.
    public bool CanGenerate => _generate;

    public bool HasRoot => _root.Exists;

    // Throws when there's nothing to serve at all. At startup that stops the host and shows the reason.
    public async Task<CertificateCheck> CheckAsync(CancellationToken cancellationToken)
    {
        await using (await _gate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            await _provisional.ResumeAsync(cancellationToken).ConfigureAwait(false);
            await _provisional.RecoverPreviousAsync(cancellationToken).ConfigureAwait(false);

            // An administrator's certificate may sit where DDT can't write, such as a read-only mount. So DDT only
            // takes the lock in a folder that holds its root, or once a check finds something to write.
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

    // Returns the SHA-256 of the certificate older boot images pin, or null once no boot image waits for a rebuild.
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

    // True when DDT's root issued the certificate. Boot images pin that root, so they accept it without a rebuild.
    public bool ChainsToRoot(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return _root.Issued(certificate);
    }

    // Serves an administrator's pair at once, but only provisionally. The caller has already checked the pair.
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

    // Issues a pair from DDT's root for the given names and serves it provisionally. The root is created first if
    // there's none yet.
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

    // Confirms the provisional pair only from a connection that was served it, which proves its browser accepted it.
    // Any other connection gets NotServedTheNewPair.
    public async Task<CertificateConfirmation> ConfirmAsync(string? servedThumbprint, CancellationToken cancellationToken)
    {
        await using (await _gate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            return _provisional.Confirm(servedThumbprint);
        }
    }

    // Puts the pair before the provisional one back in place, both on disk and in memory.
    public async Task<CertificateCheck?> RollBackAsync(string thumbprint, CancellationToken cancellationToken)
    {
        CertificateCheck? check;

        await using (await _gate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            // The pair was confirmed or replaced in the meantime.
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

    // Returns null when something needs writing but the files aren't locked. The caller checks again under the lock.
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

        // The legacy certificate's only names are its subject alternative names, and the new certificate keeps them.
        // Boot images that pin it break, and that can't be avoided. It isn't a CA, so nothing new can chain to it.
        if (_generate && root is null && loaded is not null && CertificateChains.IsLegacy(loaded.TargetCertificate))
        {
            return locked ? Migrate(loaded.TargetCertificate, now) : null;
        }

        if (root is not null)
        {
            using X509Certificate2 rootCertificate = X509Certificate2.CreateFromPem(root.CertificatePem);

            // DDT only reissues its own pair when it doesn't load. An administrator's half-copied pair, or one whose
            // key needs a password, stays as it is.
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

        // Don't renew if the root expires first. Renewing would only issue the same end date again.
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

    // The certificate served before stays in service. DDT doesn't try the files again until they change once more.
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

    // The new certificate keeps the names of the one it replaces, so a renewal never removes a name something still
    // uses. Addresses are the host's current ones.
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

    // Checks only the certificate file, because it doesn't load together with its key.
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

    // Changed is true when the files differ from the ones the served pair came from, or when nothing is served yet.
    private sealed record LoadedPair(SslStreamCertificateContext? Context, FileStamp Stamp, bool Changed);
}
