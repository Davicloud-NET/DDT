// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Configuration;
using DDT.Server.Data;
using Microsoft.Data.Sqlite;

namespace DDT.Host.Startup;

// DDT.Host backup <file>: a copy of the SQLite database as it is at one moment, taken next to the running server.
// Copying the file itself could catch it halfway through a write.
public static class BackupConsole
{
    private const string Usage = "Usage: DDT.Host backup <file>, where the file doesn't exist yet.";

    public static bool Handles(string[] args) => args is ["backup", ..];

    // Tests pass configuration, so they don't read the machine's ddt.ini.
    public static int Run(string[] args, TextWriter output, IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        if (args is not ["backup", string target])
        {
            output.WriteLine(Usage);

            return 2;
        }

        configuration ??= Configuration();
        DdtOptions options = configuration.GetSection(DdtOptions.SectionName).Get<DdtOptions>() ?? new DdtOptions();
        DatabaseProvider provider = DatabaseProviders.Choose(options.Database, configuration.GetConnectionString(DatabaseProviders.ConnectionStringName));
        string file = DatabaseProviders.SqliteFileIn(options.StorePath);

        string? problem = provider != DatabaseProvider.Sqlite
            ? $"The database is on {provider}, which has backup tools of its own."
            : !File.Exists(file)
                ? $"{file} doesn't exist. The server creates it at its first start."
                : File.Exists(target)
                    ? $"{target} exists already. Name a new file."
                    : null;

        if (problem is not null)
        {
            output.WriteLine(problem);

            return 1;
        }

        string copy = Path.GetFullPath(target);

        using (SqliteConnection connection = new($"Data Source={file};Mode=ReadOnly;Pooling=False"))
        {
            connection.Open();

            using SqliteCommand vacuum = connection.CreateCommand();
            vacuum.CommandText = "VACUUM INTO $copy";
            vacuum.Parameters.AddWithValue("$copy", copy);
            vacuum.ExecuteNonQuery();
        }

        output.WriteLine($"Copied the database to {copy}. The key ring in the store's keys folder decrypts its secrets: keep both.");

        return 0;
    }

    private static IConfiguration Configuration()
    {
        ConfigurationBuilder builder = new();
        builder.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true);
        builder.AddEnvironmentVariables();
        BootstrapFile.Add(builder, BootstrapFile.DefaultPath);

        return builder.Build();
    }
}
