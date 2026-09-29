// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;

namespace DDT.Server.Images;

// One row per image index of an uploaded file, so several rows can share one stored file.
public sealed class Image
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public ImageKind Kind { get; set; }

    // Lower case hex, which is also the stored file's name.
    public required string Sha256 { get; set; }

    public long SizeBytes { get; set; }

    public int WimIndex { get; set; }

    public string? Edition { get; set; }

    public string? Architecture { get; set; }

    public string? Version { get; set; }

    public string? Language { get; set; }

    public long InstalledBytes { get; set; }

    public string? OriginalFileName { get; set; }

    public DateTimeOffset UploadedUtc { get; set; }

    public Guid? UploadedByUserId { get; set; }

    public string? UploadedByName { get; set; }

    // Only set for raw disk images. They hold whether the image starts with Secure Boot on, the sentence that says why,
    // and the SHA-256 of the uncompressed disk. That hash finds the same disk uploaded again in another format.
    public ImageBootCapability? BootCapability { get; set; }

    // For an image signed for Secure Boot, this says which of Microsoft's third-party UEFI CAs its boot file chains to.
    public UefiCa? SignedUnder { get; set; }

    public string? BootDetail { get; set; }

    public string? SourceSha256 { get; set; }
}
