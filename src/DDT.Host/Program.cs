using DDT.Host.Logging;
using DDT.Host.Startup;
using DDT.Server.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<DdtOptions>(builder.Configuration.GetSection(DdtOptions.SectionName));

DdtOptions options = builder.Configuration.GetSection(DdtOptions.SectionName).Get<DdtOptions>() ?? new DdtOptions();
IReadOnlySet<DeploymentRole> roles = DeploymentRoles.Parse(options.Roles);
string activeRoles = string.Join(", ", roles.Order());
string certificatePath = builder.Configuration["Kestrel:Certificates:Default:Path"] ?? string.Empty;
bool generatedCertificate = CertificateBootstrap.EnsureConfiguredCertificate(builder.Configuration, options);

HttpsConfigurationCheck.Validate(builder.Configuration, options);

var app = builder.Build();

HostLog.ActiveRoles(app.Logger, activeRoles);

if (generatedCertificate)
{
    HostLog.GeneratedCertificate(app.Logger, certificatePath);
}

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
