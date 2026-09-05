using System.Security.Claims;
using HBA.Merchants.Application.Sellers.Commands.ActivateSeller;
using HBA.Merchants.Application.Sellers.Commands.AddKybDocument;
using HBA.Merchants.Application.Sellers.Commands.ApproveKyb;
using HBA.Merchants.Application.Sellers.Commands.ApproveSellerReactivation;
using HBA.Merchants.Application.Sellers.Commands.DeleteSeller;
using HBA.Merchants.Application.Sellers.Commands.RegisterSeller;
using HBA.Merchants.Application.Sellers.Commands.RejectKyb;
using HBA.Merchants.Application.Sellers.Commands.RemoveKybDocument;
using HBA.Merchants.Application.Sellers.Commands.SubmitKyb;
using HBA.Merchants.Application.Sellers.Commands.RequestSellerClosure;
using HBA.Merchants.Application.Sellers.Commands.RequestSellerReactivation;
using HBA.Merchants.Application.Sellers.Commands.SetPayoutAccount;
using HBA.Merchants.Application.Sellers.Commands.SuspendSeller;
using HBA.Merchants.Application.Sellers.Commands.UpdateSellerMetadata;
using HBA.Merchants.Application.Sellers.Commands.UpdateSellerProfile;
using HBA.Merchants.Application.Sellers.Queries.GetSeller;
using HBA.Merchants.Application.Sellers.Queries.GetSellerByUser;
using HBA.Merchants.Application.Members;
using HBA.Merchants.Application.Sellers.Queries.ListSellers;
using HBA.Merchants.Application.Stores;
using HBA.Merchants.Contracts;
using HBA.Merchants.Domain.Members;
using HBA.Merchants.Domain.Sellers;
using HBA.Shared.Domain.Results;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Merchants.Api.Endpoints;

/// <summary>Surface HTTP initiale du service Merchant.</summary>
public static class MerchantEndpoints
{
    public static IEndpointRouteBuilder MapMerchantEndpoints(this IEndpointRouteBuilder app)
    {
        // ═════════════════════════════════════════════════════════════════════
        // TOUT CE QUI SUIT TENAIT DANS UN SEUL GROUPE « AUTHENTIFIÉ ».
        //
        // `MapAuthenticatedGroup` n'exige aucun rôle, et aucun handler n'ouvrait
        // le jeton. Un acheteur, avec le jeton que lui rend sa propre application
        // mobile — même clé de signature sur les cinq hôtes — et un sellerId
        // ramassé dans une fiche produit, obtenait :
        //
        //   POST /{id}/kyb/approve  → il validait SON PROPRE dossier KYB et
        //                             devenait vendeur sans qu'aucun humain n'ait
        //                             regardé une pièce ;
        //   POST /{id}/suspend      → il coupait la vente d'un concurrent, qui ne
        //                             pouvait pas se relever lui-même ;
        //   DELETE /{id}            → il effaçait un vendeur, ses boutiques et ses
        //                             pièces d'identité.
        //
        // La réparation est en deux temps : la gouvernance descend dans un groupe
        // administrateur, et ce qui reste au vendeur prouve la propriété dans le
        // handler — voir `DenyUnlessOwnSellerAsync`.
        // ═════════════════════════════════════════════════════════════════════

        // ═════════════════════════════════════════════════════════════════════
        // LA RACINE PASSE DE `/api/merchants` À `/api/v1/merchants` (D15).
        //
        // Sixième service aligné, après identity, media, promotions, users et
        // catalog. Comme pour eux, le changement ne tient QUE parce que la
        // passerelle garde une coquille de dépréciation sur l'ancien chemin :
        // toutes les applications déjà installées appellent `/api/merchants/...`,
        // et renommer sans coquille leur rendrait 404 sur toute la surface
        // vendeur, à la seconde du déploiement.
        //
        // ET IL FAUT AUSSI CHERCHER DANS `apps/api-gateway/.../HttpClients/`.
        //
        // La leçon du catalogue : `MerchantClient` appelle le service EN DIRECT,
        // avec un `HttpClient` pointé sur son adresse — le proxy n'est pas sur ce
        // chemin, donc la coquille ne le protège pas. Ses trois routes sont
        // migrées avec celles-ci.
        // ═════════════════════════════════════════════════════════════════════

        // ═════════════════════════════════════════════════════════════════════
        // L'INSCRIPTION RESTE OUVERTE À TOUT COMPTE AUTHENTIFIÉ. C'EST UNE
        //    EXCEPTION, ET ELLE EST OBLIGATOIRE.
        //
        // Tout le reste de cette surface exige désormais le rôle `Seller` (§22).
        // Ces deux routes-ci ne le peuvent pas : le rôle est greffé PAR
        // l'inscription — `SellerRegisteredIntegrationEvent` →
        // `GrantSellerRoleHandler`. L'exiger ici rendrait impossible de jamais le
        // devenir : il faudrait être vendeur pour pouvoir s'inscrire comme vendeur.
        //
        // `GET /me` suit la même logique : c'est l'appel par lequel l'application
        // découvre si le porteur du jeton EST vendeur. Le fermer au rôle ferait
        // rendre 403 là où la réponse attendue est « pas de dossier vendeur ».
        //
        // Les deux résolvent le vendeur DEPUIS le jeton, sans identifiant dans
        // l'URL : il n'y a rien à contrôler, et rien à fuiter.
        // ═════════════════════════════════════════════════════════════════════
        var inscription = app.MapAuthenticatedGroup("/api/v1/merchants").WithTags("Merchant · Sellers");
        inscription.MapGet("/me", GetMySellerAsync);
        inscription.MapPost("/", RegisterSellerAsync).AllowIdempotency();

        // ═════════════════════════════════════════════════════════════════════
        // LE RESTE DE LA SURFACE VENDEUR EXIGE LE RÔLE (§22).
        //
        // Avant, un jeton suffisait : n'importe quel ACHETEUR entrait, et seule la
        // garde d'appartenance — route par route — l'arrêtait. Cela tenait tant que
        // CHAQUE route portait sa garde, c'est-à-dire tant que personne n'en
        // ajoutait une en l'oubliant. La protection était une discipline, pas une
        // barrière.
        //
        // `MapSellerGroup` admet `Seller`, `Admin` et `Moderator` — les deux
        // derniers parce que `DenyUnlessOwnSellerAsync` les laisse déjà passer
        // délibérément, pour qu'un modérateur puisse corriger le dossier d'un
        // vendeur injoignable.
        // ═════════════════════════════════════════════════════════════════════
        var sellers = app.MapSellerGroup("/api/v1/merchants").WithTags("Merchant · Sellers");

        sellers.MapGet("/{sellerId:guid}", GetSellerAsync);
        sellers.MapPut("/{sellerId:guid}/profile", UpdateProfileAsync);
        sellers.MapPut("/{sellerId:guid}/metadata", UpdateMetadataAsync);
        sellers.MapPut("/{sellerId:guid}/payout-account", SetPayoutAsync);
        sellers.MapPost("/{sellerId:guid}/kyb/documents", AddKybDocumentAsync).AllowIdempotency();
        sellers.MapDelete("/{sellerId:guid}/kyb/documents/{documentId:guid}", RemoveKybDocumentAsync);

        // ═════════════════════════════════════════════════════════════════════
        // LA SOUMISSION DU DOSSIER (§10.3 : POST /kyc/submit) — ELLE MANQUAIT.
        //
        // Le passage en validation était jusqu'ici un EFFET DE BORD du dépôt de la
        // première pièce : le vendeur qui téléverse sa carte d'identité un lundi et
        // son registre le jeudi occupait la file d'un administrateur pendant trois
        // jours avec un dossier incomplet.
        //
        // LA BASCULE AUTOMATIQUE EST CONSERVÉE, DÉPRÉCIÉE, LE TEMPS QUE L'APP
        //    SUIVE.
        //
        // L'application vendeur déjà déployée n'appelle pas cette route. La retirer
        // aujourd'hui ferait que plus AUCUN dossier n'atteindrait la file de
        // validation — l'onboarding s'arrêterait net, sans erreur ni trace. Voir
        // l'encadré de `Seller.AddKybDocument` : la condition de retrait y est
        // écrite. Même recette que la coquille de dépréciation des routes (D15).
        //
        // Idempotente sur un dossier déjà en revue : l'app pourra l'appeler après
        // chaque dépôt sans avoir à savoir si c'est la première pièce.
        // ═════════════════════════════════════════════════════════════════════
        sellers.MapPost("/{sellerId:guid}/kyb/submit", SubmitKybAsync);

        // Demander n'est pas décider. La fermeture et la demande de réactivation
        // sont des actes du vendeur sur son propre compte ; l'approbation de cette
        // réactivation est plus bas, avec le reste de la gouvernance.
        sellers.MapPost("/{sellerId:guid}/close", RequestClosureAsync);
        sellers.MapPost("/{sellerId:guid}/reactivation", RequestReactivationAsync);

        // ─────────────────────────── Gouvernance ─────────────────────────────
        //
        // Même préfixe, politique différente : ces routes décident du SORT d'un
        // vendeur. Aucune n'a de sens signée par l'intéressé, et le contrôle de
        // propriété n'y serait pas seulement inutile, il serait à l'envers.
        //
        // `GET /` EST ICI, ET CE N'EST PAS UN EXCÈS DE ZÈLE. Tout compte inscrit
        // vidait le fichier fournisseurs de la plateforme en un appel, sans
        // paramètre : la liste rendait le `SellerSummary` COMPLET de chaque vendeur
        // — numéro du compte de retrait, RCCM, IFU, téléphone du gérant, références
        // des pièces d'identité.
        //
        // Le rôle administrateur reste nécessaire, mais il n'est plus SUFFISANT
        // pour excuser la charge utile : la liste rend désormais `SellerListItem`,
        // qui n'en porte rien. Une console a le droit d'afficher ces données — sur
        // la fiche qu'un humain ouvre, pas dans un listing qu'un écran charge au
        // réveil.
        var governance = app.MapAdminGroup("/api/v1/merchants").WithTags("Merchant · Gouvernance");
        governance.MapGet("/", ListSellersAsync);

        // ═════════════════════════════════════════════════════════════════════
        // L'ONBOARDING PAR UN ADMINISTRATEUR — ANNONCÉ PAR LE CONTRAT, JAMAIS
        //     MONTÉ.
        //
        // `RegisterSellerCommand` le documente noir sur blanc depuis le début :
        // « l'onboarding admin ne la fournit pas [la métadonnée société],
        // l'auto-inscription oui ». Deux appelants étaient donc prévus. Un seul
        // existait.
        //
        // CE QUE SON ABSENCE COÛTAIT. `RegisterSellerAsync` lit l'identifiant
        // dans le JETON (`CurrentUserId`) : elle inscrit l'appelant, jamais un
        // tiers. Une console d'administration ne pouvait donc créer aucun
        // vendeur, et la liste de gouvernance restait vide tant que personne ne
        // s'inscrivait depuis l'application vendeur — laquelle n'est pas
        // déployée. Le tableau de bord d'une plateforme sans vendeurs.
        //
        // `/inscriptions` ET NON `POST "/"`. Le groupe d'inscription monte déjà
        // `POST /api/v1/merchants`. Une seconde route POST sur le MÊME chemin ne
        // lèverait pas au démarrage : elle lèverait une `AmbiguousMatchException`
        // à la première requête, en production, sur un chemin qui marchait la
        // veille. Un segment distinct l'évite par construction.
        //
        // LE `UserId` VIENT DU CORPS, ET C'EST UNE ENTORSE ASSUMÉE.
        //
        // La règle du dépôt est « ne jamais accepter un identifiant librement
        // depuis le corps » — c'est ce que répètent le §22 et la validation
        // catalogue. Elle vise les routes où l'appelant pourrait se faire passer
        // pour un autre. Ici l'appelant EST l'administration, le groupe est
        // `MapAdminGroup`, et désigner quelqu'un d'autre est exactement l'objet
        // du geste. C'est le même arbitrage qu'`order-service`, dont la garde
        // d'appartenance commence par `IsInRole(AdminRole)`.
        //
        // LES TROIS REFUS DU GESTIONNAIRE RESTENT EN PLACE, ET IL FAUT LES
        //     CONNAÎTRE :
        //
        //   . `sellers.seller.user_not_found` — le compte n'existe pas côté
        //     identity (lecture gRPC, pas une table locale) ;
        //   . `sellers.seller.email_unverified` — l'adresse n'est pas vérifiée.
        //     Elle l'est par `POST /api/identity/users/{id}/email-verified`, et
        //     l'APPROBATION du compte ne suffit pas : les deux gestes sont
        //     distincts côté identity ;
        //   . `sellers.seller.already_seller` et `shop_name_taken`.
        //
        // Cette route n'affaiblit aucun de ces contrôles : elle ne fait que
        // donner un second appelant à la commande qui les porte.
        // ═════════════════════════════════════════════════════════════════════
        governance.MapPost("/inscriptions", RegisterSellerForUserAsync).AllowIdempotency();
        governance.MapPost("/{sellerId:guid}/kyb/approve", ApproveKybAsync);
        governance.MapPost("/{sellerId:guid}/kyb/reject", RejectKybAsync);
        governance.MapPost("/{sellerId:guid}/activate", ActivateSellerAsync);
        governance.MapPost("/{sellerId:guid}/suspend", SuspendSellerAsync);
        governance.MapPost("/{sellerId:guid}/lift-suspension", LiftSuspensionAsync);
        governance.MapPost("/{sellerId:guid}/reactivation/approve", ApproveReactivationAsync);
        governance.MapDelete("/{sellerId:guid}", DeleteSellerAsync);

        var stores = app.MapSellerGroup("/api/v1/merchants/{sellerId:guid}/stores").WithTags("Merchant · Stores");
        stores.MapGet("/", ListStoresAsync);
        stores.MapPost("/", CreateStoreAsync).AllowIdempotency();
        stores.MapGet("/{storeId:guid}", GetStoreAsync);
        stores.MapPut("/{storeId:guid}/profile", UpdateStoreProfileAsync);
        stores.MapPut("/{storeId:guid}/contact", UpdateStoreContactAsync);
        stores.MapPut("/{storeId:guid}/location", AttachStoreLocationAsync);
        stores.MapPut("/{storeId:guid}/opening-hours", SetOpeningHoursAsync);
        stores.MapPost("/{storeId:guid}/open", OpenStoreAsync);
        stores.MapPost("/{storeId:guid}/close", CloseStoreAsync);

        // SUSPENDRE UNE BOUTIQUE EST UNE SANCTION, PAS UNE FERMETURE.
        //
        // `SuspendStoreCommand` ne porte volontairement PAS de SellerId, et son
        // handler emprunte le chemin sans contrôle de propriété : le domaine la
        // déclare « décision d'admin ». Dans le groupe vendeur, cette absence de
        // SellerId devenait l'inverse d'une protection — n'importe quel inscrit
        // suspendait la boutique de son choix, et le propriétaire ne disposait
        // d'aucune route pour la rouvrir. Le vendeur garde `close`, qui est
        // réversible par lui.
        var storeGovernance = app.MapAdminGroup("/api/v1/merchants/{sellerId:guid}/stores")
            .WithTags("Merchant · Stores · Gouvernance");
        storeGovernance.MapPost("/{storeId:guid}/suspend", SuspendStoreAsync);
        storeGovernance.MapPost("/{storeId:guid}/lift-suspension", LiftStoreSuspensionAsync);

        // ═══════════════════════════════════════════════════════════════════════
        // L'ÉQUIPE.
        //
        // PAS DE `DenyUnlessOwnSellerAsync` SUR CES ROUTES, ET CE N'EST PAS UN
        //    OUBLI.
        //
        // Leur garde est la RÉSOLUTION D'APPARTENANCE, dans la couche Application :
        // « faites-vous partie de cette équipe, et avec quels droits ». Poser en
        // plus la garde de propriété donnerait deux sources de vérité pour la même
        // décision — et le jour où l'une serait oubliée sur une route, c'est la
        // plus permissive qui l'emporterait.
        //
        // Conséquence assumée : un ADMINISTRATEUR de la plateforme n'a pas
        // d'appartenance, donc ne compose pas l'équipe d'un commerçant. Ce n'est
        // pas un acte de gouvernance, et les routes qui le sont — suspension,
        // clôture — existent déjà.
        //
        // `MapSellerGroup` reste en première barrière : un acheteur est refusé
        // sans qu'on touche la base.
        // ═══════════════════════════════════════════════════════════════════════
        var members = app.MapSellerGroup("/api/v1/merchants/{sellerId:guid}/members")
            .WithTags("Merchant · Équipe");

        members.MapGet("/", ListMembersAsync);
        members.MapGet("/invitations", ListInvitationsAsync);
        members.MapPost("/invitations", InviteMemberAsync).AllowIdempotency();
        members.MapPost("/invitations/{invitationId:guid}/resend", ResendInvitationAsync);
        members.MapDelete("/invitations/{invitationId:guid}", RevokeInvitationAsync);
        members.MapGet("/{memberId:guid}", GetMemberAsync);
        members.MapPut("/{memberId:guid}/roles", SetMemberRolesAsync);
        members.MapPut("/{memberId:guid}/stores/{storeId:guid}", AssignMemberStoreAsync);
        members.MapDelete("/{memberId:guid}/stores/{storeId:guid}", UnassignMemberStoreAsync);
        members.MapPost("/{memberId:guid}/suspend", SuspendMemberAsync);
        members.MapPost("/{memberId:guid}/activate", ReactivateMemberAsync);
        members.MapDelete("/{memberId:guid}", RevokeMemberAsync);

        // ═════════════════════════════════════════════════════════════════════
        // LE TRANSFERT DE PROPRIÉTÉ — `OWNERSHIP_TRANSFER` NE GARDAIT RIEN
        //    (ISSUE-040).
        //
        // La permission était déclarée, critique, réservée au propriétaire, et
        // aucune route ne l'exigeait. Trois gardes du domaine renvoyaient pourtant
        // l'utilisateur vers ce geste : « le rôle de propriétaire se transfère »,
        // « il ne s'attribue que par un transfert de propriété », « transférez la
        // propriété d'abord ». Aucune des trois ne désignait quoi que ce soit.
        //
        // `POST` ET NON `PUT`, PARCE QUE CE N'EST PAS UNE PROPRIÉTÉ QU'ON ÉCRIT.
        //
        // C'est une opération, avec ses propres refus et son propre événement. Un
        // `PUT /{memberId}/roles` étendu aurait été plus court, et aurait rouvert
        // exactement ce que `EnsureCanAssign` referme : le rôle OWNER attribuable
        // comme un autre.
        //
        // ET LE STEP-UP EST PORTÉ ICI, PAS HÉRITÉ.
        //
        // Le groupe `members` ne passe DÉLIBÉRÉMENT pas par
        // `DenyUnlessOwnSellerAsync` — la garde d'appartenance y est dans le
        // domaine. Or c'est cette méthode qui applique la réauthentification pour
        // les permissions critiques. Sans la ligne ci-dessous, le geste le plus
        // irréversible du module serait le SEUL geste critique sans step-up.
        // ═════════════════════════════════════════════════════════════════════
        members.MapPost("/{memberId:guid}/ownership", TransferOwnershipAsync);

        // LE DÉPART VOLONTAIRE — AUCUN `memberId`, ET C'EST LE POINT.
        //
        // `SellerMember.Leave` existait sans appelant : pour quitter une équipe, il
        // fallait demander à quelqu'un de vous révoquer. La route ne porte pas
        // d'identifiant de membre — en porter un en ferait une révocation déguisée,
        // sans permission à exiger, c'est-à-dire le contournement de `MEMBER_REVOKE`.
        members.MapDelete("/me", LeaveSellerAsync);

        var memberRoles = app.MapSellerGroup("/api/v1/merchants/{sellerId:guid}/roles")
            .WithTags("Merchant · Rôles");

        memberRoles.MapGet("/", ListSellerRolesAsync);

        // ═══════════════════════════════════════════════════════════════════════
        // LES RÔLES TAILLÉS PAR LE VENDEUR (§18, lot A3).
        //
        // SANS CES TROIS ROUTES, LES NEUF RÔLES SYSTÈME ÉTAIENT TOUT CE QUI
        // EXISTAIT.
        //
        // `SellerRole.Custom`, `Update` et `EnsureDeletable` étaient écrites,
        // gardées contre l'escalade, testées — et injoignables : le groupe n'avait
        // qu'un `GET`. `ROLE_CREATE`, `ROLE_UPDATE` et `ROLE_DELETE` figuraient au
        // catalogue en ne gardant rien. Un vendeur dont l'organisation ne rentre
        // pas dans les neuf modèles n'avait aucun recours.
        //
        // PAS DE GARDE ICI : ELLE EST DANS LA COMMANDE, ET C'EST LA MÊME RAISON
        // QUE POUR LES MEMBRES.
        //
        // Elle ne se réduit pas à « est-ce votre dossier » : elle dépend des
        // permissions du rôle VISÉ comparées à celles de l'acteur (§11, on ne donne
        // pas ce qu'on n'a pas), ce qu'une garde d'entrée ne peut pas voir. Ce que
        // ces méthodes garantissent, c'est que l'identifiant transmis vient du
        // JETON et jamais du corps de la requête.
        //
        // `PATCH` ET NON `PUT`, MAIS LES PERMISSIONS SONT REMPLACÉES.
        //
        // Le verbe suit l'usage du dépôt pour les modifications partielles — le nom
        // et la description peuvent bouger indépendamment. La LISTE de permissions,
        // elle, est remplacée en bloc : voir `UpdateSellerRoleCommand` pour
        // pourquoi une fusion aurait exigé d'inventer une grammaire des retraits.
        // ═══════════════════════════════════════════════════════════════════════
        memberRoles.MapPost("/", CreateSellerRoleAsync).AllowIdempotency();
        memberRoles.MapPatch("/{roleId:guid}", UpdateSellerRoleAsync);
        memberRoles.MapDelete("/{roleId:guid}", DeleteSellerRoleAsync);

        // ═══════════════════════════════════════════════════════════════════════
        // LE JOURNAL D'ÉQUIPE — LA SEULE ROUTE QUE `AUDIT_VIEW` GARDE.
        //
        // `AUDIT_VIEW` FIGURAIT AU CATALOGUE SANS GARDER QUOI QUE CE SOIT.
        //
        // Elle était attribuée par défaut au FINANCE_MANAGER, affichée dans l'écran
        // des rôles, cochable — et ne donnait accès à rien. Le lot 0c a posé le
        // journal ; celle-ci est ce qui le rend lisible.
        //
        // SOUS `/members`, ET NON SOUS UN GROUPE `/audit` À ELLE.
        //
        // Ce qu'elle rend est l'activité de l'ÉQUIPE : le filtre est construit
        // depuis la liste des membres, et un geste de la plateforme sur ce dossier
        // n'y figure pas. Un groupe autonome laisserait croire à un journal
        // complet du vendeur, ce qu'il n'est pas — voir `ListAuditEntriesQuery`.
        // ═══════════════════════════════════════════════════════════════════════
        members.MapGet("/audit", ListAuditEntriesAsync);

        // Le catalogue des permissions ne dépend d'aucun vendeur. La contrainte
        // `:guid` de la route voisine empêche « permissions » d'être pris pour un
        // identifiant de vendeur.
        var permissions = app.MapSellerGroup("/api/v1/merchants/permissions")
            .WithTags("Merchant · Rôles");

        permissions.MapGet("/", ListPermissionsAsync);

        // ═══════════════════════════════════════════════════════════════════════
        // L'ACCEPTATION — LA SEULE ROUTE D'ÉQUIPE HORS DU GROUPE VENDEUR.
        //
        // L'INVITÉ N'A PAS ENCORE LE RÔLE `Seller`. C'EST TOUT LE PROBLÈME.
        //
        // `MapSellerGroup` filtre sur la claim de rôle du jeton, et ce rôle n'est
        // greffé qu'APRÈS l'entrée dans l'équipe (lot B′). Poser cette route dans
        // le groupe vendeur rendrait l'acceptation impossible : personne ne
        // pourrait jamais rejoindre une équipe.
        //
        // C'est exactement l'exception déjà consentie à l'inscription vendeur, et
        // pour la même raison. Elle n'ouvre rien : le jeton est le secret, et
        // l'adresse du compte doit correspondre à celle qui a été invitée.
        // ═══════════════════════════════════════════════════════════════════════
        var acceptation = app.MapAuthenticatedGroup("/api/v1/merchants/invitations")
            .WithTags("Merchant · Équipe");

        acceptation.MapPost("/accept", AcceptInvitationAsync);

        return app;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LE JETON DÉSIGNE UN UTILISATEUR, L'URL DÉSIGNE UN VENDEUR.
    //
    // Ce ne sont pas le même nombre, et rien ne les rapprochait : les handlers
    // recopiaient le sellerId de l'URL dans la commande sans jamais lire le
    // jeton. Changer un GUID — celui d'une fiche produit suffit — permettait donc
    // de renommer la boutique d'un concurrent, de réécrire ses informations
    // légales, et surtout de DÉTOURNER SES VIREMENTS en repointant son compte de
    // retrait vers un autre numéro Mobile Money.
    //
    // La correspondance appartient à ce service : `GetSellerByUserIdAsync` la
    // sert en interne, aucun appel réseau n'est nécessaire.
    //
    // 403 et non 404 : l'appelant connaît son propre sellerId, et l'existence
    // d'un vendeur n'a jamais été secrète — les boutiques sont publiques.
    // Répondre « introuvable » ne cacherait rien et rendrait tout diagnostic
    // impossible au vendeur légitime qui s'est trompé d'identifiant.
    //
    // L'administrateur traverse : agir sur le compte d'autrui est son métier.
    // ═════════════════════════════════════════════════════════════════════════
    //
    // ═════════════════════════════════════════════════════════════════════════
    // ELLE NE DEMANDE PLUS « ÊTES-VOUS LE VENDEUR » MAIS « QUE POUVEZ-VOUS »
    // (lot D1).
    //
    // `GetSellerByUserIdAsync` ne résout que les PROPRIÉTAIRES : elle lit la
    // colonne `UserId` du dossier. Les dix-huit routes gardées ici étaient donc
    // fermées à toute l'équipe — un responsable de boutique ne pouvait pas
    // changer les horaires de SA boutique, un gestionnaire KYB ne pouvait pas
    // déposer une pièce. Le §10 crée pourtant `STORE_UPDATE`, `STORE_OPEN_CLOSE`
    // et `KYB_MANAGE` exactement pour cela.
    //
    // `MemberAccessResolver` répond aux deux questions d'un coup : l'appelant
    // appartient-il à ce vendeur, et son rôle porte-t-il la permission. Le
    // propriétaire passe par le même chemin — la migration lui a créé une
    // appartenance portant le rôle OWNER, qui porte tout.
    //
    // POURQUOI LA GARDE RESTE ICI ET NON DANS LES HANDLERS.
    //
    // Les routes d'ÉQUIPE, elles, contrôlent dans la commande : leur décision
    // dépend des rôles du membre CIBLE autant que de ceux de l'acteur, ce qu'un
    // garde d'entrée ne peut pas voir. Ces dix-huit routes-ci ne comparent que
    // l'appelant à une permission fixe ; les descendre dans dix-huit handlers
    // multiplierait le même contrôle par dix-huit sans rien apprendre de plus.
    //
    // 403 DANS LES DEUX CAS, ET DEUX `reason` DIFFÉRENTS.
    //
    // L'appartenance manquante et la capacité manquante se répondent toutes deux
    // en 403 — l'existence d'un vendeur n'est pas un secret, les boutiques sont
    // publiques. Mais l'une se répare en changeant de compte et l'autre en
    // élargissant un rôle : c'est `error.details[reason]` qui les distingue, pas
    // le status.
    // ═════════════════════════════════════════════════════════════════════════
    /// <param name="storeId">
    /// La boutique visée, quand la route en nomme une.
    /// <para>
    /// CADRAGE RÉEL DEPUIS LE LOT F. Les neuf routes `/stores/{storeId}/…`
    /// portent la boutique dans leur gabarit : un responsable de la boutique A ne
    /// change plus les horaires de la B. Les routes de dossier — profil, KYB,
    /// clôture — passent `null`, parce qu'aucune boutique n'y a de sens.
    /// </para>
    /// </param>
    private static async Task<IResult?> DenyUnlessOwnSellerAsync(
        ClaimsPrincipal user, Guid sellerId, MemberAccessResolver acces,
        MerchantPermission capacite, CancellationToken ct, Guid? storeId = null)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        if (user.IsInRole(ApiAuthorization.AdminRole))
        {
            return null;
        }

        var acteur = await acces.ResolveAsync(sellerId, userId, ct);

        // `Results.Forbid()` RENDAIT UN 403 AU CORPS VIDE.
        //
        // Pas de `error.code`, et surtout pas de `meta.requestId` : c'est
        // précisément la réponse qu'un vendeur envoie en capture d'écran au support,
        // et la seule qu'on ne puisse relier à aucune trace. Le message reste vague
        // — dire « ce dossier appartient à quelqu'un d'autre » confirmerait son
        // existence à qui tâtonne.
        if (acteur.IsFailure)
        {
            return ApiResults.Failure(
                ErrorCodes.Forbidden,
                "Ce dossier vendeur n'est pas le vôtre.",
                StatusCodes.Status403Forbidden,
                [new ApiErrorDetail { Field = "reason", Message = acteur.Error.Code }]);
        }

        // `HasInStore` QUAND LA ROUTE NOMME UNE BOUTIQUE, `Has` SINON (lot F).
        //
        // `Has` répond sur l'union de tout ce que le membre porte : un responsable
        // de la boutique A pouvait donc fermer la boutique B du même vendeur.
        // Quand aucune boutique n'est nommée — profil, KYB, clôture — la question
        // n'a pas de périmètre, et `Has` reste la bonne réponse.
        var autorise = storeId is { } boutique
            ? acteur.Value.HasInStore(boutique, capacite)
            : acteur.Value.Has(capacite);

        if (!autorise)
        {
            return ApiResults.MissingCapability(capacite.ToCode());
        }

        // ═════════════════════════════════════════════════════════════════════
        // LE STEP-UP DU §37 — DEUX ROUTES DE CE GROUPE SONT CONCERNÉES.
        //
        // `PUT /payout-account` (PAYOUT_CONFIGURE) et `POST /close` (SELLER_CLOSE).
        // Repointer un compte de versement détourne les virements à venir ; fermer
        // le dossier coupe la boutique. Les deux sont exactement ce qu'on fait d'un
        // poste laissé ouvert au marché — la permission dit que le rôle a le droit,
        // pas que le titulaire est devant l'écran.
        //
        // ICI LE NIVEAU DE RISQUE EST LU DANS LE DOMAINE, PAS DANS LE CONTRAT.
        //
        // Les quatre autres services passent par `MerchantCapabilities.Critical`,
        // une recopie tenue par un test — ils n'ont pas accès au catalogue. Ce
        // service-ci L'A : s'en servir retire une recopie de plus, et fait de la
        // table du domaine la seule source du niveau de risque là où c'est possible.
        //
        // APRÈS LA CAPACITÉ, JAMAIS AVANT.
        //
        // Un membre qui n'a pas `PAYOUT_CONFIGURE` doit lire « votre rôle ne
        // l'autorise pas ». L'ordre inverse l'enverrait ressaisir son mot de passe
        // pour se voir refuser ensuite — deux écrans pour une seule mauvaise
        // nouvelle.
        //
        // ET L'ADMINISTRATION EST DÉJÀ SORTIE PLUS HAUT, DÉLIBÉRÉMENT.
        //
        // Un modérateur n'a pas d'appartenance ; le soumettre au step-up d'un rôle
        // vendeur n'aurait aucun sens. Sa propre traçabilité est ailleurs.
        // ═════════════════════════════════════════════════════════════════════
        if (MerchantPermissions.Critical.Contains(capacite) && !user.HasRecentAuthentication())
        {
            return ApiResults.ReauthenticationRequired(capacite.ToCode());
        }

        return null;
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA FILE D'ADMINISTRATION — PAGINÉE, FILTRABLE, ET ALLÉGÉE.
    ///
    /// ELLE RENDAIT TOUS LES VENDEURS AVEC TOUTES LEURS PIÈCES.
    ///
    /// Donc, en un appel : le numéro Mobile Money de chaque vendeur, son RCCM, son
    /// IFU, le téléphone de son gérant, et les références de ses pièces d'identité.
    /// Sans pagination, et sans filtre — pas même sur `KybStatus`, la seule chose
    /// qu'un modérateur cherche dans cette file.
    ///
    /// Elle rend désormais `SellerListItem` : qui, où en est le dossier, depuis
    /// quand. Le reste est à un clic, sur `GET /merchants/{id}`, que
    /// l'administrateur ouvre délibérément.
    ///
    /// `ApiResults.Page(resultat)` — LA SURCHARGE À UN ARGUMENT.
    ///
    /// Écrire `Page(p.Items, p.Page, p.PageSize, p.Total)` compile et JETTE
    /// SILENCIEUSEMENT les facettes : la console affiche alors « 0 en revue » sur
    /// une file qui en contient quarante, et l'on cherche la cause dans la requête.
    ///
    /// Le paramètre s'appelle `resultat` et non `page` : la méthode porte déjà un
    /// `page`, et le compilateur refuse l'ombre.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> ListSellersAsync(
        int? page,
        int? pageSize,
        string? search,
        string? kybStatus,
        string? status,
        ISender sender,
        CancellationToken ct)
        => (await sender.Send(
                new ListSellersQuery(page ?? 1, pageSize ?? 20, search, kybStatus, status), ct))
            .Match(resultat => ApiResults.Page(resultat));

    private static async Task<IResult> GetMySellerAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new GetSellerByUserQuery(userId), ct)).Match(seller => ApiResults.Ok(seller));

    private static async Task<IResult> RegisterSellerAsync(
        ClaimsPrincipal user, RegisterSellerRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new RegisterSellerCommand(
                userId, request.ShopName, request.CommissionRate ?? 0.10m, request.Metadata), ct))
                .Match(id => ApiResults.Created(new { id }, $"/api/v1/merchants/{id}"));

    /// <summary>
    /// Inscrit un vendeur POUR UN AUTRE COMPTE (Admin).
    ///
    /// Même commande, mêmes refus, que l'auto-inscription : seule l'origine de
    /// l'identifiant change. Voir l'encadré de la route.
    /// </summary>
    private static async Task<IResult> RegisterSellerForUserAsync(
        RegisterSellerForUserRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RegisterSellerCommand(
            request.UserId, request.ShopName, request.CommissionRate ?? 0.10m, request.Metadata), ct))
            .Match(id => ApiResults.Created(new { id }, $"/api/v1/merchants/{id}"));

    /// <remarks>
    /// CE RÉSUMÉ N'EST PAS UNE FICHE PUBLIQUE. Il porte le compte de retrait,
    /// le RCCM, l'IFU et le téléphone du gérant. La vitrine d'une boutique, elle,
    /// passe par Catalog. Sans garde, un acheteur lisait tout cela de n'importe
    /// quel vendeur, un GUID à la fois.
    ///
    /// LA FICHE PORTE DÉSORMAIS SES BOUTIQUES (§10.3).
    ///
    /// Le cahier les montre imbriquées ; le service ne les rendait pas, et le
    /// client enchaînait un second appel à `GET /{id}/stores` — deux allers-retours
    /// pour ouvrir un écran, sur une connexion mobile béninoise. `SellerDetail`
    /// hérite de `SellerSummary` : les champs déjà servis restent EXACTEMENT à leur
    /// place dans le JSON, `stores` s'ajoute à côté. Aucun client existant ne casse.
    ///
    /// La route `GET /{id}/stores` reste : elle sert la liste seule, et l'écran de
    /// gestion des boutiques n'a pas besoin du dossier KYB pour l'afficher.
    /// </remarks>
    private static async Task<IResult> GetSellerAsync(
        Guid sellerId, ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.SellerProfileView, ct)
            ?? (await sender.Send(new GetSellerDetailQuery(sellerId), ct)).Match(seller => ApiResults.Ok(seller));

    private static async Task<IResult> UpdateProfileAsync(
        Guid sellerId, UpdateProfileRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.SellerProfileUpdate, ct)
            ?? (await sender.Send(new UpdateSellerProfileCommand(
                sellerId, request.ShopName, request.LogoUrl, request.Description), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> UpdateMetadataAsync(
        Guid sellerId, UpdateMetadataRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.SellerProfileUpdate, ct)
            ?? (await sender.Send(new UpdateSellerMetadataCommand(sellerId, request.Metadata), ct))
                .Match(() => Results.NoContent());

    /// <remarks>
    /// LA ROUTE LA PLUS RENTABLE DU SERVICE POUR QUI LA TROUVAIT.
    ///
    /// Elle fixe le numéro Mobile Money vers lequel partent les gains du vendeur.
    /// Sans contrôle de propriété, un tiers y écrivait le sien et encaissait un
    /// chiffre d'affaires qu'il n'avait pas fait — la fraude n'apparaissant qu'au
    /// premier retrait manquant, donc des semaines plus tard.
    /// </remarks>
    private static async Task<IResult> SetPayoutAsync(
        Guid sellerId, SetPayoutRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.PayoutConfigure, ct)
            ?? (await sender.Send(new SetPayoutAccountCommand(
                sellerId, request.Provider, request.AccountNumber, request.AccountName), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> AddKybDocumentAsync(
        Guid sellerId, AddKybDocumentRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.KybManage, ct)
            ?? (await sender.Send(new AddKybDocumentCommand(
                sellerId, request.Type, request.MediaId,
                // Le déposant de la pièce doit appartenir à CE dossier vendeur —
                // voir le gestionnaire. Sans ce paramètre, le rattachement est
                // REFUSÉ, pas autorisé.
                RequestedByUserId: CurrentUserId(user) ?? Guid.Empty), ct))
                .Match(id => ApiResults.Created(new { id }, $"/api/v1/merchants/{sellerId}/kyb/documents/{id}"));

    /// <remarks>
    /// Le retrait d'une pièce déclenche l'effacement du fichier chez media-service :
    /// sans garde, on supprimait les justificatifs d'un dossier qui n'était pas le
    /// sien, et le vendeur repassait en revue sans comprendre pourquoi.
    /// </remarks>
    private static async Task<IResult> RemoveKybDocumentAsync(
        Guid sellerId, Guid documentId,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.KybManage, ct)
            ?? (await sender.Send(new RemoveKybDocumentCommand(sellerId, documentId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> SubmitKybAsync(
        Guid sellerId, ClaimsPrincipal user, MemberAccessResolver acces,
        ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.KybManage, ct)
            ?? (await sender.Send(new SubmitKybCommand(sellerId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> ApproveKybAsync(Guid sellerId, ISender sender, CancellationToken ct)
        => (await sender.Send(new ApproveKybCommand(sellerId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> RejectKybAsync(
        Guid sellerId, ReasonRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RejectKybCommand(sellerId, request.Reason), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ActivateSellerAsync(Guid sellerId, ISender sender, CancellationToken ct)
        => (await sender.Send(new ActivateSellerCommand(sellerId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> SuspendSellerAsync(
        Guid sellerId, ReasonRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new SuspendSellerCommand(sellerId, request.Reason), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> LiftSuspensionAsync(Guid sellerId, ISender sender, CancellationToken ct)
        => (await sender.Send(new LiftSellerSuspensionCommand(sellerId), ct)).Match(() => Results.NoContent());

    /// <remarks>
    /// « Demandée par le vendeur » ne l'était que dans le nom de la commande :
    /// la fermeture retire les offres de la vente, et tout inscrit pouvait la
    /// prononcer sur le compte d'un autre. C'était une suspension déguisée, sans
    /// motif ni trace, à la portée d'un concurrent.
    /// </remarks>
    private static async Task<IResult> RequestClosureAsync(
        Guid sellerId, ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.SellerClose, ct)
            ?? (await sender.Send(new RequestSellerClosureCommand(sellerId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> RequestReactivationAsync(
        Guid sellerId, ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.SellerReactivate, ct)
            ?? (await sender.Send(new RequestSellerReactivationCommand(sellerId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ApproveReactivationAsync(Guid sellerId, ISender sender, CancellationToken ct)
        => (await sender.Send(new ApproveSellerReactivationCommand(sellerId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> DeleteSellerAsync(Guid sellerId, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeleteSellerCommand(sellerId), ct)).Match(() => Results.NoContent());

    // ═════════════════════════════════════════════════════════════════════════
    // POURQUOI CHAQUE ROUTE BOUTIQUE REFAIT LE MÊME CONTRÔLE.
    //
    // `StoreCommandHandler.MutateAsync` compare bien `store.SellerId` au SellerId
    // de la commande — mais ce SellerId venait de l'URL, donc de l'appelant. Le
    // contrôle confrontait ainsi deux valeurs choisies par le même homme : il
    // suffisait d'envoyer le sellerId du VRAI propriétaire avec le storeId de sa
    // boutique pour que tout concorde, et de fermer le magasin d'un concurrent en
    // pleine journée de vente. Rien n'attachait cette paire au jeton.
    //
    // Prouver ici que le sellerId de l'URL est celui du porteur du jeton est ce
    // qui rend effectif le contrôle déjà écrit dans l'application.
    // ═════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> ListStoresAsync(
        Guid sellerId, ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreView, ct)
            ?? (await sender.Send(new ListSellerStoresQuery(sellerId), ct)).Match(stores => ApiResults.Ok(stores));

    private static async Task<IResult> CreateStoreAsync(
        Guid sellerId, CreateStoreRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreCreate, ct)
            ?? (await sender.Send(new CreateStoreCommand(
                sellerId, request.Name, request.ContactPhone, request.ContactEmail), ct))
                .Match(id => ApiResults.Created(new { id }, $"/api/v1/merchants/{sellerId}/stores/{id}"));

    /// <remarks>
    /// `GetStoreQuery` NE PREND QUE LE storeId — le sellerId de l'URL ne lui est
    /// jamais transmis. La propriété du vendeur une fois prouvée, il restait donc
    /// possible de lire la boutique d'un concurrent en gardant SON sellerId et en
    /// changeant le storeId, identifiant qui circule dans les liens publics. Le
    /// résumé rendu est le complet : contacts et MOTIF DE SUSPENSION inclus.
    ///
    /// « Introuvable » plutôt qu'« interdit », comme le fait déjà le domaine :
    /// distinguer les deux dirait à qui essaie des identifiants lesquels existent.
    /// </remarks>
    /// <remarks>
    /// LA SEULE ROUTE DU GROUPE À GARDER `ISellerModuleApi` EN PLUS DE L'ACTEUR.
    ///
    /// `GetStoreAsync` répond « à quel vendeur appartient cette boutique » —
    /// question de structure, que l'acteur ne porte pas : il décrit les droits de
    /// l'appelant, pas l'arborescence du dossier.
    /// </remarks>
    private static async Task<IResult> GetStoreAsync(
        Guid sellerId, Guid storeId,
        ClaimsPrincipal user, MemberAccessResolver acces, ISellerModuleApi sellers,
        ISender sender, CancellationToken ct)
    {
        if (await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreView, ct, storeId) is { } denied)
        {
            return denied;
        }

        var store = await sellers.GetStoreAsync(storeId, ct);

        if (store is null || store.SellerId != sellerId)
        {
            return ApiResults.NotFound(ServiceCodes.Seller);
        }

        return (await sender.Send(new GetStoreQuery(storeId), ct)).Match(found => ApiResults.Ok(found));
    }

    private static async Task<IResult> UpdateStoreProfileAsync(
        Guid sellerId, Guid storeId, StoreProfileRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreUpdate, ct, storeId)
            ?? (await sender.Send(new UpdateStoreProfileCommand(
                storeId, sellerId, request.Name, request.LogoUrl, request.Description), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> UpdateStoreContactAsync(
        Guid sellerId, Guid storeId, StoreContactRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreUpdate, ct, storeId)
            ?? (await sender.Send(new UpdateStoreContactCommand(
                storeId, sellerId, request.ContactPhone, request.ContactEmail), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> AttachStoreLocationAsync(
        Guid sellerId, Guid storeId, AttachLocationRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreUpdate, ct, storeId)
            ?? (await sender.Send(new AttachStoreLocationCommand(
                storeId, sellerId, request.FulfillmentLocationId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> SetOpeningHoursAsync(
        Guid sellerId, Guid storeId, SetOpeningHoursRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreUpdate, ct, storeId)
            ?? (await sender.Send(new SetStoreOpeningHoursCommand(storeId, sellerId, request.Hours), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> OpenStoreAsync(
        Guid sellerId, Guid storeId, ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreOpenClose, ct, storeId)
            ?? (await sender.Send(new OpenStoreCommand(storeId, sellerId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> CloseStoreAsync(
        Guid sellerId, Guid storeId, ReasonRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreOpenClose, ct, storeId)
            ?? (await sender.Send(new CloseStoreCommand(storeId, sellerId, request.Reason), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> SuspendStoreAsync(
        Guid storeId, ReasonRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new SuspendStoreCommand(storeId, request.Reason), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> LiftStoreSuspensionAsync(Guid storeId, ISender sender, CancellationToken ct)
        => (await sender.Send(new LiftStoreSuspensionCommand(storeId), ct)).Match(() => Results.NoContent());

    // ═════════════════════════════════════════════════════════════════════════
    // L'ÉQUIPE — TOUTES CES ROUTES PASSENT L'IDENTIFIANT DE L'APPELANT.
    //
    // AUCUNE NE VÉRIFIE QUOI QUE CE SOIT ICI, ET C'EST LE POINT.
    //
    // Le contrôle est dans la commande, parce qu'il ne se réduit pas à « est-ce
    // votre dossier » : il dépend des rôles du membre, qui vivent en base. Ce que
    // ces méthodes garantissent, c'est que l'identifiant transmis vient du JETON
    // et jamais du corps de la requête — la règle du §36.
    // ═════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> ListMembersAsync(
        Guid sellerId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new ListMembersQuery(sellerId, userId), ct)).Match(ApiResults.Ok);

    private static async Task<IResult> GetMemberAsync(
        Guid sellerId, Guid memberId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new GetMemberQuery(sellerId, userId, memberId), ct)).Match(ApiResults.Ok);

    private static async Task<IResult> ListInvitationsAsync(
        Guid sellerId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new ListInvitationsQuery(sellerId, userId), ct)).Match(ApiResults.Ok);

    private static async Task<IResult> ListSellerRolesAsync(
        Guid sellerId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new ListSellerRolesQuery(sellerId, userId), ct)).Match(ApiResults.Ok);

    private static async Task<IResult> CreateSellerRoleAsync(
        Guid sellerId, CreateSellerRoleRequest request,
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new CreateSellerRoleCommand(
                sellerId, userId, request.Name, request.Description,
                request.Scope, request.Permissions ?? []), ct))
                .Match(id => ApiResults.Created(new { id }, $"/api/v1/merchants/{sellerId}/roles/{id}"));

    private static async Task<IResult> UpdateSellerRoleAsync(
        Guid sellerId, Guid roleId, UpdateSellerRoleRequest request,
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new UpdateSellerRoleCommand(
                sellerId, userId, roleId, request.Name, request.Description,
                request.Permissions ?? []), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> DeleteSellerRoleAsync(
        Guid sellerId, Guid roleId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new DeleteSellerRoleCommand(sellerId, userId, roleId), ct))
                .Match(() => Results.NoContent());

    /// <summary>Le journal des gestes de l'équipe. Voir <c>ListAuditEntriesQuery</c>.</summary>
    /// <remarks>
    /// `memberUserId` EST UN COMPTE, PAS UN IDENTIFIANT DE MEMBRE.
    ///
    /// C'est ce que la table porte. Traduire ici ferait échouer silencieusement la
    /// recherche sur un membre révoqué — c'est-à-dire précisément celui qu'on
    /// cherche après un incident.
    /// </remarks>
    private static async Task<IResult> ListAuditEntriesAsync(
        Guid sellerId,
        Guid? memberUserId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int? page,
        int? pageSize,
        ClaimsPrincipal user,
        ISender sender,
        CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new ListAuditEntriesQuery(
                sellerId, userId, memberUserId, fromUtc, toUtc, page ?? 1, pageSize ?? 20), ct))
                .Match(resultat => ApiResults.Page(resultat));

    /// <summary>Quitter volontairement une équipe. Voir <c>LeaveSellerCommand</c>.</summary>
    private static async Task<IResult> LeaveSellerAsync(
        Guid sellerId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new LeaveSellerCommand(sellerId, userId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> ListPermissionsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPermissionsQuery(), ct)).Match(ApiResults.Ok);

    /// <summary>
    /// LA RÉPONSE CONTIENT LE JETON, ET C'EST LE SEUL MOMENT OÙ IL EXISTE.
    ///
    /// La base ne retient que son empreinte. Le rendre ici permet au propriétaire
    /// — déjà autorisé, puisqu'il vient de créer l'invitation — de transmettre le
    /// lien par ses propres moyens et de le retrouver si le courriel se perd.
    /// </summary>
    private static async Task<IResult> InviteMemberAsync(
        Guid sellerId, InviteMemberRequest request,
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new InviteMemberCommand(
                sellerId, userId, request.Email, request.DisplayName, request.JobTitle,
                request.RoleIds ?? [], request.Stores ?? []), ct))
                .Match(ApiResults.Ok);

    private static async Task<IResult> ResendInvitationAsync(
        Guid sellerId, Guid invitationId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new ResendInvitationCommand(sellerId, userId, invitationId), ct))
                .Match(ApiResults.Ok);

    private static async Task<IResult> RevokeInvitationAsync(
        Guid sellerId, Guid invitationId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new RevokeInvitationCommand(sellerId, userId, invitationId), ct))
                .Match(() => Results.NoContent());

    /// <summary>
    /// AUCUN `sellerId` DANS CETTE ROUTE. LE JETON DÉSIGNE TOUT.
    ///
    /// L'invitation porte le vendeur, et l'adresse est lue chez identity. Accepter
    /// un `sellerId` du corps reviendrait à accepter du client la preuve
    /// d'autorisation que le §36 interdit d'y chercher.
    /// </summary>
    private static async Task<IResult> AcceptInvitationAsync(
        AcceptInvitationRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new AcceptInvitationCommand(request.Token, userId), ct))
                .Match(id => ApiResults.Ok(new { memberId = id }));

    private static async Task<IResult> SetMemberRolesAsync(
        Guid sellerId, Guid memberId, MemberRolesRequest request,
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new SetMemberRolesCommand(
                sellerId, userId, memberId, request.RoleIds ?? []), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> AssignMemberStoreAsync(
        Guid sellerId, Guid memberId, Guid storeId, MemberRolesRequest request,
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new AssignMemberStoreCommand(
                sellerId, userId, memberId, storeId, request.RoleIds ?? []), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> UnassignMemberStoreAsync(
        Guid sellerId, Guid memberId, Guid storeId,
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new UnassignMemberStoreCommand(sellerId, userId, memberId, storeId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> SuspendMemberAsync(
        Guid sellerId, Guid memberId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new SuspendMemberCommand(sellerId, userId, memberId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> ReactivateMemberAsync(
        Guid sellerId, Guid memberId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new ReactivateMemberCommand(sellerId, userId, memberId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> RevokeMemberAsync(
        Guid sellerId, Guid memberId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new RevokeMemberCommand(sellerId, userId, memberId), ct))
                .Match(() => Results.NoContent());

    /// <summary>
    /// Transfère la propriété du dossier au membre désigné.
    /// </summary>
    /// <remarks>
    /// LE STEP-UP AVANT L'ENVOI, LA PERMISSION APRÈS — L'ORDRE INVERSE DE
    /// `DenyUnlessOwnSellerAsync`, ET C'EST ASSUMÉ.
    ///
    /// Là-bas, la permission est vérifiée d'abord pour qu'un membre non autorisé
    /// lise « votre rôle ne l'autorise pas » plutôt que d'aller ressaisir son mot
    /// de passe pour rien. Ici, la permission ne se lit qu'après avoir résolu
    /// l'acteur en base — c'est-à-dire dans le handler. Exiger la réauthentification
    /// avant coûte donc un écran de trop à un membre non propriétaire ; l'accepter
    /// laisserait le geste le plus irréversible du module se faire depuis une
    /// session ouverte le matin et laissée sans surveillance.
    ///
    /// Entre les deux, on protège le dossier.
    /// </remarks>
    private static async Task<IResult> TransferOwnershipAsync(
        Guid sellerId, Guid memberId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        if (!user.HasRecentAuthentication())
        {
            return ApiResults.ReauthenticationRequired(
                MerchantPermission.OwnershipTransfer.ToCode());
        }

        var resultat = await sender.Send(
            new TransferSellerOwnershipCommand(sellerId, userId, memberId), ct);

        return resultat.Match(() => Results.NoContent());
    }

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public sealed record RegisterSellerRequest(string ShopName, decimal? CommissionRate, SellerCompanyInfo? Metadata);

    /// <summary>
    /// Corps de `POST /api/v1/merchants/inscriptions` (Admin).
    ///
    /// LE `UserId` EST ICI CE QUE LE JETON EST AILLEURS. C'est la seule
    /// différence avec `RegisterSellerRequest`, et elle n'est acceptable que
    /// parce que la route vit dans le groupe d'administration.
    ///
    /// `CommissionRate` EST UNE FRACTION : 0.10 vaut dix pour cent. Envoyer 10
    /// poserait mille pour cent de commission, et la valeur passerait toutes les
    /// validations de type.
    /// </summary>
    public sealed record RegisterSellerForUserRequest(
        Guid UserId, string ShopName, decimal? CommissionRate, SellerCompanyInfo? Metadata);

    public sealed record UpdateProfileRequest(string ShopName, string? LogoUrl, string? Description);

    public sealed record UpdateMetadataRequest(SellerCompanyInfo? Metadata);

    public sealed record SetPayoutRequest(string Provider, string AccountNumber, string AccountName);

    public sealed record AddKybDocumentRequest(string Type, Guid MediaId);

    public sealed record ReasonRequest(string? Reason);

    public sealed record CreateStoreRequest(string Name, string ContactPhone, string? ContactEmail);

    public sealed record StoreProfileRequest(string Name, string? LogoUrl, string? Description);

    public sealed record StoreContactRequest(string ContactPhone, string? ContactEmail);

    public sealed record AttachLocationRequest(Guid FulfillmentLocationId);

    public sealed record SetOpeningHoursRequest(IReadOnlyList<OpeningHourInput> Hours);

    /// <summary>Corps de `POST /merchants/{sellerId}/roles`.</summary>
    /// <param name="Scope">
    /// `Seller` ou `Store`. Absent, c'est `Seller` — voir `LirePortee` : en phase 1
    /// un rôle de vocation boutique s'applique de toute façon au vendeur entier, et
    /// choisir `Store` par défaut ferait croire à un cadrage qui n'existe pas.
    /// </param>
    /// <param name="Permissions">
    /// Les CODES publics (`ORDER_CONFIRM`, `INVENTORY_ADJUST`…), tels que
    /// `GET /merchants/permissions` les rend. Jamais les valeurs numériques de
    /// l'énumération : elles bougent à chaque insertion.
    /// </param>
    public sealed record CreateSellerRoleRequest(
        string Name,
        string? Description,
        string? Scope,
        IReadOnlyList<string>? Permissions);

    /// <summary>
    /// Corps de `PATCH /merchants/{sellerId}/roles/{roleId}`.
    /// </summary>
    /// <remarks>
    /// PAS DE `Scope` : la vocation d'un rôle ne se modifie pas. La changer
    /// déplacerait le périmètre de tous les membres qui le portent déjà, sans
    /// qu'aucun d'eux ne soit touché ni notifié.
    /// </remarks>
    public sealed record UpdateSellerRoleRequest(
        string Name,
        string? Description,
        IReadOnlyList<string>? Permissions);

    public sealed record InviteMemberRequest(
        string Email,
        string? DisplayName,
        string? JobTitle,
        IReadOnlyList<Guid>? RoleIds,
        IReadOnlyList<StoreAssignmentInput>? Stores);

    public sealed record MemberRolesRequest(IReadOnlyList<Guid>? RoleIds);

    public sealed record AcceptInvitationRequest(string Token);
}
