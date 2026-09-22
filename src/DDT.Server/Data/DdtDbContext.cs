// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Machines;
using DDT.Server.Packages;
using DDT.Server.Sequences;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Data;

public sealed class DdtDbContext(DbContextOptions<DdtDbContext> options)
    : IdentityDbContext<DdtUser, DdtRole, Guid>(options)
{
    public const string Schema = "ddt";

    public DbSet<Machine> Machines => Set<Machine>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<MachineLogLine> MachineLogLines => Set<MachineLogLine>();

    public DbSet<Image> Images => Set<Image>();

    public DbSet<ImageUpload> ImageUploads => Set<ImageUpload>();

    public DbSet<Deployment> Deployments => Set<Deployment>();

    public DbSet<TaskSequence> TaskSequences => Set<TaskSequence>();

    public DbSet<Package> Packages => Set<Package>();

    public DbSet<DeploymentArtifact> DeploymentArtifacts => Set<DeploymentArtifact>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasDefaultSchema(Schema);
        base.OnModelCreating(builder);

        builder.Entity<DdtUser>(user =>
        {
            user.Property(u => u.Source).HasConversion<string>().HasMaxLength(16);
            user.Property(u => u.DirectoryObjectId).HasMaxLength(256);
            user.Property(u => u.DisplayName).HasMaxLength(256);
            user.HasIndex(u => u.DirectoryObjectId).IsUnique().HasFilter(null);
        });

        builder.Entity<Machine>(machine =>
        {
            machine.Property(m => m.SmbiosUuid).HasMaxLength(64);
            machine.Property(m => m.PrimaryMac).HasMaxLength(32);
            machine.Property(m => m.MacAddresses).HasMaxLength(256);
            machine.Property(m => m.AgentVersion).HasMaxLength(32);
            machine.Property(m => m.LastSeenAddress).HasMaxLength(64);
            machine.Property(m => m.Manufacturer).HasMaxLength(128);
            machine.Property(m => m.Model).HasMaxLength(128);
            machine.Property(m => m.SerialNumber).HasMaxLength(128);
            machine.Property(m => m.AssignedName).HasMaxLength(15);
            machine.Property(m => m.FirstSeenAddress).HasMaxLength(64);
            machine.Property(m => m.State).HasConversion<string>().HasMaxLength(16);

            // State and generation are checked on save, so an approval, a rejection and a registration that
            // starts over cannot silently overwrite one another: the loser retries or reports a conflict.
            machine.Property(m => m.State).IsConcurrencyToken();
            machine.Property(m => m.TokenGeneration).IsConcurrencyToken();
            machine.Property(m => m.ActiveDeploymentId).IsConcurrencyToken();
            machine.Property(m => m.Disks).HasMaxLength(RegistrationValidator.MaxDisksLength);
            machine.HasIndex(m => m.SmbiosUuid);
            machine.HasIndex(m => m.PrimaryMac);
            machine.HasIndex(m => m.FirstSeenAddress);
            machine.HasOne(m => m.ApprovedBy).WithMany().HasForeignKey(m => m.ApprovedByUserId).OnDelete(DeleteBehavior.SetNull);
            machine.Property(m => m.SignedInUserName).HasMaxLength(256);
            machine.HasOne<DdtUser>().WithMany().HasForeignKey(m => m.SignedInByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<MachineLogLine>(line =>
        {
            line.Property(l => l.Level).HasConversion<string>().HasMaxLength(16);
            line.Property(l => l.Message).HasMaxLength(MachineLogLimits.MaxMessageLength);
            line.HasIndex(l => new { l.MachineId, l.Id });
            line.HasOne<Machine>().WithMany().HasForeignKey(l => l.MachineId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Image>(image =>
        {
            image.Property(i => i.Name).HasMaxLength(256);
            image.Property(i => i.Kind).HasConversion<string>().HasMaxLength(16);
            image.Property(i => i.Sha256).HasMaxLength(64);
            image.Property(i => i.Edition).HasMaxLength(64);
            image.Property(i => i.Architecture).HasMaxLength(16);
            image.Property(i => i.Version).HasMaxLength(32);
            image.Property(i => i.Language).HasMaxLength(16);
            image.Property(i => i.OriginalFileName).HasMaxLength(256);
            image.Property(i => i.UploadedByName).HasMaxLength(256);
            image.HasIndex(i => i.Sha256);
            image.HasOne<DdtUser>().WithMany().HasForeignKey(i => i.UploadedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ImageUpload>(upload =>
        {
            upload.Property(u => u.FileName).HasMaxLength(ImageUploadLimits.MaxFileNameLength);
            upload.Property(u => u.CompletedSha256).HasMaxLength(64);

            // The default fills the column for uploads made before packages existed, which were all images.
            upload.Property(u => u.Kind).HasConversion<string>().HasMaxLength(16).HasDefaultValue(UploadKind.Image);
            upload.HasIndex(u => new { u.FileName, u.Length, u.LastModified });
            upload.HasOne<DdtUser>().WithMany().HasForeignKey(u => u.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Deployment>(deployment =>
        {
            deployment.Property(d => d.ImageName).HasMaxLength(256);
            deployment.Property(d => d.Sha256).HasMaxLength(64);
            deployment.Property(d => d.State).HasConversion<string>().HasMaxLength(16);
            deployment.Property(d => d.State).IsConcurrencyToken();
            deployment.Property(d => d.Step).HasConversion<string>().HasMaxLength(16);
            deployment.Property(d => d.Source).HasConversion<string>().HasMaxLength(16);
            deployment.Property(d => d.RequestedByName).HasMaxLength(256);
            deployment.Property(d => d.Error).HasMaxLength(DeploymentLimits.MaxErrorLength);
            deployment.HasIndex(d => d.MachineId);
            deployment.HasOne<Machine>().WithMany().HasForeignKey(d => d.MachineId).OnDelete(DeleteBehavior.Cascade);
            deployment.HasOne<Image>().WithMany().HasForeignKey(d => d.ImageId).OnDelete(DeleteBehavior.SetNull);
            deployment.HasOne<DdtUser>().WithMany().HasForeignKey(d => d.RequestedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<DeploymentArtifact>(artifact =>
        {
            artifact.Property(a => a.Kind).HasConversion<string>().HasMaxLength(16);
            artifact.Property(a => a.Name).HasMaxLength(256);
            artifact.Property(a => a.Sha256).HasMaxLength(64);
            artifact.Property(a => a.Language).HasMaxLength(16);
            artifact.HasIndex(a => a.Sha256);
            artifact.HasIndex(a => a.SourceId);
            artifact.HasOne<Deployment>().WithMany().HasForeignKey(a => a.DeploymentId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Package>(package =>
        {
            package.Property(p => p.Name).HasMaxLength(PackageLimits.MaxNameLength);
            package.Property(p => p.Kind).HasConversion<string>().HasMaxLength(16);
            package.Property(p => p.Sha256).HasMaxLength(64);
            package.Property(p => p.Description).HasMaxLength(PackageLimits.MaxDescriptionLength);
            package.Property(p => p.OriginalFileName).HasMaxLength(ImageUploadLimits.MaxFileNameLength);
            package.Property(p => p.UploadedByName).HasMaxLength(256);
            package.HasIndex(p => p.Sha256);
            package.HasOne<DdtUser>().WithMany().HasForeignKey(p => p.UploadedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TaskSequence>(sequence =>
        {
            sequence.Property(s => s.Name).HasMaxLength(SequenceLimits.MaxNameLength);
            sequence.Property(s => s.NormalizedName).HasMaxLength(SequenceLimits.MaxNameLength);
            sequence.Property(s => s.Description).HasMaxLength(SequenceLimits.MaxDescriptionLength);
            sequence.Property(s => s.Revision).IsConcurrencyToken();
            sequence.Property(s => s.UpdatedByName).HasMaxLength(256);
            sequence.HasIndex(s => s.NormalizedName).IsUnique();
            sequence.HasOne<DdtUser>().WithMany().HasForeignKey(s => s.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<AuditEvent>(audit =>
        {
            audit.Property(a => a.Action).HasMaxLength(64);
            audit.Property(a => a.ActorName).HasMaxLength(256);
            audit.Property(a => a.SubjectId).HasMaxLength(64);
            audit.Property(a => a.SourceAddress).HasMaxLength(64);
            audit.Property(a => a.Detail).HasMaxLength(2048);
            audit.HasIndex(a => a.OccurredUtc);
            audit.HasIndex(a => a.Action);
        });
    }
}
