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
        // Politique « auth » (30 req/min/IP) comme sur les quatre BFF. C'était le seul
        // groupe d'authentification à retomber sur la limite globale de 300/min —
        // dix fois plus permissif sur les routes qui reçoivent des mots de passe.
        // Cet hôte n'est plus routé à l'edge, ce qui atténue sans corriger.
        // §10.1 : `/api/v1/auth`. La passerelle garde `/api/identity/auth` en coquille
        // de dépréciation le temps que les clients installés migrent — voir D3 dans
        // docs/DECISIONS.md.
        var group = app.MapGroup("/api/v1/auth").WithTags("Identity · Auth")
            .RequireRateLimiting(AuthRateLimiter.PolicyName);

        group.MapPost("/register", RegisterAsync).WithName("Register").AllowAnonymous();
        group.MapPost("/confirm-email", ConfirmEmailAsync).WithName("ConfirmEmail").AllowAnonymous();
        group.MapPost("/login", LoginAsync).WithName("Login").AllowAnonymous();
        group.MapPost("/refresh", RefreshAsync).WithName("RefreshToken").AllowAnonymous();

        // ═════════════════════════════════════════════════════════════════════
        // LE STEP-UP DU §37 — LA SEULE ROUTE `/auth` QUI EXIGE UN JETON.
        //
        // ELLE N'EST PAS `AllowAnonymous`, ET C'EST LA MOITIÉ DE SA SÛRETÉ.
        //
        // Toutes ses voisines le sont : on se connecte, on rafraîchit, on se
        // déconnecte sans session valide. Celle-ci CONFIRME une session ouverte.
        // Anonyme, avec un e-mail dans le corps, elle serait une seconde route de
        // connexion — moins testée que `/login`, et qui rendrait des jetons
        // « fraîchement authentifiés » à qui présente n'importe quel identifiant.
        // L'identifiant vient donc du jeton, et le corps ne porte QUE le mot de
        // passe.
        //
        // ELLE RESTE DANS LE GROUPE `/auth`, DONC SOUS LE LIMITEUR À 30/min.
        //
        // C'est une route qui reçoit des mots de passe. La ranger sous `/account`,
        // où elle aurait aussi eu sa place logiquement, l'aurait fait retomber sur
        // la limite globale — dix fois plus permissive, sur exactement le geste
        // qu'on essaie en boucle.
        // ═════════════════════════════════════════════════════════════════════
        group.MapPost("/reauthenticate", ReauthenticateAsync)
            .WithName("Reauthenticate").RequireAuthorization();

        // LE §10.1 PLACE LA DÉCONNEXION SOUS `/auth`, PAS SOUS `/account`.
        //
        // Elle vivait sur `/api/identity/account/me/logout`, donc derrière
        // `RequireAuthorization`. Or on se déconnecte précisément quand on doute de
        // son jeton : exiger un jeton valide pour le révoquer refuse le service à
        // celui qui en a le plus besoin. Ici la preuve est le jeton de
        // rafraîchissement présenté dans le corps, et l'ancienne route reste en place.
        group.MapPost("/logout", LogoutByRefreshTokenAsync)
            .WithName("LogoutByRefreshToken").AllowAnonymous().AllowIdempotency();

        // §10.1 : `POST /api/v1/auth/verify-otp`. La table `mfa_challenges` n'avait
        // aucun agrégat et cet endpoint n'existait pas.
        group.MapPost("/otp/request", RequestOtpAsync).WithName("RequestOtp").AllowAnonymous();
        group.MapPost("/verify-otp", VerifyOtpAsync).WithName("VerifyOtp").AllowAnonymous();

        // ═════════════════════════════════════════════════════════════════════
        // UN MOT DE PASSE OUBLIÉ ÉTAIT DÉFINITIF.
        //
        // `RequestPasswordResetCommand` et `ResetPasswordCommand` sont écrites,
        // testées, protégées contre la force brute — et aucune route ne les
        // appelait. Un compte dont on perdait le mot de passe était perdu : ni
        // récupération, ni contournement, sauf intervention en base.
        //
        // C'est aussi ce qui rendait absurde la garde de démarrage de
        // communication-service, qui REFUSE de démarrer en production sans canal
        // e-mail « parce que sans lui, aucun mot de passe oublié ne peut être
        // récupéré ». Le canal était exigé ; le geste qui l'utilise, absent.
        //
        // `/password/forgot` RÉPOND TOUJOURS 204, MÊME SUR UNE ADRESSE INCONNUE.
        //
        // Distinguer les deux cas transformerait cette route en oracle : un
        // attaquant y testerait des adresses pour savoir lesquelles ont un compte
        // sur la plateforme. Le handler applique déjà cette règle ; la route ne
        // doit pas la défaire en traduisant un échec en 404.
        // ═════════════════════════════════════════════════════════════════════
        group.MapPost("/password/forgot", ForgotPasswordAsync).WithName("ForgotPassword").AllowAnonymous();
        group.MapPost("/password/reset", ResetPasswordAsync).WithName("ResetPassword").AllowAnonymous();

        // ANONYME, ET PAR ADRESSE — PARCE QUE L'INSCRIPTION NE CONNECTE PAS.
        //
        // Mon premier jet exigeait un jeton et lisait le `UserId` dedans. C'était
        // correct et inutilisable : le compte naît en attente de vérification,
        // sans session, et l'écran « saisissez le code reçu » est le PREMIER
        // après l'inscription. Son bouton « renvoyer » n'a rien à présenter.
        //
        // Elle répond 204 quoi qu'il arrive — adresse inconnue, compte déjà
        // vérifié, demande trop rapprochée. Le limiteur du groupe /auth borne
        // l'abus ; c'est lui la parade, pas une distinction dans la réponse.
        group.MapPost("/email/resend", ResendEmailVerificationAsync)
            .WithName("ResendEmailVerification").AllowAnonymous();

        // SECOND CHEMIN DE VÉRIFICATION, PAR ADRESSE — ET NON UN DOUBLON.
        //
        // `/confirm-email` prend un `userId` : c'est le contrat du LIEN cliquable,
        // qui le porte. Celui-ci sert l'écran « saisissez le code reçu », où
        // l'utilisateur n'a que l'adresse qu'il vient de taper — notamment quand
        // il arrive depuis une tentative de connexion sur un compte non vérifié.
        //
        // Les deux existent parce que les deux parcours existent. Supprimer l'un
        // obligerait l'autre à inventer ce qu'il n'a pas.
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

    /// <summary>
    /// Demande un lien de réinitialisation. Répond 204 quoi qu'il arrive.
    /// </summary>
    /// <remarks>
    /// NE JAMAIS TRADUIRE L'ÉCHEC EN 404.
    ///
    /// « Cette adresse n'a pas de compte » est exactement le renseignement qu'un
    /// attaquant vient chercher. Le handler traite l'adresse inconnue comme un
    /// succès silencieux ; la route se contente de ne pas défaire ce choix.
    /// </remarks>
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
    /// <remarks>
    /// Contrairement à ses voisines anonymes, celle-ci DIT l'échec — sans
    /// distinguer « compte inconnu » de « code faux ». Elle exige un secret que
    /// l'attaquant n'a pas ; taire l'erreur empêcherait surtout l'utilisateur
    /// légitime de comprendre qu'il s'est trompé ou que son code a expiré.
    /// </remarks>
    private static async Task<IResult> VerifyEmailByCodeAsync(
        VerifyEmailRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ConfirmEmailByEmailCommand(request.Email, request.Code), ct))
            .Match(() => Results.NoContent());

    /// <summary>Renvoie un code de vérification. 204 dans tous les cas.</summary>
    private static async Task<IResult> ResendEmailVerificationAsync(
        ResendEmailRequest request, ISender sender, CancellationToken ct)
    {
        await sender.Send(new RequestEmailVerificationByEmailCommand(request.Email), ct);
        return Results.NoContent();
    }

    /// <summary>Authentifie l'utilisateur ; renvoie les jetons ou l'exigence d'un code MFA.</summary>
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
    /// dont l'`auth_time` est neuf. Voir <c>ReauthenticateCommand</c>.
    /// </summary>
    /// <remarks>
    /// LA RÉPONSE EST UNE PAIRE COMPLÈTE, PAS UN SIMPLE « ok ».
    ///
    /// Le client DOIT remplacer ses deux jetons : l'ancien jeton d'accès porte
    /// toujours l'ancien `auth_time`, et l'ancien jeton de rafraîchissement vient
    /// d'être révoqué par la rotation. Un client qui garderait les siens
    /// ressaisirait son mot de passe puis se verrait refuser le virement — le pire
    /// des deux mondes.
    /// </remarks>
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

    /// <summary>
    /// Demande d'un code à usage unique. Rend TOUJOURS un défi, même pour un compte
    /// inconnu — voir l'encadré d'<see cref="IssueOtpChallengeCommand"/> : distinguer
    /// les deux cas ferait de cet endpoint un oracle d'existence de comptes.
    /// </summary>
    private static async Task<IResult> RequestOtpAsync(
        OtpRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new IssueOtpChallengeCommand(request.Login, request.Channel), ct))
            .Match(challenge => ApiResults.Ok(challenge));

    /// <summary>
    /// Vérifie le code et, s'il est bon, OUVRE LA SESSION.
    /// </summary>
    /// <remarks>
    /// LA RÉPONSE PORTE DÉSORMAIS LES JETONS (ISSUE-062).
    ///
    /// Elle rendait `(Verified, Channel)` : le client apprenait que son code était
    /// bon et n'avait rien à en faire. La route était décorative de bout en bout —
    /// le code n'était d'ailleurs jamais livré non plus.
    ///
    /// C'EST UNE CONNEXION SANS MOT DE PASSE, et l'`amr` du jeton dit `otp`
    /// seul — ni `pwd`, ni `mfa`. Une garde qui exige un mot de passe récent
    /// refusera donc ce jeton, ce qui est le comportement voulu : entrer par SMS ne
    /// doit pas suffire à transférer la propriété d'un vendeur.
    /// </remarks>
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

        // ═════════════════════════════════════════════════════════════════════
        // DEUX COMMANDES ÉCRITES DEPUIS L'ORIGINE, ET QUE PERSONNE N'APPELAIT.
        //
        // `DeleteAccountCommand` et `AcceptTermsCommand` sont complètes, testées,
        // et n'avaient AUCUN appelant dans tout le dépôt — un `grep` ne les
        // trouvait que dans leur propre fichier. Il ne leur manquait que ces deux
        // lignes.
        //
        // LA SUPPRESSION DE COMPTE EST UNE EXIGENCE DE L'APP STORE (5.1.1(v)),
        // et elle est BLOQUANTE : une application qui permet de créer un compte
        // doit permettre de le supprimer depuis l'application elle-même. Renvoyer
        // vers un formulaire web ou un courriel ne suffit pas. Tant que cette
        // route n'existait pas, l'application vendeur ne pouvait pas être
        // soumise — quel que soit l'état du reste.
        //
        // `DELETE` AVEC UN CORPS, ET C'EST ASSUMÉ.
        //
        // Le mot de passe ne peut pas voyager en paramètre d'URL : il finirait
        // dans les journaux d'accès de la passerelle, dans l'historique du proxy
        // et dans les traces OpenTelemetry. La RFC 9110 ne l'interdit pas ; elle
        // dit seulement qu'un corps sur DELETE n'a pas de sémantique définie.
        // Ici elle l'est : c'est la preuve d'identité exigée par la commande.
        //
        // AUCUNE RÉÉCRITURE DE PASSERELLE N'EST NÉCESSAIRE. La route YARP
        // `identity-admin` proxifie `/api/identity/{**catch-all}` en
        // `Authenticated` sans transformer le chemin : ces deux routes sont
        // joignables dès qu'elles existent.
        // ═════════════════════════════════════════════════════════════════════
        group.MapDelete("/me", DeleteAccountAsync).WithName("DeleteAccount");
        group.MapPost("/me/accept-terms", AcceptTermsAsync).WithName("AcceptTerms");
    }

    /// <summary>L'utilisateur supprime son propre compte. IRRÉVERSIBLE.</summary>
    /// <remarks>
    /// Le mot de passe est revérifié par la commande : un téléphone déverrouillé
    /// posé sur une table ne doit pas suffire à effacer un historique de
    /// commandes. Un mot de passe faux rend 401
    /// (`identity.account.wrong_password`), pas 400 — l'application doit le
    /// présenter comme un refus d'identité, non comme une saisie invalide.
    ///
    /// 204 SUR UN COMPTE DÉJÀ SUPPRIMÉ : la commande est idempotente, et c'est
    /// délibéré. Un second appui sur le bouton ne doit pas faire croire que la
    /// première suppression a échoué.
    /// </remarks>
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
    /// <remarks>
    /// LA VERSION VIENT DU CLIENT, ET CE N'EST PAS UNE FAIBLESSE.
    ///
    /// C'est exactement le texte que l'utilisateur a eu sous les yeux.
    /// Enregistrer « la version courante » sans savoir laquelle a été affichée
    /// reviendrait à faire signer un document qu'on n'a pas montré — et le jour
    /// du litige, on ne saurait pas ce qui a été accepté.
    ///
    /// La lecture existait déjà : `UserSummary.AcceptedTermsVersion`, rendue par
    /// `GET /me`. On savait donc LIRE ce qui avait été accepté, sans pouvoir
    /// l'écrire.
    /// </remarks>
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

        // ═════════════════════════════════════════════════════════════════════
        // LA LISTE MANQUAIT, ET LA REQUÊTE ÉTAIT DÉJÀ ÉCRITE.
        //
        // `ListUsersQuery` et son gestionnaire existent depuis le début, avec
        // recherche, filtre de statut, tri, pagination ET comptage par statut.
        // `UserRepository.ListPagedAsync` les sert. Rien n'appelait cet ensemble :
        // aucune route ne le montait, et le code était donc mort — testé par
        // personne, exécuté jamais.
        //
        // CE QUE SON ABSENCE COÛTAIT : les cinq gestes d'administration
        // ci-dessous sont TOUS adressés par identifiant. Sans liste, il fallait
        // déjà connaître le GUID d'un compte pour le suspendre — c'est-à-dire
        // qu'aucune console ne pouvait exister, et qu'on suspendait un compte en
        // interrogeant la base à la main.
        //
        // ON N'INVENTE RIEN ICI : le contrat, les filtres et les facettes sont
        // ceux que la requête rendait déjà. La seule décision de cette ligne est
        // l'enveloppe — `ApiResults.Page`, qui préserve `Facets` dans `meta` et
        // porte le `requestId` que `Results.Ok` ne porte pas.
        // ═════════════════════════════════════════════════════════════════════
        group.MapGet("/", ListUsersAsync).WithName("ListUsers");

        group.MapGet("/{id:guid}", GetUserAsync).WithName("GetUser");

        // ═════════════════════════════════════════════════════════════════════
        // L'APPROBATION — LA ROUTE QUE LE MESSAGE DE CONNEXION PROMETTAIT DÉJÀ.
        //
        // `ApproveUserCommand`, son gestionnaire et `User.Approve()` existaient,
        // testés, appelés par AUCUNE route. C'est le même défaut que les six
        // routes de validation du catalogue : un geste écrit et injoignable.
        //
        // CE QUE SON ABSENCE COÛTAIT — UN CUL-DE-SAC COMPLET.
        //
        // `LoginCommandHandler` refuse un compte `PendingVerification` en
        // annonçant qu'il « sera activé » : « Désormais un compte naît
        // PendingVerification et n'en sort QUE PAR L'APPROBATION D'UN
        // ADMINISTRATEUR ». Or aucun administrateur ne pouvait approuver.
        //
        // Les trois sorties supposées étaient toutes fermées :
        //   . `/reactivate` rend 409 `identity.user.not_suspended` — il lève une
        //     SUSPENSION, il n'approuve pas une inscription ;
        //   . `/auth/email/verify` attend le code à six chiffres, envoyé par
        //     courriel — notification-service n'est pas déployé, aucun ne part ;
        //   . `/auth/confirm-email` attend le même jeton.
        //
        // Tout compte créé depuis la mise en production était donc DÉFINITIVEMENT
        // bloqué : il ne pouvait pas se connecter, et rien ne pouvait le
        // débloquer. La console affichait « À vérifier » sans aucun geste en face.
        //
        // ELLE N'EST PAS `MarkEmailVerified`, ET LA DISTINCTION EST DÉLIBÉRÉE.
        //
        // Approuver, c'est AUTORISER L'ACCÈS. Marquer une adresse vérifiée, c'est
        // CONSTATER qu'elle appartient bien à la personne. Le domaine sépare les
        // deux — `Approve()` ne touche pas `EmailVerified`, qui « redevient ce
        // qu'il prétend être : le constat qu'une adresse a été confirmée —
        // aujourd'hui faux pour tout le monde, et honnêtement faux ». Les fondre
        // ferait affirmer une vérification que personne n'a faite.
        //
        // IDEMPOTENTE : `Approve()` rend un succès sur un compte déjà actif. Un
        // double-clic ne produit pas d'erreur à expliquer.
        // ═════════════════════════════════════════════════════════════════════
        group.MapPost("/{id:guid}/approve", ApproveUserAsync).WithName("ApproveUser");

        // ═════════════════════════════════════════════════════════════════════
        // L'ATTESTATION D'ADRESSE — LE SECOND MAILLON, ET IL EST BLOQUANT
        //     AILLEURS.
        //
        // `MarkEmailVerifiedCommand` et `User.MarkEmailVerifiedByAdmin` étaient
        // eux aussi écrits et montés par aucune route. `ListUsersQuery` expose
        // pourtant déjà `EmailVerifiedByAdminOnUtc`, et son contrat dit que « la
        // console doit pouvoir le montrer — Oui et Oui, sur parole ne valent pas
        // la même chose ». La colonne existait, le geste non.
        //
        // CE QUE SON ABSENCE FERMAIT : L'ONBOARDING VENDEUR, ENTIÈREMENT.
        //
        // `RegisterSellerCommandHandler` lit le compte par gRPC et refuse net :
        // `sellers.seller.email_unverified` — « L'e-mail du compte doit être
        // vérifié avant l'onboarding vendeur ». Or `EmailVerified` est faux pour
        // TOUS les comptes de la plateforme, faute de service d'e-mailing
        // déployé, et l'approbation ne le pose pas.
        //
        // Aucun vendeur ne pouvait donc être inscrit, quel que soit le chemin.
        //
        // CE N'EST PAS UNE VÉRIFICATION, ET LE DOMAINE REFUSE DE FAIRE SEMBLANT.
        //
        // « Personne n'a cliqué de lien, personne n'a prouvé qu'il relevait cette
        // boîte. C'est une attestation humaine, tracée comme telle. » D'où une
        // colonne distincte — `EmailVerifiedByAdminOnUtc` — et non un simple
        // passage de `EmailVerified` à vrai : le jour où l'e-mailing existera, on
        // saura lesquelles ont été prouvées et lesquelles ont été attestées.
        //
        // ELLE RESTE SÉPARÉE DE L'APPROBATION. Fondre les deux gestes en un seul
        // bouton ferait attester une adresse à chaque activation de compte, y
        // compris quand l'administrateur n'a rien vérifié du tout.
        // ═════════════════════════════════════════════════════════════════════
        group.MapPost("/{id:guid}/email-verified", MarkEmailVerifiedAsync)
            .WithName("MarkEmailVerified");

        group.MapPost("/{id:guid}/suspend", SuspendUserAsync).WithName("SuspendUser");
        group.MapPost("/{id:guid}/reactivate", ReactivateUserAsync).WithName("ReactivateUser");
        group.MapPost("/{id:guid}/roles", AssignRoleAsync).WithName("AssignRole");
        group.MapDelete("/{id:guid}/roles/{roleId:guid}", RemoveRoleAsync).WithName("RemoveRole");
    }

    /// <summary>Page de comptes, avec recherche, filtre de statut et tri (Admin).</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// TOUS LES PARAMÈTRES SONT NULLABLES, ET C'EST CE QUI REND LA ROUTE
    ///    UTILISABLE SANS RIEN SAVOIR.
    ///
    /// `int?` et `string?` plutôt que des valeurs nues : un appel sans le moindre
    /// paramètre doit rendre la première page. Déclarer `int page` refuserait la
    /// requête sur un paramètre requis absent — c'est exactement le piège que
    /// portent les routes `wallet`, dont le `take` non nullable rend inatteignable
    /// la valeur par défaut de leur propre requête.
    ///
    /// LA RECHERCHE NE PORTE QUE SUR LE PRÉNOM ET LE NOM.
    ///
    /// `UserRepository.ListPagedAsync` explique pourquoi : « ILike uniquement sur
    /// des colonnes string simples : Email/PhoneNumber sont des value objects
    /// convertis, non traduisibles. » C'est une limite réelle, et c'est
    /// précisément la façon dont on cherche un compte en support — par son
    /// e-mail. La console le dit à l'écran plutôt que de laisser croire à une
    /// recherche globale qui ne trouve rien.
    ///
    /// `PageRequest.Normalize` borne ensuite page et taille DANS le gestionnaire :
    /// une taille venue de la requête ne peut pas rouvrir un balayage complet de
    /// la table des comptes. C'est aussi pourquoi cette méthode ne fixe pas de
    /// taille par défaut elle-même — la requête porte la sienne.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static async Task<IResult> ListUsersAsync(
        int? page, int? pageSize, string? search, string? status, string? sort, string? dir,
        ISender sender, CancellationToken ct)
    {
        var demande = new ListUsersQuery(
            Page: page ?? 1, Search: search, Status: status, Sort: sort, Dir: dir);

        // `PageSize` N'EST POSÉ QUE S'IL EST DEMANDÉ, ET CE N'EST PAS DU STYLE.
        //
        // Écrire `pageSize ?? PageRequest.DefaultPageSize` obligerait ce projet
        // d'API à référencer `HBA.Shared.Application` pour lire une constante —
        // une dépendance directe de plus, sur une couche qu'il n'utilise que par
        // MediatR. Laisser la requête appliquer sa propre valeur par défaut évite
        // la référence ET garde la valeur à un seul endroit.
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

    /// <summary>
    /// Active un compte en attente (Admin).
    ///
    /// N'AGIT PAS SUR `EmailVerified` : approuver autorise l'accès, vérifier
    /// constate qu'une adresse appartient bien à quelqu'un. Voir l'encadré de la
    /// route.
    /// </summary>
    private static async Task<IResult> ApproveUserAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ApproveUserCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>
    /// Atteste que l'adresse appartient au titulaire (Admin).
    ///
    /// N'ACTIVE PAS LE COMPTE : voir <c>ApproveUserAsync</c>. Les deux gestes
    /// sont distincts et le resteront.
    /// </summary>
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
    /// <remarks>
    /// PAS DE CHAMP `Email` NI `UserId`. L'identité vient du jeton — un
    /// identifiant accepté depuis le corps ne prouve rien, il se recopie.
    /// </remarks>
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

    /// <param name="Password">
    /// Le mot de passe COURANT, revérifié avant l'anonymisation. Voir
    /// <c>DeleteAccountCommand</c> : l'action est irréversible, et le titulaire
    /// doit prouver que c'est bien lui.
    /// </param>
    public sealed record DeleteAccountRequest(string Password);

    /// <param name="Version">
    /// La version EXACTE du texte affiché à l'utilisateur, telle que
    /// l'application la connaît. Pas « la dernière » : voir
    /// <c>AcceptTermsCommand</c>.
    /// </param>
    public sealed record AcceptTermsRequest(string Version);
    public sealed record MfaCodeRequest(string Code);
    public sealed record AssignRoleRequest(Guid RoleId);
    public sealed record CreateRoleRequest(string Name, string? Description, IReadOnlyList<string>? Permissions);
    public sealed record UpdateRoleRequest(string Name, string? Description);
    public sealed record SetPermissionsRequest(IReadOnlyList<string> Permissions);
}
