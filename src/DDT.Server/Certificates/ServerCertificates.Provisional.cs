// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace DDT.Server.Certificates;

// A pair the settings page installs is provisional. Outside Development the host sends HSTS, so a browser that trusted
// the old certificate refuses a new one it does not trust, with no way to click through: the page could lock out the
// very administrator who installed it. Unless someone confirms the pair within 5 minutes, from a connection that was
// served it and so proves that a browser accepts it, DDT goes back to the pair before it. The deadline is kept in a file,
// so a restart in between does not make the pair permanent.
public sealed partial class ServerCertificates
{
    public static readonly TimeSpan ConfirmWithin = TimeSpan.FromMinutes(5);

    private ProvisionalCertificate? _provisional;
    private ITimer? _deadline;

    // Raised after a provisional pair went back to the pair before it.
    public event EventHandler<CertificateRolledBackEventArgs>? RolledBack;

    // Null while nothing waits for a confirmation.
    public ProvisionalCertificate? Provisional => Volatile.Read(ref _provisional);

    // DDT can make or has a root to issue from.
    public bool CanGenerate => _generate;

    public bool HasRoot => File.Exists(Files.RootPath);

    // Whether the certificate comes from DDT's root, which boot images pin, so they accept it with no rebuild.
    public bool ChainsToRoot(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        if (!HasRoot)
        {
            return false;
        }

        using X509Certificate2 root = X509Certificate2.CreateFromPem(File.ReadAllText(Files.RootPath));

        return ChainsTo(certificate, root);
    }

    // An administrator's pair, checked by the caller, served at once and provisionally.
    public async Task<CertificateCheck> InstallAsync(PemPair pair, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pair);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(Files.Folder);

            using FileStream fileLock = await LockAsync(cancellationToken).ConfigureAwait(false);

            return Install(pair, _timeProvider.GetUtcNow());
        }
        finally
        {
            _gate.Release();
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

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(Files.Folder);

            using FileStream fileLock = await LockAsync(cancellationToken).ConfigureAwait(false);

            DateTimeOffset now = _timeProvider.GetUtcNow();
            PemPair root = ReadRoot() ?? CreateRoot(now);

            return Install(ServerCertificateAuthority.Issue(root, names, ServerNames.LocalAddresses(), now), now);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Only from a connection that was served the provisional pair: its browser accepted it.
    public async Task<CertificateConfirmation> ConfirmAsync(string? servedThumbprint, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_provisional is not { } provisional)
            {
                return CertificateConfirmation.NothingToConfirm;
            }

            if (!string.Equals(servedThumbprint, provisional.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return CertificateConfirmation.NotServedTheNewPair;
            }

            EndProvisional();

            return CertificateConfirmation.Confirmed;
        }
        finally
        {
            _gate.Release();
        }
    }

    // The pair before the provisional one goes back into place, as its files and in memory.
    public async Task<CertificateCheck?> RollBackAsync(string thumbprint, CancellationToken cancellationToken)
    {
        CertificateCheck? check;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Confirmed, or replaced by another pair, in the meantime.
            if (_provisional?.Thumbprint != thumbprint)
            {
                return null;
            }

            using FileStream fileLock = await LockAsync(cancellationToken).ConfigureAwait(false);

            check = RestorePrevious(_timeProvider.GetUtcNow());
            EndProvisional();
        }
        finally
        {
            _gate.Release();
        }

        if (check is not null)
        {
            RolledBack?.Invoke(this, new CertificateRolledBackEventArgs(thumbprint, check.Certificate));
        }

        return check;
    }

    // The files of the pair before are kept once, for the first of several provisional pairs, so a rollback goes back to
    // the pair that was confirmed last rather than to another provisional one.
    private CertificateCheck Install(PemPair pair, DateTimeOffset now)
    {
        if (_provisional is null)
        {
            KeepAsPrevious(Files.CertificatePath, Files.PreviousCertificatePath);
            KeepAsPrevious(Files.KeyPath, Files.PreviousKeyPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Files.KeyPath))!);
        PemFiles.Write(Files.KeyPath, pair.KeyPem, isKey: true);
        PemFiles.Write(Files.CertificatePath, pair.CertificatePem, isKey: false);

        SslStreamCertificateContext context = TryLoad(out string? problem)
            ?? throw new InvalidOperationException($"The certificate written to {Files.CertificatePath} does not load: {problem}");

        CertificateCheck check = Serve(context, FileStamp.Of(Files), RootOf(context.TargetCertificate), CertificateAction.Installed, now);
        ProvisionalCertificate provisional = new(context.TargetCertificate.Thumbprint, now + ConfirmWithin);

        File.WriteAllText(
            Files.ProvisionalPath,
            string.Create(CultureInfo.InvariantCulture, $"{provisional.DeadlineUtc:O}\n{provisional.Thumbprint}\n"));
        Arm(provisional, ConfirmWithin);

        return check;
    }

    // Null when there is no pair to go back to, which leaves the provisional pair in service.
    private CertificateCheck? RestorePrevious(DateTimeOffset now)
    {
        if (!File.Exists(Files.PreviousCertificatePath) || !File.Exists(Files.PreviousKeyPath))
        {
            return null;
        }

        File.Move(Files.PreviousKeyPath, Files.KeyPath, overwrite: true);
        File.Move(Files.PreviousCertificatePath, Files.CertificatePath, overwrite: true);

        return TryLoad(out _) is { } context
            ? Serve(context, FileStamp.Of(Files), RootOf(context.TargetCertificate), CertificateAction.RolledBack, now)
            : null;
    }

    // After a restart: a deadline that passed meanwhile rolls back now, and one still ahead is kept.
    private async Task ResumeProvisionalAsync(CancellationToken cancellationToken)
    {
        if (_provisional is not null || !File.Exists(Files.ProvisionalPath))
        {
            return;
        }

        string[] lines = (await File.ReadAllTextAsync(Files.ProvisionalPath, cancellationToken).ConfigureAwait(false))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (lines.Length < 2 || !DateTimeOffset.TryParse(lines[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset deadline))
        {
            File.Delete(Files.ProvisionalPath);

            return;
        }

        if (deadline > now)
        {
            ProvisionalCertificate provisional = new(lines[1], deadline);
            Arm(provisional, deadline - now);

            return;
        }

        using FileStream fileLock = await LockAsync(cancellationToken).ConfigureAwait(false);

        _ = RestorePrevious(now);
        File.Delete(Files.ProvisionalPath);
    }

    // At startup, a certificate and key that do not load together, such as a pair half replaced by hand, give way to the
    // previous pair, which every replacement keeps.
    private async Task RecoverPreviousAsync(CancellationToken cancellationToken)
    {
        if (_context is not null
            || !File.Exists(Files.PreviousCertificatePath)
            || !File.Exists(Files.PreviousKeyPath)
            || TryLoad(out _) is not null
            || !Loads(Files.PreviousCertificatePath, Files.PreviousKeyPath))
        {
            return;
        }

        using FileStream fileLock = await LockAsync(cancellationToken).ConfigureAwait(false);

        File.Move(Files.PreviousKeyPath, Files.KeyPath, overwrite: true);
        File.Move(Files.PreviousCertificatePath, Files.CertificatePath, overwrite: true);
    }

    private static bool Loads(string certificatePath, string keyPath)
    {
        try
        {
            using X509Certificate2 pair = X509Certificate2.CreateFromPem(File.ReadAllText(certificatePath), File.ReadAllText(keyPath));

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private void Arm(ProvisionalCertificate provisional, TimeSpan due)
    {
        _deadline?.Dispose();
        Volatile.Write(ref _provisional, provisional);
        _deadline = _timeProvider.CreateTimer(
            _ => _ = RollBackAsync(provisional.Thumbprint, CancellationToken.None),
            null,
            due,
            Timeout.InfiniteTimeSpan);
    }

    private void EndProvisional()
    {
        _deadline?.Dispose();
        _deadline = null;
        Volatile.Write(ref _provisional, null);
        File.Delete(Files.ProvisionalPath);
    }

    // DDT's root while the certificate comes from it, which is what boot images pin.
    private string? RootOf(X509Certificate2 certificate)
    {
        if (!HasRoot)
        {
            return null;
        }

        string rootPem = File.ReadAllText(Files.RootPath);
        using X509Certificate2 root = X509Certificate2.CreateFromPem(rootPem);

        return ChainsTo(certificate, root) ? rootPem : null;
    }
}

// Thumbprint names the pair, as the connection it was served on records it.
public sealed record ProvisionalCertificate(string Thumbprint, DateTimeOffset DeadlineUtc);

public enum CertificateConfirmation
{
    Confirmed,
    NothingToConfirm,

    // The connection that asked was served another pair, so it proves nothing about the new one.
    NotServedTheNewPair,
}

public sealed class CertificateRolledBackEventArgs(string rolledBackThumbprint, X509Certificate2 restored) : EventArgs
{
    public string RolledBackThumbprint { get; } = rolledBackThumbprint;

    public X509Certificate2 Restored { get; } = restored;
}
