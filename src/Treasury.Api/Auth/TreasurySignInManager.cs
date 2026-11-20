using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Treasury.Api.Auth;

/// Identity's sign-in manager plus one rule: a deactivated Member can't log in (password login answers
/// NotAllowed -> 401) and their existing cookies fail the per-request security-stamp check (-> 401).
public class TreasurySignInManager(
    UserManager<Member> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<Member> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<Member>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<Member> confirmation)
    : SignInManager<Member>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(Member user) =>
        user.IsActive && await base.CanSignInAsync(user);

    public override async Task<bool> ValidateSecurityStampAsync(Member? user, string? securityStamp) =>
        user is { IsActive: true } && await base.ValidateSecurityStampAsync(user, securityStamp);
}
