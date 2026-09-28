// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // Packages and images in the library.

    public static readonly MessageTemplate PackageTargetsMissing = Define(
        "package.targetsMissing",
        "Send the list of targets, empty for none.");

    public static readonly MessageTemplate PackageFilesHaveNoTargets = Define(
        "package.filesHaveNoTargets",
        "A Files package is unpacked for the Run script steps that name it, not by the machine's model, so it has no targets.");

    public static readonly MessageTemplate PackageTooManyTargets = Define(
        "package.tooManyTargets",
        "A package can have at most {max} targets.");

    public static readonly MessageTemplate PackageTargetEmpty = Define("package.targetEmpty", "A target is empty.");

    public static readonly MessageTemplate PackageTargetTwice = Define("package.targetTwice", "{model} is a target twice.");

    public static readonly MessageTemplate PackageBootImageDriversOnly = Define(
        "package.bootImageDriversOnly",
        "Only a driver package can go into the Windows PE boot image.");

    public static readonly MessageTemplate PackageInUse = Define(
        "package.inUse",
        "Machines are waiting to install this package or are installing it. Cancel those runs or let them finish, then delete it.");

    public static readonly MessageTemplate PackageFileMissing = Define(
        "package.fileMissing",
        "The package's file is missing from the server's library. Upload it again.");

    public static readonly MessageTemplate ImageInUse = Define(
        "image.inUse",
        "Runs that are assigned or running use this image. Cancel them or let them finish, then delete it.");

    // Uploads of images and packages.

    public static readonly MessageTemplate UploadFileName = Define(
        "upload.fileName",
        "The file name must have 1 to {max} characters and no control characters.");

    public static readonly MessageTemplate UploadKind = Define("upload.kind", "Choose an image, a driver package or a files package.");

    public static readonly MessageTemplate UploadLength = Define(
        "upload.length",
        "The file must not be empty and must fit on the server's store volume.");

    public static readonly MessageTemplate UploadNoSpace = Define(
        "upload.noSpace",
        "The image store needs {required} free for this upload but has {available}. Free space on the server's store volume or " +
        "discard unfinished uploads, then try again.");

    public static readonly MessageTemplate UploadContentLength = Define(
        "upload.contentLength",
        "Send every chunk with a Content-Length header.");

    public static readonly MessageTemplate UploadChunkSize = Define(
        "upload.chunkSize",
        "A chunk holds 1 to {max} bytes. Send the file in smaller chunks.");

    public static readonly MessageTemplate UploadOffsetHeader = Define(
        "upload.offsetHeader",
        "Send the position of the chunk in the file in the Upload-Offset header.");

    public static readonly MessageTemplate UploadBeyondLength = Define(
        "upload.beyondLength",
        "This chunk ends past the end of the file. Upload the file this session was created for.");

    public static readonly MessageTemplate UploadComplete = Define(
        "upload.complete",
        "This upload is complete. Nothing more needs to be sent.");

    public static readonly MessageTemplate UploadChunkBusy = Define(
        "upload.chunkBusy",
        "Another request is using this upload. Wait a few seconds, then send the chunk again.");

    public static readonly MessageTemplate UploadOffsetMismatch = Define(
        "upload.offsetMismatch",
        "The server holds a different part of this file. Continue from the offset in the Upload-Offset header.");

    public static readonly MessageTemplate UploadRestarted = Define(
        "upload.restarted",
        "The server lost part of this upload. Send the file again from the start.");

    public static readonly MessageTemplate UploadCutOff = Define(
        "upload.cutOff",
        "The chunk ended before all of it arrived. Send it again.");

    public static readonly MessageTemplate UploadStalled = Define(
        "upload.stalled",
        "The chunk stopped arriving for too long. Send it again.");

    public static readonly MessageTemplate UploadDiskFull = Define(
        "upload.diskFull",
        "The image store is full. Free space on the server's store volume, then continue the upload.");

    public static readonly MessageTemplate UploadGone = Define(
        "upload.gone",
        "This upload no longer exists. Select the file again to upload it.");

    public static readonly MessageTemplate UploadBeingChecked = Define(
        "upload.beingChecked",
        "This upload is being checked or written to. Ask again in a few seconds.");

    public static readonly MessageTemplate UploadIncomplete = Define(
        "upload.incomplete",
        "Not all of the file has arrived. Continue the upload from the offset in the Upload-Offset header.");

    public static readonly MessageTemplate UploadFailed = Define(
        "upload.failed",
        "The upload could not be added to the library. Look at the server log, then complete the upload again.");

    public static readonly MessageTemplate UploadServerStopping = Define(
        "upload.serverStopping",
        "The server is stopping. Complete the upload again once it is back.");

    public static readonly MessageTemplate UploadInUse = Define("upload.inUse", "This upload is in use. Try again in a few seconds.");

    // Why an uploaded file cannot go into the library: WIM images, disk images and zip packages.

    public static readonly MessageTemplate WimNotAWim = Define("wim.notAWim", "This file is not a WIM image.");

    public static readonly MessageTemplate WimPipable = Define(
        "wim.pipable",
        "Pipable WIM files are not supported. Export the image into a regular WIM first.");

    public static readonly MessageTemplate WimVersion = Define(
        "wim.version",
        "This WIM file uses format version 0x{version}, which DDT does not support.");

    public static readonly MessageTemplate WimSplit = Define(
        "wim.split",
        "Split WIM files (.swm) are not supported. Export the image into a single WIM first.");

    public static readonly MessageTemplate WimCompressedList = Define(
        "wim.compressedList",
        "This WIM file stores its image list compressed, which DDT cannot read.");

    public static readonly MessageTemplate WimIncomplete = Define("wim.incomplete", "This WIM file is incomplete or damaged.");

    public static readonly MessageTemplate WimListTooLarge = Define("wim.listTooLarge", "The image list in this WIM file is too large.");

    public static readonly MessageTemplate WimListNotUtf16 = Define(
        "wim.listNotUtf16",
        "The image list in this WIM file is not UTF-16 text, which DDT cannot read.");

    public static readonly MessageTemplate WimListDamaged = Define("wim.listDamaged", "The image list in this WIM file is damaged.");

    public static readonly MessageTemplate WimEncrypted = Define(
        "wim.encrypted",
        "This image is encrypted (an ESD from Windows Update) and cannot be applied.");

    public static readonly MessageTemplate WimListMismatch = Define(
        "wim.listMismatch",
        "The image list in this WIM file does not match its header.");

    public static readonly MessageTemplate WimNoX64Image = Define("wim.noX64Image", "This WIM holds no x64 Windows image.");

    public static readonly MessageTemplate GptNoTable = Define(
        "gpt.noTable",
        "The file has no GUID partition table, so it is not a UEFI disk image. Upload a disk image such as a distribution's cloud image.");

    public static readonly MessageTemplate GptFourKilobyteSectors = Define(
        "gpt.fourKilobyteSectors",
        "The disk image is made for disks with 4 KiB sectors. DDT writes images for disks with 512-byte sectors, which is what " +
        "distributions publish.");

    public static readonly MessageTemplate GptDamaged = Define("gpt.damaged", "The GUID partition table of the disk image is damaged.");

    public static readonly MessageTemplate GptDamagedUsableRange = Define(
        "gpt.damagedUsableRange",
        "The GUID partition table of the disk image is damaged. Its usable range does not fit its own headers.");

    public static readonly MessageTemplate GptDamagedEndsInTable = Define(
        "gpt.damagedEndsInTable",
        "The GUID partition table of the disk image is damaged. The file ends inside the partition table.");

    public static readonly MessageTemplate GptDamagedHeader = Define(
        "gpt.damagedHeader",
        "The GUID partition table of the disk image is damaged. Its header has a revision or size DDT does not know.");

    public static readonly MessageTemplate GptDamagedEntries = Define(
        "gpt.damagedEntries",
        "The GUID partition table of the disk image is damaged. Its partition entries are laid out in a way DDT does not read.");

    public static readonly MessageTemplate GptDamagedPartitionOutside = Define(
        "gpt.damagedPartitionOutside",
        "The GUID partition table of the disk image is damaged. Partition {number} lies outside the usable sectors.");

    public static readonly MessageTemplate GptDamagedOverlap = Define(
        "gpt.damagedOverlap",
        "The GUID partition table of the disk image is damaged. Partitions {first} and {second} overlap.");

    public static readonly MessageTemplate GptIncomplete = Define(
        "gpt.incomplete",
        "The disk image holds {length} bytes, but its partitions reach to byte {end}. The file is incomplete.");

    public static readonly MessageTemplate UploadNotAnImage = Define(
        "upload.notAnImage",
        "This file is neither a WIM image nor a disk image with a GUID partition table. Upload a WIM or ESD file, or a disk image " +
        "such as a distribution's cloud image.");

    public static readonly MessageTemplate UploadConversionOutOfSpace = Define(
        "upload.conversionOutOfSpace",
        "The store volume ran out of space while the image was converted. Free some space and complete the upload again.");

    public static readonly MessageTemplate UploadConversionFailed = Define(
        "upload.conversionFailed",
        "The image could not be converted. {detail}");

    public static readonly MessageTemplate UploadCompressedDamaged = Define(
        "upload.compressedDamaged",
        "The compressed file is damaged: {detail}");

    public static readonly MessageTemplate UploadUnreadableFormat = Define(
        "upload.unreadableFormat",
        "This is a {format} disk image, which DDT does not read. Convert it with qemu-img convert -O raw <file> disk.raw and upload " +
        "disk.raw, or upload the distribution's raw or qcow2 image.");

    public static readonly MessageTemplate UploadNoXz = Define(
        "upload.noXz",
        "This file is compressed with xz, which is not installed on the server. Install xz there and complete the upload again, or " +
        "unpack the file with xz -d and upload the disk image it holds.");

    public static readonly MessageTemplate UploadNoQemuImg = Define(
        "upload.noQemuImg",
        "This is a qcow2 image, and qemu-img is not installed on the server. Install qemu-img there and complete the upload again, or " +
        "convert the file with qemu-img convert -O raw <file> disk.raw and upload disk.raw.");

    public static readonly MessageTemplate UploadQcow2TooShort = Define("upload.qcow2TooShort", "The qcow2 image is too short to be one.");

    public static readonly MessageTemplate UploadQcow2Backing = Define(
        "upload.qcow2Backing",
        "The qcow2 image depends on a backing file. Make a standalone image with qemu-img convert -O qcow2 <file> standalone.qcow2 and " +
        "upload that.");

    public static readonly MessageTemplate UploadQcow2Encrypted = Define(
        "upload.qcow2Encrypted",
        "The qcow2 image is encrypted. Upload an image that is not.");

    public static readonly MessageTemplate UploadQcow2ExternalData = Define(
        "upload.qcow2ExternalData",
        "The qcow2 image keeps its data in an external file. Convert it with qemu-img convert -O raw <file> disk.raw and upload disk.raw.");

    public static readonly MessageTemplate PackageNotZip = Define(
        "package.notZip",
        "The file is not a zip archive, or it is damaged. Upload a zip file.");

    public static readonly MessageTemplate PackageTooManyEntries = Define(
        "package.tooManyEntries",
        "The zip holds {count} entries. A package can hold at most {max}.");

    public static readonly MessageTemplate PackageEntryNameTooLong = Define(
        "package.entryNameTooLong",
        "The entry {entry} has a name longer than {max} characters.");

    public static readonly MessageTemplate PackageEntryControlCharacter = Define(
        "package.entryControlCharacter",
        "The entry {entry} has a control character in its name.");

    public static readonly MessageTemplate PackageEntryAtRoot = Define(
        "package.entryAtRoot",
        "The entry {entry} starts at the root of a drive.");

    public static readonly MessageTemplate PackageEntryTooDeep = Define(
        "package.entryTooDeep",
        "The entry {entry} is more than {max} folders deep.");

    public static readonly MessageTemplate PackageEntryEmptyName = Define(
        "package.entryEmptyName",
        "The entry {entry} has an empty name, or a folder name that points out of the package.");

    public static readonly MessageTemplate PackageEntryForbiddenCharacter = Define(
        "package.entryForbiddenCharacter",
        "The entry {entry} has a character Windows does not allow in names, such as : for a drive or a data stream.");

    public static readonly MessageTemplate PackageEntryTrailingDot = Define(
        "package.entryTrailingDot",
        "The entry {entry} has a name that ends in a dot or a space, which Windows cannot create.");

    public static readonly MessageTemplate PackageEntryDeviceName = Define(
        "package.entryDeviceName",
        "The entry {entry} has a name Windows keeps for a device, such as CON or NUL.");

    public static readonly MessageTemplate PackageEntryEncrypted = Define(
        "package.entryEncrypted",
        "The entry {entry} is encrypted. Upload a zip without a password.");

    public static readonly MessageTemplate PackageEntrySymbolicLink = Define(
        "package.entrySymbolicLink",
        "The entry {entry} is a symbolic link. Put the file itself in the zip.");

    public static readonly MessageTemplate PackageEntryTwice = Define(
        "package.entryTwice",
        "The entry {entry} is in the zip twice, if case is ignored as Windows ignores it.");

    public static readonly MessageTemplate PackageTooLargeUnpacked = Define(
        "package.tooLargeUnpacked",
        "Unpacked, the zip would take more than {max} GB.");

    public static readonly MessageTemplate PackageFileAndFolder = Define(
        "package.fileAndFolder",
        "The zip has a file {entry} and a folder of the same name.");

    public static readonly MessageTemplate PackageNoInf = Define(
        "package.noInf",
        "A driver package needs at least one .inf file. Zip the folder that holds the drivers' .inf files.");

    public static readonly MessageTemplate PackageEntryDamaged = Define(
        "package.entryDamaged",
        "The entry {entry} is damaged. Create the zip again.");

    public static readonly MessageTemplate PackageEntryCompression = Define(
        "package.entryCompression",
        "The entry {entry} is compressed in a way DDT cannot unpack. Create the zip with Deflate.");

    public static readonly MessageTemplate PackageEntrySize = Define(
        "package.entrySize",
        "The entry {entry} unpacks to {actual} bytes, but the zip says {declared}. Create the zip again.");

    public static readonly MessageTemplate PackageEntryChecksum = Define(
        "package.entryChecksum",
        "The entry {entry} does not unpack to the bytes the zip says it holds. Create the zip again.");
}
