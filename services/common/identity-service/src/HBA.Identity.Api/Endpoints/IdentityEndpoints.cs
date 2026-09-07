using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using MediatR;
using HBA.Shared.Hosting.Http;
using HBA.Identity.Application.Roles.Commands.CreateRole;
using HBA.Identity.Application.Roles.Commands.DeleteRole;
using HBA.Identity.Application.Roles.Commands.SetRolePermissions;
using HBA.Identity.Application.Roles.Commands.UpdateRole;
using HBA.Identity.Application.Roles.Queries.GetRole;
using HBA.Identity.Application.Roles.Queries.ListRoles;
using HBA.Identity.Application.Users.Commands.AcceptTerms;
using HBA.Identity.Application.Users.Commands.AssignRole;
using HBA.Identity.Application.Users.Commands.ChangePassword;
using HBA.Identity.Application.Users.Commands.DeleteAccount;
using HBA.Identity.Application.Users.Commands.ConfirmEmail;
using HBA.Identity.Application.Users.Commands.Login;
using HBA.Identity.Application.Users.Commands.Logout;
using HBA.Identity.Application.Users.Commands.Otp;
using HBA.Identity.Application.Users.Commands.Mfa;
using HBA.Identity.Application.Users.Commands.PasswordReset;
using HBA.Identity.Application.Users.Commands.Reauthenticate;
using HBA.Identity.Application.Users.Commands.ReactivateUser;
using HBA.Identity.Application.Users.Commands.RefreshToken;
using HBA.Identity.Application.Users.Commands.ApproveUser;
using HBA.Identity.Application.Users.Commands.MarkEmailVerified;
using HBA.Identity.Application.Users.Commands.RegisterUser;
using HBA.Identity.Application.Users.Commands.RequestEmailVerification;
using HBA.Identity.Application.Users.Commands.RemoveRole;
using HBA.Identity.Application.Users.Commands.SuspendUser;
using HBA.Identity.Application.Users.Commands.UpdateProfile;
using HBA.Identity.Application.Users.Queries.GetUser;
using HBA.Identity.Application.Users.Queries.ListUsers;

namespace HBA.Identity.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Identity : authentification (anonyme), gestion de son
/// propre compte (authentifié) et administration des comptes et rôles (Admin).
/// </summary>
public static class IdentityEndpoints
{
    /// <summary>Enregistre les routes auth, compte et administration.</summary>
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        MapAuth(app);
        MapAccount(app);
        MapAdminUsers(app);
        MapRoles(app);
        return app;
    }

    // -------------------------------------------------------------------- Auth

    private static void MapAuth(IEndpointRouteBuilder app)
    {
        // Politique « auth » (30 req/min/IP) comme sur les quatre BFF. C'était le
        // seul groupe d'authentification à retomber sur la limite globale de
        // 300/min — dix fois plus permissif sur les routes qui reçoivent des mots
        // de passe.
        var group = app.MapGroup("/api/v1/auth").WithTags("Identity · Auth")
            .RequireRateLimiting(AuthRateLimiter.PolicyName);

        group.MapPost("/register", RegisterAsync).WithName("Register").AllowAnonymous();
        group.MapPost("/confirm-email", ConfirmEmailAsync).WithName("ConfirmEmail").AllowAnonymous();
        group.MapPost("/login", LoginAsync).WithName("Login").AllowAnonymous();
        group.MapPost("/refresh", RefreshAsync).WithName("RefreshToken").AllowAnonymous();

        // LE STEP-UP DU §37 — LA SEULE ROUTE `/auth` QUI EXIGE UN JETON.
        group.MapPost("/reauthenticate", ReauthenticateAsync)
            .WithName("Reauthenticate").RequireAuthorization();

        // LE §10.1 PLACE LA DÉCONNEXION SOUS `/auth`, PAS SOUS `/account`.
        group.MapPost("/logout", LogoutByRefreshTokenAsync)
            .WithName("LogoutByRefreshToken").AllowAnonymous().AllowIdempotency();

        // §10.1 : `POST /api/v1/auth/verify-otp`.
        group.MapPost("/otp/request", RequestOtpAsync).WithName("RequestOtp").AllowAnonymous();
        group.MapPost("/verify-otp", VerifyOtpAsync).WithName("VerifyOtp").AllowAnonymous();

        // UN MOT DE PASSE OUBLIÉ ÉTAIT DÉFINITIF.
        group.MapPost("/password/forgot", ForgotPasswordAsync).WithName("ForgotPassword").AllowAnonymous();
        group.MapPost("/password/reset", ResetPasswordAsync).WithName("ResetPassword").AllowAnonymous();

        // ANONYME, ET PAR ADRESSE — PARCE QUE L'INSCRIPTION NE CONNECTE PAS.
        group.MapPost("/email/resend", ResendEmailVerificationAsync)
            .WithName("ResendEmailVerification").AllowAnonymous();

        // SECOND CHEMIN DE VÉRIFICATION, PAR ADRESSE — ET NON UN DOUBLON.
        group.MapPost("/email/verify", VerifyEmailByCodeAsync)
            .WithName("VerifyEmailByCode").AllowAnonymous();
    }

    /// <summary>Inscrit un nouvel utilisateur ; un e-mail de vérification est envoyé.</summary>
    private static async Task<IResult> RegisterAsync(RegisterRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(
            new RegisterUserCommand(request.FirstName, request.LastName, request.Email, request.PhoneNumber, request.Password), ct);
        return result.Match(id => Results.Created($"/api/identity/users/{id}", new { id }));
    }

    /// <summary>Confirme l'adresse e-mail à partir du jeton reçu par lien.</summary>
    private static async Task<IResult> ConfirmEmailAsync(ConfirmEmailRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ConfirmEmailCommand(request.UserId, request.Token), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Demande un lien de réinitialisation.</summary>
    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest request, ISender sender, CancellationToken ct)
    {
        await sender.Send(new RequestPasswordResetCommand(request.Email), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(
            new ResetPasswordCommand(request.Email, request.Token, request.NewPassword), ct))
            .Match(() => Results.NoContent());

    /// <summary>Vérifie l'adresse à partir du code à six chiffres.</summary>
    private static async Task<IResult> VerifyEmailByCodeAsync(
        VerifyEmailRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ConfirmEmailByEmailCommand(request.Email, request.Code), ct))
            .Match(() => Results.NoContent());

    /// <summary>Renvoie un code de vérification.</summary>
    private static async Task<IResult> ResendEmailVerificationAsync(
        ResendEmailRequest request, ISender sender, CancellationToken ct)
    {
        await sender.Send(new RequestEmailVerificationByEmailCommand(request.Email), ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Authentifie l'utilisateur ; renvoie les jetons ou l'exigence d'un code MFA.
    /// </summary>
    private static async Task<IResult> LoginAsync(LoginRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new LoginCommand(request.Email, request.Password, request.MfaCode), ct);
        return result.Match(response => Results.Ok(response));
    }

    /// <summary>Échange un refresh token valide contre une nouvelle paire de jetons.</summary>
    private static async Task<IResult> RefreshAsync(RefreshRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new RefreshTokenCommand(request.RefreshToken), ct);
        return result.Match(tokens => Results.Ok(tokens));
    }

    /// <summary>
    /// Rejoue le mot de passe d'une session ouverte et rend une paire de jetons
    /// dont l'`auth_time` est neuf.
    /// </summary>
    private static async Task<IResult> ReauthenticateAsync(
        ReauthenticateRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        var result = await sender.Send(new ReauthenticateCommand(userId, request.Password), ct);
        return result.Match(tokens => Results.Ok(tokens));
    }

    private static async Task<IResult> LogoutByRefreshTokenAsync(
        RefreshRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new LogoutByRefreshTokenCommand(request.RefreshToken), ct))
            .Match(() => ApiResults.Ok(new { revoked = true }));

    /// <summary>Demande d'un code à usage unique.</summary>
    private static async Task<IResult> RequestOtpAsync(
        OtpRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new IssueOtpChallengeCommand(request.Login, request.Channel), ct))
            .Match(challenge => ApiResults.Ok(challenge));

    /// <summary>Vérifie le code et, s'il est bon, OUVRE LA SESSION.</summary>
    private static async Task<IResult> VerifyOtpAsync(
        VerifyOtpRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new VerifyOtpCommand(request.ChallengeId, request.Code), ct))
            .Match(verification => ApiResults.Ok(verification));

    // ----------------------------------------------------------------- Account

    private static void MapAccount(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity/account").WithTags("Identity · Account").RequireAuthorization();

        group.MapGet("/me", GetMeAsync).WithName("GetMe");
        group.MapPut("/me", UpdateMeAsync).WithName("UpdateProfile");
        group.MapPost("/me/change-password", ChangePasswordAsync).WithName("ChangePassword");
        group.MapPost("/me/logout", LogoutAsync).WithName("Logout");
        group.MapPost("/me/mfa/setup", BeginMfaAsync).WithName("BeginMfaSetup");
        group.MapPost("/me/mfa/confirm", ConfirmMfaAsync).WithName("ConfirmMfa");
        group.MapPost("/me/mfa/disable", DisableMfaAsync).WithName("DisableMfa");

        // DEUX COMMANDES ÉCRITES DEPUIS L'ORIGINE, ET QUE PERSONNE N'APPELAIT.
        group.MapDelete("/me", DeleteAccountAsync).WithName("DeleteAccount");
        group.MapPost("/me/accept-terms", AcceptTermsAsync).WithName("AcceptTerms");
    }

    /// <summary>L'utilisateur supprime son propre compte.</summary>
    private static async Task<IResult> DeleteAccountAsync(
        [FromBody] DeleteAccountRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new DeleteAccountCommand(userId, request.Password), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Enregistre l'acceptation d'une version des conditions générales.</summary>
    private static async Task<IResult> AcceptTermsAsync(
        AcceptTermsRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new AcceptTermsCommand(userId, request.Version), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Renvoie le profil de l'utilisateur authentifié.</summary>
    private static async Task<IResult> GetMeAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new GetUserQuery(userId), ct);
        return result.Match(summary => Results.Ok(summary));
    }

    /// <summary>Met à jour le profil de l'utilisateur authentifié.</summary>
    private static async Task<IResult> UpdateMeAsync(UpdateProfileRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new UpdateUserProfileCommand(userId, request.FirstName, request.LastName, request.PhoneNumber), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Change le mot de passe de l'utilisateur authentifié.</summary>
    private static async Task<IResult> ChangePasswordAsync(ChangePasswordRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ChangePasswordCommand(userId, request.CurrentPassword, request.NewPassword), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Déconnecte un appareil en révoquant son refresh token.</summary>
    private static async Task<IResult> LogoutAsync(LogoutRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new LogoutCommand(userId, request.RefreshToken), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Initie l'activation MFA : renvoie le secret et l'URI otpauth (QR code).</summary>
    private static async Task<IResult> BeginMfaAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new BeginMfaSetupCommand(userId), ct);
        return result.Match(setup => Results.Ok(setup));
    }

    /// <summary>Confirme l'activation MFA avec un premier code TOTP.</summary>
    private static async Task<IResult> ConfirmMfaAsync(MfaCodeRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ConfirmMfaCommand(userId, request.Code), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Désactive la MFA après vérification d'un code TOTP.</summary>
    private static async Task<IResult> DisableMfaAsync(MfaCodeRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new DisableMfaCommand(userId, request.Code), ct);
        return result.Match(() => Results.NoContent());
    }

    // ------------------------------------------------------------- Admin users

    private static void MapAdminUsers(IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/identity/users")
            .WithTags("Identity · Admin Users")
            .RequireAuthorization(policy => policy.RequireRole("Admin"));

        // LA LISTE MANQUAIT, ET LA REQUÊTE ÉTAIT DÉJÀ ÉCRITE.
        group.MapGet("/", ListUsersAsync).WithName("ListUsers");

        group.MapGet("/{id:guid}", GetUserAsync).WithName("GetUser");

        // L'APPROBATION — LA ROUTE QUE LE MESSAGE DE CONNEXION PROMETTAIT DÉJÀ.
        group.MapPost("/{id:guid}/approve", ApproveUserAsync).WithName("ApproveUser");

        // L'ATTESTATION D'ADRESSE — LE SECOND MAILLON, ET IL EST BLOQUANT AILLEURS.
        group.MapPost("/{id:guid}/email-verified", MarkEmailVerifiedAsync)
            .WithName("MarkEmailVerified");

        group.MapPost("/{id:guid}/suspend", SuspendUserAsync).WithName("SuspendUser");
        group.MapPost("/{id:guid}/reactivate", ReactivateUserAsync).WithName("ReactivateUser");
        group.MapPost("/{id:guid}/roles", AssignRoleAsync).WithName("AssignRole");
        group.MapDelete("/{id:guid}/roles/{roleId:guid}", RemoveRoleAsync).WithName("RemoveRole");
    }

    /// <summary>Page de comptes, avec recherche, filtre de statut et tri (Admin).</summary>
    private static async Task<IResult> ListUsersAsync(
        int? page, int? pageSize, string? search, string? status, string? sort, string? dir,
        ISender sender, CancellationToken ct)
    {
        var demande = new ListUsersQuery(
            Page: page ?? 1, Search: search, Status: status, Sort: sort, Dir: dir);

        // `PageSize` N'EST POSÉ QUE S'IL EST DEMANDÉ, ET CE N'EST PAS DU STYLE.
        var result = await sender.Send(
            pageSize is { } taille ? demande with { PageSize = taille } : demande, ct);

        // Lambda et non groupe de méthodes : `ApiResults.Page` porte DEUX
        // surcharges, et la forme explicite dit laquelle sans dépendre de la
        // résolution.
        return result.Match(resultat => ApiResults.Page(resultat));
    }

    /// <summary>Récupère un compte par son identifiant (Admin).</summary>
    private static async Task<IResult> GetUserAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetUserQuery(id), ct);
        return result.Match(summary => Results.Ok(summary));
    }

    /// <summary>Suspend un compte (Admin).</summary>
    private static async Task<IResult> SuspendUserAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new SuspendUserCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Active un compte en attente (Admin).</summary>
    private static async Task<IResult> ApproveUserAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ApproveUserCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Atteste que l'adresse appartient au titulaire (Admin).</summary>
    private static async Task<IResult> MarkEmailVerifiedAsync(
        Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new MarkEmailVerifiedCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Réactive un compte suspendu (Admin).</summary>
    private static async Task<IResult> ReactivateUserAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ReactivateUserCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Assigne un rôle à un compte (Admin).</summary>
    private static async Task<IResult> AssignRoleAsync(Guid id, AssignRoleRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new AssignRoleCommand(id, request.RoleId), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Retire un rôle d'un compte (Admin).</summary>
    private static async Task<IResult> RemoveRoleAsync(Guid id, Guid roleId, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new RemoveRoleCommand(id, roleId), ct);
        return result.Match(() => Results.NoContent());
    }

    // ------------------------------------------------------------------ Roles

    private static void MapRoles(IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/identity/roles")
            .WithTags("Identity · Roles")
            .RequireAuthorization(policy => policy.RequireRole("Admin"));

        group.MapGet("/", ListRolesAsync).WithName("ListRoles");
        group.MapGet("/{id:guid}", GetRoleAsync).WithName("GetRole");
        group.MapPost("/", CreateRoleAsync).WithName("CreateRole");
        group.MapPut("/{id:guid}", UpdateRoleAsync).WithName("UpdateRole");
        group.MapPut("/{id:guid}/permissions", SetRolePermissionsAsync).WithName("SetRolePermissions");
        group.MapDelete("/{id:guid}", DeleteRoleAsync).WithName("DeleteRole");
    }

    /// <summary>Liste tous les rôles (Admin).</summary>
    private static async Task<IResult> ListRolesAsync(ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListRolesQuery(), ct);
        return result.Match(roles => Results.Ok(roles));
    }

    /// <summary>Récupère un rôle et ses permissions (Admin).</summary>
    private static async Task<IResult> GetRoleAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetRoleQuery(id), ct);
        return result.Match(role => Results.Ok(role));
    }

    /// <summary>Crée un rôle (Admin).</summary>
    private static async Task<IResult> CreateRoleAsync(CreateRoleRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new CreateRoleCommand(request.Name, request.Description, request.Permissions), ct);
        return result.Match(id => Results.Created($"/api/identity/roles/{id}", new { id }));
    }

    /// <summary>Met à jour le nom et la description d'un rôle (Admin).</summary>
    private static async Task<IResult> UpdateRoleAsync(Guid id, UpdateRoleRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new UpdateRoleCommand(id, request.Name, request.Description), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Remplace l'ensemble des permissions d'un rôle (Admin).</summary>
    private static async Task<IResult> SetRolePermissionsAsync(Guid id, SetPermissionsRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new SetRolePermissionsCommand(id, request.Permissions), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Supprime un rôle non-système (Admin).</summary>
    private static async Task<IResult> DeleteRoleAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new DeleteRoleCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    // -------------------------------------------------------------- Utilitaire

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    // ------------------------------------------------------- Contrats d'entrée

    public sealed record RegisterRequest(string FirstName, string LastName, string Email, string PhoneNumber, string Password);
    public sealed record ConfirmEmailRequest(Guid UserId, string Token);
    public sealed record LoginRequest(string Email, string Password, string? MfaCode);
    public sealed record RefreshRequest(string RefreshToken);

    /// <summary>Corps de `POST /api/v1/auth/reauthenticate` (§37).</summary>
    public sealed record ReauthenticateRequest(string Password);

    /// <summary>Corps de `POST /api/v1/auth/otp/request`.</summary>
    public sealed record OtpRequest(string? Login, string? Channel);

    /// <summary>Corps de `POST /api/v1/auth/verify-otp` (§10.1).</summary>
    public sealed record VerifyOtpRequest(Guid ChallengeId, string? Code);
    public sealed record ForgotPasswordRequest(string Email);

    public sealed record ResendEmailRequest(string Email);

    public sealed record VerifyEmailRequest(string Email, string Code);

    /// <param name="Token">Le jeton reçu par e-mail, non son empreinte.</param>
    public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);
    public sealed record LogoutRequest(string RefreshToken);
    public sealed record UpdateProfileRequest(string FirstName, string LastName, string PhoneNumber);
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    /// <param name="Password">Le mot de passe COURANT, revérifié avant l'anonymisation.</param>
    public sealed record DeleteAccountRequest(string Password);

    /// <param name="Version">
    /// La version EXACTE du texte affiché à l'utilisateur, telle que l'application
    /// la connaît.
    /// </param>
    public sealed record AcceptTermsRequest(string Version);
    public sealed record MfaCodeRequest(string Code);
    public sealed record AssignRoleRequest(Guid RoleId);
    public sealed record CreateRoleRequest(string Name, string? Description, IReadOnlyList<string>? Permissions);
    public sealed record UpdateRoleRequest(string Name, string? Description);
    public sealed record SetPermissionsRequest(IReadOnlyList<string> Permissions);
}
