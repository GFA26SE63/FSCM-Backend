using System.Security.Claims;
using FSCM.API.Contracts.Identity;
using FSCM.Application.Features.Identity;
using FSCM.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IdentityService identityService, IWebHostEnvironment environment) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<AuthenticationResult>(StatusCodes.Status200OK)]
    public Task<AuthenticationResult> Login(
        LoginRequest request,
        CancellationToken cancellationToken) =>
        identityService.LoginAsync(
            request.Email,
            request.Password,
            GetRemoteIpAddress(),
            cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.GoogleExchange)]
    [HttpPost("google/exchange")]
    [ProducesResponseType<AuthenticationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<AuthenticationResult> ExchangeGoogle(CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue("sub");
        var email = User.FindFirstValue("email")
            ?? User.FindFirstValue(ClaimTypes.Email);
        var emailVerified = User.FindFirstValue("email_verified");
        if (string.IsNullOrWhiteSpace(subject) ||
            string.IsNullOrWhiteSpace(email) ||
            !string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase))
            throw IdentityOperationException.InvalidToken();

        return identityService.ExchangeGoogleAsync(
            new GoogleIdentity(subject, email),
            GetRemoteIpAddress(),
            cancellationToken);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType<AuthenticationResult>(StatusCodes.Status200OK)]
    public Task<AuthenticationResult> Refresh(
        RefreshTokenRequest request,
        CancellationToken cancellationToken) =>
        identityService.RefreshAsync(request.RefreshToken, GetRemoteIpAddress(), cancellationToken);

    [AllowAnonymous]
    [HttpPost("activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Activate(
        ActivateAccountRequest request,
        CancellationToken cancellationToken)
    {
        await identityService.ActivateAsync(request.Token, request.Password, cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("password-reset/request")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestPasswordReset(
        PasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        var resetToken = await identityService.RequestPasswordResetAsync(request.Email, cancellationToken);

        return Accepted(new
        {
            Message = "If the account exists, password reset instructions have been generated.",
            ResetToken = environment.IsDevelopment() ? resetToken : null
        });
    }

    [AllowAnonymous]
    [HttpPost("password-reset/confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmPasswordReset(
        ConfirmPasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        await identityService.ResetPasswordAsync(request.Token, request.Password, cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        await identityService.LogoutAsync(request.RefreshToken, GetRemoteIpAddress(), cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<AuthenticatedUser>(StatusCodes.Status200OK)]
    public Task<AuthenticatedUser> Me(CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            throw IdentityOperationException.InvalidToken();
        }

        return identityService.GetCurrentUserAsync(userId, cancellationToken);
    }

    private string? GetRemoteIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString();
}

