using DDT.Host.Logging;
using DDT.Server.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

DdtOptions options = builder.Configuration.GetSection(DdtOptions.SectionName).Get<DdtOptions>() ?? new DdtOptions();
IReadOnlySet<DdtRole> roles = DdtRoles.Parse(options.Roles);
string activeRoles = string.Join(", ", roles.Order());

var app = builder.Build();

HostLog.ActiveRoles(app.Logger, activeRoles);

app.MapDefaultEndpoints();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
