// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Certificates;

// Held while the certificate files change. Other DDT processes on the same store hold it only for a check, which takes
// well under a second.
internal sealed class CertificateFileLock(CertificateFiles files, TimeProvider timeProvider)
{
    private const int MaxAttempts = 300;

    private static readonly TimeSpan s_retry = TimeSpan.FromMilliseconds(100);

    public async Task<FileStream> TakeAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(files.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < MaxAttempts)
            {
                await Task.Delay(s_retry, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
