// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Net.Security;

namespace DDT.Server.Certificates;

// Outside Development the host sends HSTS. A browser then refuses a new pair it doesn't trust, and the user can't click
// through. So a pair the page installs is rolled back unless a connection that was served it confirms it in time. The
// deadline file survives a restart. Callers hold the file lock for Install and RestorePrevious.
internal sealed class ProvisionalPairs(
    CertificateFiles files,
    ServedCertificate served,
    CertificateRoot root,
    CertificateFileLock fileLock,
    TimeProvider timeProvider,
    Func<string, Task> rollBack)
{
    private ProvisionalCertificate? _provisional;
    private ITimer? _deadline;

    // Null while nothing waits for a confirmation.
    public ProvisionalCertificate? Current => Volatile.Read(ref _provisional);

    // The previous pair's files are only kept for the first of several provisional pairs. That way a rollback
    // returns to the last confirmed pair, not to another provisional one.
    public CertificateCheck Install(PemPair pair, DateTimeOffset now)
    {
        if (_provisional is null)
        {
            CertificateChains.KeepAsPrevious(files.CertificatePath, files.PreviousCertificatePath);
            CertificateChains.KeepAsPrevious(files.KeyPath, files.PreviousKeyPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(files.KeyPath))!);
        PemFiles.Write(files.KeyPath, pair.KeyPem, isKey: true);
        PemFiles.Write(files.CertificatePath, pair.CertificatePem, isKey: false);

        SslStreamCertificateContext context = CertificateChains.TryLoad(files, out string? problem)
            ?? throw new InvalidOperationException($"The certificate written to {files.CertificatePath} does not load: {problem}");

        CertificateCheck check = served.Serve(context, FileStamp.Of(files), root.Of(context.TargetCertificate), CertificateAction.Installed, now);
        ProvisionalCertificate provisional = new(context.TargetCertificate.Thumbprint, now + ServerCertificates.ConfirmWithin);

        File.WriteAllText(
            files.ProvisionalPath,
            string.Create(CultureInfo.InvariantCulture, $"{provisional.DeadlineUtc:O}\n{provisional.Thumbprint}\n"));
        Arm(provisional, ServerCertificates.ConfirmWithin);

        return check;
    }

    public CertificateConfirmation Confirm(string? servedThumbprint)
    {
        if (_provisional is not { } provisional)
        {
            return CertificateConfirmation.NothingToConfirm;
        }

        if (!string.Equals(servedThumbprint, provisional.Thumbprint, StringComparison.OrdinalIgnoreCase))
        {
            return CertificateConfirmation.NotServedTheNewPair;
        }

        End();

        return CertificateConfirmation.Confirmed;
    }

    // Puts the previous pair back in place, both on disk and in memory. Returns null if there's no previous pair, and
    // the provisional pair stays in service.
    public CertificateCheck? RestorePrevious(DateTimeOffset now)
    {
        if (!File.Exists(files.PreviousCertificatePath) || !File.Exists(files.PreviousKeyPath))
        {
            return null;
        }

        File.Move(files.PreviousKeyPath, files.KeyPath, overwrite: true);
        File.Move(files.PreviousCertificatePath, files.CertificatePath, overwrite: true);

        return CertificateChains.TryLoad(files, out _) is { } context
            ? served.Serve(context, FileStamp.Of(files), root.Of(context.TargetCertificate), CertificateAction.RolledBack, now)
            : null;
    }

    public void End()
    {
        _deadline?.Dispose();
        _deadline = null;
        Volatile.Write(ref _provisional, null);
        File.Delete(files.ProvisionalPath);
    }

    // After a restart, a deadline that passed in the meantime rolls back now. A deadline still ahead is kept.
    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        if (_provisional is not null || !File.Exists(files.ProvisionalPath))
        {
            return;
        }

        string[] lines = (await File.ReadAllTextAsync(files.ProvisionalPath, cancellationToken).ConfigureAwait(false))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        DateTimeOffset now = timeProvider.GetUtcNow();

        if (lines.Length < 2 || !DateTimeOffset.TryParse(lines[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset deadline))
        {
            File.Delete(files.ProvisionalPath);

            return;
        }

        if (deadline > now)
        {
            ProvisionalCertificate provisional = new(lines[1], deadline);
            Arm(provisional, deadline - now);

            return;
        }

        using FileStream held = await fileLock.TakeAsync(cancellationToken).ConfigureAwait(false);

        _ = RestorePrevious(now);
        File.Delete(files.ProvisionalPath);
    }

    // At startup, a certificate and key that don't load together, such as a pair half replaced by hand, are replaced by
    // the previous pair. Every replacement keeps one.
    public async Task RecoverPreviousAsync(CancellationToken cancellationToken)
    {
        if (served.Context is not null
            || !File.Exists(files.PreviousCertificatePath)
            || !File.Exists(files.PreviousKeyPath)
            || CertificateChains.TryLoad(files, out _) is not null
            || !CertificateChains.Loads(files.PreviousCertificatePath, files.PreviousKeyPath))
        {
            return;
        }

        using FileStream held = await fileLock.TakeAsync(cancellationToken).ConfigureAwait(false);

        File.Move(files.PreviousKeyPath, files.KeyPath, overwrite: true);
        File.Move(files.PreviousCertificatePath, files.CertificatePath, overwrite: true);
    }

    private void Arm(ProvisionalCertificate provisional, TimeSpan due)
    {
        _deadline?.Dispose();
        Volatile.Write(ref _provisional, provisional);
        _deadline = timeProvider.CreateTimer(
            _ => _ = rollBack(provisional.Thumbprint),
            null,
            due,
            Timeout.InfiniteTimeSpan);
    }
}
