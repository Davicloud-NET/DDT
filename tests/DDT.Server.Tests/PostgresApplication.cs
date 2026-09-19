namespace DDT.Server.Tests;

// The real host on PostgreSQL, which applies every migration at startup instead of creating the schema.
public sealed class PostgresApplication(string connectionString) : SettingsApplication(("ConnectionStrings:ddtdb", connectionString));
