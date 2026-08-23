using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using HBA.Identity.Contracts;
using HBA.Identity.Domain.Roles;
using HBA.Identity.Domain.Users;
using HBA.Identity.Infrastructure.Persistence;
using HBA.Identity.Infrastructure.Security;

namespace HBA.Identity.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Identity. Lecture seule,
/// projetée vers le DTO de Contracts. Les autres modules ne voient que ça.
/// </summary>
internal sealed class IdentityModuleApi : IIdentityModuleApi
{
    private readonly IdentityDbContext _dbContext;
    private readonly JwtOptions _jwt;

    public IdentityModuleApi(IdentityDbContext dbContext, JwtOptions jwt)
    {
        _dbContext = dbContext;
        _jwt = jwt;
    }

    public async Task<UserSummary?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var id = new UserId(userId);
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(u => u.RoleAssignments)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        return user is null ? null : Map(user);
    }

    public async Task<UserSummary?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var emailResult = Email.Create(email);
        if (emailResult.IsFailure)
        {
            return null;
        }

        var value = emailResult.Value;
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(u => u.RoleAssignments)
            .FirstOrDefaultAsync(u => u.Email == value, cancellationToken);

        return user is null ? null : Map(user);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// VALIDATION D'UN JETON D'ACCÈS — DEUX CONTRÔLES, PAS UN.
    ///
    /// 1. La SIGNATURE et les dates, comme le ferait n'importe quel service.
    /// 2. Le SECURITY STAMP, que seul identity-service peut vérifier.
    ///
    /// Le second est la raison d'être de ce RPC. Le jeton porte le `security_stamp`
    /// du compte au moment de son émission ; le compte fait tourner ce tampon à
    /// chaque événement qui doit invalider les sessions — suspension, changement de
    /// mot de passe, révocation explicite. Comparer les deux permet de refuser un
    /// jeton cryptographiquement valide mais métier-mort.
    ///
    /// Sans ce contrôle, un compte suspendu conserve un accès complet pendant toute
    /// la durée de vie de son jeton. Quinze minutes suffisent à vider un wallet.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<AccessTokenValidation> ValidateAccessTokenAsync(
        string accessToken, CancellationToken cancellationToken = default)
    {
        var handler = new JwtSecurityTokenHandler();

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = _jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)),
            ValidateLifetime = true,

            // Zéro tolérance d'horloge, contrairement aux cinq minutes par défaut.
            //
            // La valeur par défaut de la bibliothèque accepte un jeton expiré depuis
            // cinq minutes. Sur un contrôle destiné aux paiements et aux retraits,
            // c'est cinq minutes de sursis offertes à un jeton volé.
            ClockSkew = TimeSpan.Zero
        };

        ClaimsPrincipal principal;

        try
        {
            principal = handler.ValidateToken(accessToken, parameters, out _);
        }
        catch (SecurityTokenExpiredException)
        {
            return Rejected(TokenRejectionReasons.Expired);
        }
        catch (Exception)
        {
            // Signature fausse, jeton illisible, algorithme inattendu : tout se
            // ramène à « ce jeton n'est pas de nous ». Détailler la cause au client
            // aiderait surtout celui qui essaie de forger.
            return Rejected(TokenRejectionReasons.SignatureInvalid);
        }

        // `FindFirst(...)?.Value` et non l'extension de confort d'ASP.NET Core.
        //
        // Celle-ci n'existe que dans les projets qui référencent Microsoft.AspNetCore.App.
        // Ce projet est une bibliothèque de persistance : lui ajouter tout le framework
        // web pour une méthode de confort serait un mauvais échange. Le BCL fait la
        // même chose en un caractère de plus.
        var subject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(subject, out var userId))
        {
            return Rejected(TokenRejectionReasons.SignatureInvalid);
        }

        var id = new UserId(userId);
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(u => u.RoleAssignments)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user is null)
        {
            return Rejected(TokenRejectionReasons.UserUnknown);
        }

        if (user.Status != UserStatus.Active)
        {
            return Rejected(TokenRejectionReasons.UserSuspended);
        }

        var stamp = principal.FindFirst(JwtTokenGenerator.SecurityStampClaimType)?.Value;

        if (!string.Equals(stamp, user.SecurityStamp.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            // Le tampon a tourné depuis l'émission : mot de passe changé, sessions
            // révoquées, compte réactivé. Le jeton est valide et périmé à la fois.
            return Rejected(TokenRejectionReasons.UserSuspended);
        }

        var authorization = await LoadAuthorizationAsync(user, cancellationToken);

        return new AccessTokenValidation(
            true, userId, authorization.Roles, authorization.Permissions, null);
    }

    public async Task<UserAuthorization?> GetUserRolesAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var id = new UserId(userId);
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(u => u.RoleAssignments)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        return user is null ? null : await LoadAuthorizationAsync(user, cancellationToken);
    }

    /// <summary>
    /// Révoque toutes les sessions : les jetons de rafraîchissement sont marqués
    /// révoqués ET le tampon de sécurité tourne.
    ///
    /// LES DEUX SONT NÉCESSAIRES, ET POUR DES RAISONS DIFFÉRENTES.
    ///
    /// Révoquer les jetons de rafraîchissement empêche d'obtenir un NOUVEAU jeton
    /// d'accès. Faire tourner le tampon invalide ceux DÉJÀ ÉMIS. N'en faire qu'un
    /// laisse une porte ouverte : sans rotation, l'attaquant garde son accès
    /// jusqu'à expiration ; sans révocation, il le renouvelle indéfiniment.
    /// </summary>
    public async Task<int> RevokeUserSessionsAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var id = new UserId(userId);
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user is null)
        {
            return 0;
        }

        var revoked = user.RevokeAllSessions();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return revoked;
    }

    private async Task<UserAuthorization> LoadAuthorizationAsync(
        User user, CancellationToken cancellationToken)
    {
        var roleIds = user.RoleIds.Select(r => new RoleId(r)).ToList();

        var roles = await _dbContext.Roles
            .AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        var permissions = roles
            .SelectMany(r => r.Permissions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var names = roles
            .Select(r => r.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new UserAuthorization(user.Id.Value, names, permissions);
    }

    private static AccessTokenValidation Rejected(string reason)
        => new(false, Guid.Empty, Array.Empty<string>(), Array.Empty<string>(), reason);

    private static UserSummary Map(User user) => new(
        user.Id.Value,
        user.FirstName,
        user.LastName,
        user.Email.Value,
        user.PhoneNumber.Value,
        user.Status.ToString(),
        user.EmailVerified,
        user.MfaEnabled,
        user.RoleIds.ToList());
}
