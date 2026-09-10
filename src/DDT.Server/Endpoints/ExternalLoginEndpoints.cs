using DDT.Server.Authentication;
using DDT.Server.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace DDT.Server.Endpoints;

public static class ExternalLoginEndpoints
{
    private const string CompletePath = "/api/auth/external/complete";

    public static RouteGroupBuilder MapExternalLoginEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/start", Start).AllowAnonymous();
        group.MapGet("/complete", CompleteAsync).AllowAnonymous();
        group.MapPost("/link", LinkAsync);

        return group;
    }

    private static ChallengeHttpResult Start(SignInManager<DdtUser> signInManager)
    {
        AuthenticationProperties properties =
            signInManager.ConfigureExternalAuthenticationProperties(OidcOptions.SchemeName, CompletePath);

        return TypedResults.Challenge(properties, [OidcOptions.SchemeName]);
    }

    private static async Task<RedirectHttpResult> CompleteAsync(
        SignInManager<DdtUser> signInManager,
        UserManager<DdtUser> userManager,
        IOptions<OidcOptions> options)
    {
        ExternalLoginInfo? info = await signInManager.GetExternalLoginInfoAsync().ConfigureAwait(false);

        if (info is null)
        {
            return TypedResults.Redirect("/sign-in?error=external");
        }

        SignInResult result = await signInManager
            .ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: false)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            return TypedResults.Redirect("/");
        }

        if (result.IsLockedOut)
        {
            return TypedResults.Redirect("/sign-in?error=locked");
        }

        if (!options.Value.AutoProvision)
        {
            // The identity is unknown and DDT will not guess which local account it belongs to.
            // Matching on the asserted email address would let any issuer that does not verify
            // addresses take over an account by claiming one.
            return TypedResults.Redirect("/sign-in?error=unlinked");
        }

        DdtUser user = new()
        {
            UserName = info.Principal.Identity?.Name ?? info.ProviderKey,
            Email = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
            DisplayName = info.Principal.Identity?.Name,
            Source = AccountSource.Directory,
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        IdentityResult created = await userManager.CreateAsync(user).ConfigureAwait(false);

        if (!created.Succeeded)
        {
            return TypedResults.Redirect("/sign-in?error=provision");
        }

        await userManager.AddLoginAsync(user, info).ConfigureAwait(false);
        await userManager.AddToRoleAsync(user, options.Value.AutoProvisionRole).ConfigureAwait(false);
        await signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);

        return TypedResults.Redirect("/");
    }

    // Linking happens only from an already authenticated session, so the account being linked to
    // is proven rather than inferred.
    private static async Task<Results<Ok, ValidationProblem, UnauthorizedHttpResult>> LinkAsync(
        System.Security.Claims.ClaimsPrincipal principal,
        SignInManager<DdtUser> signInManager,
        UserManager<DdtUser> userManager)
    {
        DdtUser? user = await userManager.GetUserAsync(principal).ConfigureAwait(false);

        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        ExternalLoginInfo? info = await signInManager.GetExternalLoginInfoAsync(user.Id.ToString()).ConfigureAwait(false);

        if (info is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["external"] = ["No external sign in is in progress."],
            });
        }

        IdentityResult result = await userManager.AddLoginAsync(user, info).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return TypedResults.ValidationProblem(result.ToProblemDictionary());
        }

        return TypedResults.Ok();
    }
}
