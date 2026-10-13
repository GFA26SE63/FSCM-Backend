using System.Security.Claims;
using FSCM.API.Contracts.Identity;
using FSCM.Application.Features.Identity;
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

