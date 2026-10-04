// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Accounts;
using DDT.Server.Data.Configurations;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Machines;
using DDT.Server.Packages;
using DDT.Server.Rules;
using DDT.Server.Sequences;
using DDT.Server.Tokens;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Data;

// One model for every database. Each provider has a context of its own below it, because EF Core keeps a context's
// migrations and snapshot per type. Services ask for this one.
public abstract class DdtDbContext(DbContextOptions options)
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

        builder.ApplyConfiguration(new DdtUserConfiguration());
        builder.ApplyConfiguration(new MachineConfiguration());
        builder.ApplyConfiguration(new MachineLogLineConfiguration());
        builder.ApplyConfiguration(new ImageConfiguration());
        builder.ApplyConfiguration(new ImageUploadConfiguration());
        builder.ApplyConfiguration(new DeploymentConfiguration());
        builder.ApplyConfiguration(new DeploymentSnapshotConfiguration());
        builder.ApplyConfiguration(new DeploymentStepConfiguration());
        builder.ApplyConfiguration(new RunCredentialConfiguration());
        builder.ApplyConfiguration(new DeploymentArtifactConfiguration());
        builder.ApplyConfiguration(new PackageConfiguration());
        builder.ApplyConfiguration(new TaskSequenceConfiguration());
        builder.ApplyConfiguration(new RuleConfiguration());
        builder.ApplyConfiguration(new MachineRoleConfiguration());
        builder.ApplyConfiguration(new AccountConfiguration());
        builder.ApplyConfiguration(new ApiTokenConfiguration());
        builder.ApplyConfiguration(new SettingsSectionConfiguration());
        builder.ApplyConfiguration(new SettingsHostStateConfiguration());
        builder.ApplyConfiguration(new AuditEventConfiguration());
    }
}
