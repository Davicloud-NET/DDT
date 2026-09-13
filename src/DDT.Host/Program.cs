using DDT.Contracts;
using DDT.Host.Logging;
using DDT.Host.Startup;
using DDT.Pxe;
using DDT.Server.Authentication;
using DDT.Server.Configuration;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using DDT.Server.Security;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<DdtOptions>(builder.Configuration.GetSection(DdtOptions.SectionName));

DdtOptions options = builder.Configuration.GetSection(DdtOptions.SectionName).Get<DdtOptions>() ?? new DdtOptions();
IReadOnlySet<DeploymentRole> roles = DeploymentRoles.Parse(options.Roles);
string activeRoles = string.Join(", ", roles.Order());
string certificatePath = builder.Configuration["Kestrel:Certificates:Default:Path"] ?? string.Empty;
bool generatedCertificate = CertificateBootstrap.EnsureConfiguredCertificate(builder.Configuration, options);

builder.Services.ConfigureHttpJsonOptions(json =>
    json.SerializerOptions.TypeInfoResolverChain.Insert(0, DdtJsonContext.Default));

builder.Services.AddAntiforgery(antiforgery =>
{
    antiforgery.HeaderName = CsrfHeaderNames.RequestToken;
    antiforgery.Cookie.Name = options.RequireHttps ? "__Host-ddt-csrf" : "ddt-csrf";
    antiforgery.Cookie.SameSite = SameSiteMode.Strict;
    antiforgery.Cookie.SecurePolicy = options.RequireHttps
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddDdtData(builder.Configuration, options);
builder.Services.AddDdtAuthentication(builder.Configuration, options);
builder.Services.AddDdtAuthorization();
builder.Services.AddDdtRateLimiting();

// After the data services, so hosted services start in dependency order, and before the endpoint
// check, because the Kestrel endpoint the pxe role adds changes which settings Kestrel honours.
PxeSetup? pxe = roles.Contains(DeploymentRole.Pxe) ? builder.AddDdtPxe() : null;

HttpsConfigurationCheck.Validate(builder.Configuration, options);

var app = builder.Build();

HostLog.ActiveRoles(app.Logger, activeRoles);

if (generatedCertificate)
{
    HostLog.GeneratedCertificate(app.Logger, certificatePath);
}

app.MapDefaultEndpoints();

app.UseRouting();

if (pxe is not null)
{
    app.UseDdtBootListener(pxe);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// DisableCookieRedirect makes an unauthenticated API call answer 401 instead of redirecting to a
// login page: the redirect is inferred per endpoint from metadata, so it cannot be relied on.
RouteGroupBuilder api = app.MapGroup("/api")
    .DisableCookieRedirect()
    .AddEndpointFilter<SameOriginEndpointFilter>()
    .AddEndpointFilter<AntiforgeryEndpointFilter>();

api.MapGroup("/auth").MapAuthEndpoints();
api.MapGroup("/auth/2fa").MapTwoFactorEndpoints();
api.MapGroup("/auth/external").MapExternalLoginEndpoints();

app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();
