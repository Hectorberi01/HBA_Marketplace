using HBA.Identity.Application.Models;
using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Users.Commands.Reauthenticate;

/// <summary>
/// Rejoue la preuve d'identité d'une session DÉJÀ ouverte, et rend une paire de
/// jetons dont l'<c>auth_time</c> est neuf.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE STEP-UP DU §37 — CE QUE CETTE COMMANDE EST, ET CE QU'ELLE N'EST PAS.
///
/// Six permissions sont classées Critiques : configurer un compte de versement,
/// demander un virement, changer les coordonnées bancaires, fermer le dossier,
/// transférer la propriété, modifier la politique de sécurité. Toutes déplacent
/// de l'argent ou l'accès à l'argent, et toutes sont irréversibles à l'échelle de
/// la journée. Les autoriser sur la seule présentation d'un jeton valide, c'est
/// les autoriser à quiconque a saisi un poste laissé ouvert.
///
/// CE N'EST PAS UNE CONNEXION.
///
/// L'appelant est DÉJÀ authentifié — son identifiant vient du jeton, jamais du
/// corps. Il ne fournit qu'un mot de passe, et n'obtient aucun droit nouveau :
/// exactement les mêmes rôles, exactement les mêmes permissions. La seule chose
/// qui change est `auth_time`, donc la fraîcheur.
///
/// Reprendre `LoginCommand` aurait été plus court et faux : cette commande-là
/// prend un e-mail dans le corps. Le pire scénario que cela ouvre est un compte
/// qui « se réauthentifie » en présentant les identifiants D'UN AUTRE, et repart
/// avec les jetons de celui-là — un changement d'identité déguisé en
/// confirmation.
///
/// NI MFA, NI DÉFI SUPPLÉMENTAIRE ICI.
///
/// Le second facteur a joué à la connexion et `amr` en garde la trace ; le
/// redemander toutes les cinq minutes ferait décrocher le vendeur qui enchaîne
/// deux virements. Le jour où une permission exigera `mfa` et non seulement
/// `pwd`, c'est ce handler qui devra faire rejouer le facteur — et `amr` est
/// déjà là pour le dire.
///
/// ET ELLE FAIT TOURNER LE JETON DE RAFRAÎCHISSEMENT.
///
/// Elle passe par le même émetteur que la connexion, donc en émet un neuf. Ne pas
/// le faire aurait laissé en base un jeton portant l'ANCIEN `auth_time` : le
/// prochain rafraîchissement aurait annulé la réauthentification qu'on vient de
/// payer, et le vendeur aurait ressaisi son mot de passe pour rien.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="UserId">Vient du JETON, jamais du corps de la requête.</param>
public sealed record ReauthenticateCommand(Guid UserId, string Password) : ICommand<AuthTokens>;
