// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Images;
using DDT.Server.Accounts;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Machines;
using DDT.Server.Packages;
using DDT.Server.Rules;
using DDT.Server.Sequences;
using DDT.Server.Tokens;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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

    public DbSet<DeploymentSnapshot> DeploymentSnapshots => Set<DeploymentSnapshot>();

    public DbSet<DeploymentStep> DeploymentSteps => Set<DeploymentStep>();

    public DbSet<AssignmentRule> AssignmentRules => Set<AssignmentRule>();

    public DbSet<Rule> Rules => Set<Rule>();

    public DbSet<MachineRole> MachineRoles => Set<MachineRole>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<RunCredential> RunCredentials => Set<RunCredential>();

    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    public DbSet<SettingsSection> SettingsSections => Set<SettingsSection>();

    public DbSet<SettingsHostState> SettingsHostStates => Set<SettingsHostState>();

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
            machine.Property(m => m.AgentEnvironment).HasConversion<string>().HasMaxLength(16).HasDefaultValue(AgentEnvironment.WindowsPE);
            machine.Property(m => m.TrustedUefiCas).HasConversion<string>().HasMaxLength(32);

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
            line.HasIndex(l => new { l.DeploymentId, l.Id });
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
            image.Property(i => i.BootCapability).HasConversion<string>().HasMaxLength(16);
            image.Property(i => i.SignedUnder).HasConversion<string>().HasMaxLength(32);
            image.Property(i => i.BootDetail).HasMaxLength(RawImageLimits.MaxBootDetailLength);
            image.Property(i => i.SourceSha256).HasMaxLength(64);
            image.HasIndex(i => i.SourceSha256);
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
            deployment.Property(d => d.Title).HasMaxLength(256);
            deployment.Property(d => d.State).HasConversion<string>().HasMaxLength(16);
            deployment.Property(d => d.State).IsConcurrencyToken();
            deployment.Property(d => d.Source).HasConversion<string>().HasMaxLength(16);
            deployment.Property(d => d.RequestedByName).HasMaxLength(256);
            deployment.Property(d => d.CurrentStepName).HasMaxLength(DeploymentLimits.MaxStepNameLength);
            deployment.Property(d => d.CurrentPhase).HasConversion<string>().HasMaxLength(16);
            deployment.Property(d => d.Activity).HasConversion<string>().HasMaxLength(32);
            deployment.Property(d => d.Error).HasMaxLength(DeploymentLimits.MaxErrorLength);
            deployment.Property(d => d.PauseMessage).HasMaxLength(DeploymentLimits.MaxPauseMessageLength);
            deployment.Property(d => d.ContinuedByName).HasMaxLength(256);
            deployment.HasIndex(d => d.MachineId);
            deployment.HasOne<Machine>().WithMany().HasForeignKey(d => d.MachineId).OnDelete(DeleteBehavior.Cascade);
            deployment.HasOne<TaskSequence>().WithMany().HasForeignKey(d => d.TaskSequenceId).OnDelete(DeleteBehavior.SetNull);
            deployment.HasOne<DdtUser>().WithMany().HasForeignKey(d => d.RequestedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<DeploymentSnapshot>(snapshot =>
        {
            snapshot.HasKey(s => s.DeploymentId);
            snapshot.HasOne<Deployment>().WithOne().HasForeignKey<DeploymentSnapshot>(s => s.DeploymentId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DeploymentStep>(step =>
        {
            step.HasKey(s => new { s.DeploymentId, s.StepId });
            step.Property(s => s.Name).HasMaxLength(DeploymentLimits.MaxStepNameLength);
            step.Property(s => s.Kind).HasMaxLength(32);
            step.Property(s => s.Phase).HasConversion<string>().HasMaxLength(16);
            step.Property(s => s.State).HasConversion<string>().HasMaxLength(16);

            // A report that saves after a newer one could otherwise move a finished step back to Running.
            step.Property(s => s.State).IsConcurrencyToken();
            step.Property(s => s.Error).HasMaxLength(DeploymentLimits.MaxErrorLength);
            step.Property(s => s.Branch).HasConversion<string>().HasMaxLength(8);
            step.HasOne<Deployment>().WithMany().HasForeignKey(s => s.DeploymentId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RunCredential>(credential =>
        {
            credential.HasKey(c => new { c.DeploymentId, c.InputName });
            credential.Property(c => c.InputName).HasMaxLength(RunCredential.MaxInputNameLength);
            credential.Property(c => c.UserName).HasMaxLength(AccountLimits.MaxUserNameLength);
            credential.Property(c => c.Domain).HasMaxLength(AccountLimits.MaxDomainLength);
            credential.Property(c => c.ProvidedByName).HasMaxLength(256);

            // A run's credentials go with it.
            credential.HasOne<Deployment>().WithMany().HasForeignKey(c => c.DeploymentId).OnDelete(DeleteBehavior.Cascade);
            credential.HasOne<DdtUser>().WithMany().HasForeignKey(c => c.ProvidedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<DeploymentArtifact>(artifact =>
        {
            artifact.Property(a => a.Kind).HasConversion<string>().HasMaxLength(16);
            artifact.Property(a => a.Name).HasMaxLength(256);
            artifact.Property(a => a.Sha256).HasMaxLength(64);
            artifact.Property(a => a.Language).HasMaxLength(16);
            artifact.Property(a => a.BootCapability).HasConversion<string>().HasMaxLength(16);
            artifact.Property(a => a.SignedUnder).HasConversion<string>().HasMaxLength(32);
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

        builder.Entity<AssignmentRule>(rule =>
        {
            rule.Property(r => r.Kind).HasConversion<string>().HasMaxLength(16);
            rule.Property(r => r.MatchKey).HasMaxLength(AssignmentRuleKeys.MaxMatchKeyLength);
            rule.Property(r => r.Mac).HasMaxLength(12);
            rule.Property(r => r.Manufacturer).HasMaxLength(HardwareModels.MaxLength);
            rule.Property(r => r.Model).HasMaxLength(HardwareModels.MaxLength);
            rule.Property(r => r.Description).HasMaxLength(AssignmentRuleKeys.MaxDescriptionLength);
            rule.Property(r => r.UpdatedByName).HasMaxLength(256);
            rule.HasIndex(r => r.MatchKey).IsUnique();

            // A sequence that rules choose cannot be deleted, so no rule is left pointing nowhere.
            rule.HasOne<TaskSequence>().WithMany().HasForeignKey(r => r.TaskSequenceId).OnDelete(DeleteBehavior.Restrict);
            rule.HasOne<DdtUser>().WithMany().HasForeignKey(r => r.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Rule>(rule =>
        {
            rule.Property(r => r.Name).HasMaxLength(RuleLimits.MaxNameLength);
            rule.Property(r => r.Description).HasMaxLength(RuleLimits.MaxDescriptionLength);
            rule.Property(r => r.Revision).IsConcurrencyToken();
            rule.Property(r => r.UpdatedByName).HasMaxLength(256);

            // Two rules never share a place. The index is checked for each row as it changes, so a reorder that swaps
            // places first moves the rules it changes out of the way, in a save of its own.
            rule.HasIndex(r => r.Position).IsUnique();

            // A sequence that rules choose cannot be deleted, so no rule is left pointing nowhere.
            rule.HasOne<TaskSequence>().WithMany().HasForeignKey(r => r.TaskSequenceId).OnDelete(DeleteBehavior.Restrict);
            rule.HasOne<DdtUser>().WithMany().HasForeignKey(r => r.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<MachineRole>(role =>
        {
            role.Property(r => r.Name).HasMaxLength(RuleLimits.MaxRoleNameLength);
            role.Property(r => r.NormalizedName).HasMaxLength(RuleLimits.MaxRoleNameLength);
            role.Property(r => r.Description).HasMaxLength(RuleLimits.MaxDescriptionLength);
            role.Property(r => r.Revision).IsConcurrencyToken();
            role.Property(r => r.UpdatedByName).HasMaxLength(256);
            role.HasIndex(r => r.NormalizedName).IsUnique();
            role.HasOne<DdtUser>().WithMany().HasForeignKey(r => r.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Account>(account =>
        {
            account.Property(a => a.Name).HasMaxLength(AccountLimits.MaxNameLength);
            account.Property(a => a.NormalizedName).HasMaxLength(AccountLimits.MaxNameLength);
            account.Property(a => a.UserName).HasMaxLength(AccountLimits.MaxUserNameLength);
            account.Property(a => a.Domain).HasMaxLength(AccountLimits.MaxDomainLength);
            account.Property(a => a.Revision).IsConcurrencyToken();
            account.Property(a => a.UpdatedByName).HasMaxLength(256);
            account.HasIndex(a => a.NormalizedName).IsUnique();
            account.HasOne<DdtUser>().WithMany().HasForeignKey(a => a.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ApiToken>(token =>
        {
            token.Property(t => t.Name).HasMaxLength(ApiTokenLimits.MaxNameLength);
            token.Property(t => t.Role).HasMaxLength(16);
            token.Property(t => t.SecretHash).HasMaxLength(64);
            token.Property(t => t.Hint).HasMaxLength(ApiTokenSecrets.HintLength);
            token.Property(t => t.LastUsedAddress).HasMaxLength(64);
            token.Property(t => t.RevokedByName).HasMaxLength(256);

            // Every request that carries a token looks it up by the hash of what it sent.
            token.HasIndex(t => t.SecretHash).IsUnique();
            token.HasIndex(t => t.UserId);

            // A token acts only for its user, so it goes with the account.
            token.HasOne<DdtUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            token.HasOne<DdtUser>().WithMany().HasForeignKey(t => t.RevokedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<SettingsSection>(section =>
        {
            section.HasKey(s => s.Section);
            section.Property(s => s.Section).HasMaxLength(SettingsSection.MaxNameLength);

            // Every save checks the version it read, so two saves of one section never overwrite each other unnoticed.
            section.Property(s => s.Version).IsConcurrencyToken();
            section.Property(s => s.UpdatedByName).HasMaxLength(256);
            section.HasOne<DdtUser>().WithMany().HasForeignKey(s => s.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<SettingsHostState>(state =>
        {
            state.HasKey(s => new { s.Host, s.Section });
            state.Property(s => s.Host).HasMaxLength(SettingsHostState.MaxHostLength);
            state.Property(s => s.Section).HasMaxLength(SettingsSection.MaxNameLength);
            state.Property(s => s.State).HasConversion<string>().HasMaxLength(16);
            state.Property(s => s.Message).HasMaxLength(SettingsHostState.MaxMessageLength);
        });

        builder.Entity<AuditEvent>(audit =>
        {
            audit.Property(a => a.Action).HasMaxLength(64);
            audit.Property(a => a.ActorName).HasMaxLength(256);
            audit.Property(a => a.SubjectId).HasMaxLength(64);
            audit.Property(a => a.SourceAddress).HasMaxLength(64);
            audit.Property(a => a.Detail).HasMaxLength(AuditEvent.MaxDetailLength);
            audit.HasIndex(a => a.OccurredUtc);
            audit.HasIndex(a => a.Action);

            // SQLite has no type for DateTimeOffset and compares it only for equality, but the audit log is filtered by
            // time. As UTC ticks it compares in order and keeps every digit, which EF Core's own binary converter does
            // not. PostgreSQL, which has the type, keeps it.
            if (Database.IsSqlite())
            {
                audit.Property(a => a.OccurredUtc).HasConversion(new ValueConverter<DateTimeOffset, long>(
                    time => time.UtcTicks,
                    ticks => new DateTimeOffset(ticks, TimeSpan.Zero)));
            }
        });
    }
}
