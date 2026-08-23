using HBA.Deliveries.Contracts;
using HBA.Merchants.Contracts;
using HBA.Shared.Domain.Results;
using System.Security.Claims;
using HBA.Financial.Billing.Application.Commissions;
using HBA.Financial.Billing.Application.Invoices;
using HBA.Financial.Payments.Application.PaymentMethods;
using HBA.Financial.Payments.Application.Payments.Commands;
using HBA.Financial.Payments.Application.Payments.Commands.InitiatePayment;
using HBA.Financial.Payments.Application.Payments.Queries;
using HBA.Financial.Wallet.Application.Batches;
using HBA.Financial.Wallet.Application.Wallets;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Financial.Api.Endpoints;

/// <summary>Surface HTTP initiale du service Financial.</summary>
public static class FinancialEndpoints
{
    public static IEndpointRouteBuilder MapFinancialEndpoints(this IEndpointRouteBuilder app)
    {
        var payments = app.MapAuthenticatedGroup("/api/financial/payments").WithTags("Financial · Payments");
        payments.MapGet("/", ListPaymentsAsync).RequireAdmin();
        payments.MapGet("/stats", GetPaymentStatsAsync).RequireAdmin();
        payments.MapGet("/{id:guid}", GetPaymentAsync);
        payments.MapGet("/by-order/{orderId:guid}", GetPaymentByOrderAsync);
        payments.MapPost("/", InitiatePaymentAsync);

        // ═════════════════════════════════════════════════════════════════════
        // `capture` ET `fail` ÉTAIENT OUVERTES À TOUT COMPTE INSCRIT.
        //
        // `CapturePaymentCommandHandler` charge le paiement et appelle
        // `payment.Capture(...)` : aucune signature de prestataire, aucun
        // propriétaire, aucun rôle. Et la chaîne aval est bien branchée —
        // `PaymentCapturedDomainEvent` → `PaymentCapturedIntegrationEvent` →
        // `ConfirmOrderOnPaymentCapturedHandler`.
        //
        // Autrement dit : « confirmer une commande sans jamais l'avoir payée ».
        // C'est exactement le geste qu'`OrderEndpoints` documente avoir retiré de
        // chez lui pour cette raison ; il avait survécu de l'autre côté de la
        // frontière. Symétriquement, `fail` faisait échouer le paiement d'autrui.
        //
        // ADMIN, ET NON « PROPRIÉTAIRE DU PAIEMENT ».
        //
        // Ce ne sont pas des gestes d'ACHETEUR : le chemin nominal est le webhook
        // du prestataire, signé et vérifié quelques lignes plus bas. Ces deux
        // routes HTTP sont une trappe d'exploitation — même raisonnement que les
        // réservations de stock d'`InventoryEndpoints`. Les rendre à l'acheteur
        // propriétaire lui permettrait de déclarer payé ce qu'il n'a pas payé.
        // ═════════════════════════════════════════════════════════════════════
        payments.MapPost("/{id:guid}/capture", CapturePaymentAsync).RequireAdmin();
        payments.MapPost("/{id:guid}/fail", FailPaymentAsync).RequireAdmin();
        payments.MapPost("/{id:guid}/refund", RefundPaymentAsync).RequireAdmin();

        // CELLE-CI RESTE À L'ACHETEUR, ET ELLE EST GARDÉE DANS LE HANDLER.
        //
        // Le retour d'une page de paiement redirigée est un geste d'acheteur : la
        // fermer casserait le parcours. C'est la PROPRIÉTÉ du paiement qui décide.
        payments.MapPost("/{id:guid}/redirect/confirm", ConfirmPaymentFromRedirectAsync);
        // ═════════════════════════════════════════════════════════════════════
        // LE WEBHOOK DU PRESTATAIRE ÉTAIT DERRIÈRE L'AUTHENTIFICATION.
        //
        // Il était enregistré sur `payments`, un groupe `RequireAuthorization()`.
        // Or Stripe, MTN MoMo ou Moov n'ont pas de jeton JWT : leurs rappels étaient
        // donc TOUS refusés en 401. Concrètement, aucune confirmation de paiement
        // n'arrivait jamais par ce chemin — les commandes restaient en attente et il
        // fallait que le client rafraîchisse pour déclencher une reprise.
        //
        // La serrure de cette route n'est pas un jeton, c'est la SIGNATURE du
        // prestataire, vérifiée dans `ProcessGatewayWebhookCommand` à partir de
        // l'en-tête `X-Signature`. C'est la seule preuve qu'un tiers ne peut pas
        // fabriquer — un JWT, lui, n'aurait rien prouvé sur l'origine du rappel.
        // ═════════════════════════════════════════════════════════════════════
        payments.MapPost("/webhooks/{provider}", ProcessGatewayWebhookAsync).AllowAnonymous();

        // ═════════════════════════════════════════════════════════════════════
        // SURFACE DU §10.12, EN PARALLÈLE DE `/api/financial/payments`.
        //
        // Le cahier des charges décrit quatre routes sous `/api/v1/payments`. Elles
        // désignent exactement les mêmes gestes que les routes historiques : on
        // réutilise donc les MÊMES handlers plutôt que d'en dupliquer la logique —
        // deux implémentations du même paiement divergeraient au premier correctif
        // appliqué d'un seul côté.
        //
        // Ce qui change ici et pas là-bas : l'enveloppe du §5 en succès, et la clé
        // d'idempotence obligatoire sur la création d'intention et le remboursement.
        // Les anciennes routes restent en place le temps que les clients migrent
        // (D3 dans docs/DECISIONS.md).
        // ═════════════════════════════════════════════════════════════════════
        var v1 = app.MapAuthenticatedGroup("/api/v1/payments").WithTags("Payments · v1");

        v1.MapPost("/intents", CreatePaymentIntentAsync).WithName("CreatePaymentIntent").RequireIdempotency();
        v1.MapGet("/intents/{id:guid}", GetPaymentIntentAsync).WithName("GetPaymentIntent");
        v1.MapPost("/{id:guid}/refunds", CreateRefundAsync).WithName("CreateRefund").RequireAdmin().RequireIdempotency();
        v1.MapPost("/webhooks/{provider}", ProcessGatewayWebhookAsync).WithName("PaymentWebhookV1").AllowAnonymous();

        var methods = app.MapAuthenticatedGroup("/api/financial/payment-methods").WithTags("Financial · Payment Methods");
        methods.MapGet("/", ListPaymentMethodsAsync);
        methods.MapPost("/", AddPaymentMethodAsync);
        methods.MapPut("/{id:guid}", UpdatePaymentMethodAsync);
        methods.MapPost("/{id:guid}/default", SetDefaultPaymentMethodAsync);
        methods.MapDelete("/{id:guid}", DeletePaymentMethodAsync);

        var commissions = app.MapAuthenticatedGroup("/api/financial/commissions").WithTags("Financial · Commissions");

        // ═════════════════════════════════════════════════════════════════════
        // `.RequireAdmin()` AJOUTÉ — CETTE LISTE REND LES TAUX NÉGOCIÉS.
        //
        // Elle était la SEULE route de ce groupe sans garde, à côté de cinq
        // écritures qui en portent une. Or elle rend toutes les règles, y compris
        // celles de portée `Seller` — c'est-à-dire le taux consenti à chaque
        // vendeur, un par un.
        //
        // C'est exactement la donnée que l'encadré de `ComputeCommissionAsync`
        // décrit comme la fuite qu'il vient de refermer : « tout inscrit
        // calculait la commission d'un concurrent, catégorie par catégorie, en
        // faisant varier le montant — c'est-à-dire la donnée sur laquelle on
        // décide de casser un prix ». Le calcul a été fermé ; la liste, elle,
        // restait ouverte, et elle donne la même information sans même avoir à
        // la déduire.
        //
        // CE QUE CETTE GARDE FERME POUR LE VENDEUR, ET POURQUOI C'EST JUSTE.
        //
        // Un vendeur perd la lecture des règles de commission. Il garde
        // `/compute` sur SON dossier — simuler sa propre commission avant de
        // fixer un prix reste légitime, et `DenyUnlessOwnSellerAsync` l'y
        // autorise. Ce qu'il perd, c'est la grille des AUTRES.
        // ═════════════════════════════════════════════════════════════════════
        commissions.MapGet("/", ListCommissionRulesAsync).RequireAdmin();
        commissions.MapGet("/compute", ComputeCommissionAsync);
        commissions.MapPost("/", CreateCommissionRuleAsync).RequireAdmin();
        commissions.MapPut("/{id:guid}", UpdateCommissionRuleAsync).RequireAdmin();
        commissions.MapPost("/{id:guid}/deactivate", DeactivateCommissionRuleAsync).RequireAdmin();
        commissions.MapPost("/{id:guid}/reactivate", ReactivateCommissionRuleAsync).RequireAdmin();
        commissions.MapDelete("/{id:guid}", DeleteCommissionRuleAsync).RequireAdmin();

        // ═════════════════════════════════════════════════════════════════════
        // QUATRE ÉCRITURES DE FACTURE ÉTAIENT OUVERTES À TOUT COMPTE INSCRIT.
        //
        // `CreateInvoiceCommand` lie le `sellerId` DEPUIS LE CORPS — la violation
        // littérale du §36. Un acheteur inscrit en trente secondes prenait un
        // identifiant de vendeur sur une fiche boutique publique, postait une
        // facture à son nom, y ajoutait les lignes de son choix, l'émettait, puis
        // la marquait payée. Le vendeur découvrait dans son espace une facture
        // qu'il n'avait pas émise, marquée réglée, sur une période qu'il n'avait
        // pas choisie.
        //
        // ADMINISTRATION, ET NON « LE VENDEUR CONCERNÉ ».
        //
        // Une facture de commission est un acte de la PLATEFORME envers le
        // vendeur : c'est elle qui facture, lui qui reçoit. La rendre au vendeur
        // reviendrait à le laisser établir ce qu'il doit. Il la LIT — c'est
        // `GET /seller/{sellerId}`, gardé par `FINANCE_VIEW` — il ne l'écrit pas.
        // ═════════════════════════════════════════════════════════════════════
        var invoices = app.MapAuthenticatedGroup("/api/financial/invoices").WithTags("Financial · Invoices");

        // ═════════════════════════════════════════════════════════════════════
        // LA LISTE PLATEFORME MANQUAIT, ET AUCUN ÉCRAN NE POUVAIT EXISTER.
        //
        // `GET /{id}` exige une facture, `GET /seller/{id}` exige un vendeur :
        // rien ne répondait à « quelles factures sont émises et impayées ce
        // mois-ci », qui est la seule question qu'on pose à ce module.
        //
        // `.RequireAdmin()` DÈS LA PREMIÈRE LIGNE, ET AVANT LA ROUTE DE
        //    PASSERELLE.
        //
        // Elle rend le chiffre d'affaires commissionné vendeur par vendeur.
        // Ouvrir le relais avant de poser la garde l'aurait exposé à tout compte
        // authentifié le temps d'un déploiement — et c'est le défaut que ce
        // service vient de refermer sur ses lectures voisines.
        // ═════════════════════════════════════════════════════════════════════
        invoices.MapGet("/", ListInvoicesAsync).RequireAdmin();

        invoices.MapGet("/{id:guid}", GetInvoiceAsync);
        invoices.MapGet("/seller/{sellerId:guid}", ListInvoicesBySellerAsync);
        invoices.MapPost("/", CreateInvoiceAsync).RequireAdmin();
        invoices.MapPost("/{id:guid}/lines", AddInvoiceLineAsync).RequireAdmin();
        invoices.MapPost("/{id:guid}/issue", IssueInvoiceAsync).RequireAdmin();
        invoices.MapPost("/{id:guid}/paid", MarkInvoicePaidAsync).RequireAdmin();

        var wallet = app.MapAuthenticatedGroup("/api/financial/wallets").WithTags("Financial · Wallets");
        wallet.MapGet("/sellers/{sellerId:guid}", GetSellerWalletAsync);
        wallet.MapGet("/sellers/{sellerId:guid}/transactions", ListSellerWalletTransactionsAsync);
        wallet.MapGet("/sellers/{sellerId:guid}/withdrawals", ListWithdrawalsAsync);
        wallet.MapPost("/sellers/{sellerId:guid}/withdrawals", RequestWithdrawalAsync);
        wallet.MapGet("/drivers/{driverId:guid}", GetDriverWalletAsync);
        wallet.MapGet("/drivers/{driverId:guid}/transactions", ListDriverWalletTransactionsAsync);
        wallet.MapGet("/platform", GetPlatformWalletAsync).RequireAdmin();
        wallet.MapGet("/platform/transactions", ListPlatformWalletTransactionsAsync).RequireAdmin();
        wallet.MapGet("/withdrawals/pending", ListPendingWithdrawalsAsync).RequireAdmin();
        wallet.MapGet("/withdrawals/processing", ListProcessingWithdrawalsAsync).RequireAdmin();
        wallet.MapPost("/withdrawals/{id:guid}/approve", ApproveWithdrawalAsync).RequireAdmin();
        wallet.MapPost("/withdrawals/{id:guid}/reject", RejectWithdrawalAsync).RequireAdmin();

        // ═════════════════════════════════════════════════════════════════════
        // LE PORTEFEUILLE CLIENT (D33 dans docs/DECISIONS.md).
        //
        // FedaPay n'expose aucune API de remboursement : l'argent revient au client
        // sur SON portefeuille, et le virement Mobile Money est une demande
        // distincte, exécutée et marquée payée à la main par un administrateur.
        //
        // `/me` PARTOUT, ET JAMAIS D'IDENTIFIANT DE CLIENT DANS L'URL NI DANS LE
        // CORPS.
        //
        // C'est exactement la faille ISSUE-017/018 corrigée à la vague 1 : une route
        // financière qui accepte l'identifiant de son propriétaire en paramètre est
        // une route que n'importe quel compte authentifié peut viser en devinant un
        // GUID. Les routes vendeur et livreur ci-dessus portent cet identifiant
        // parce qu'un vendeur n'est pas un utilisateur — il faut une garde
        // d'appartenance explicite (`DenyUnlessOwnSellerAsync`) pour faire le lien.
        //
        // Ici, le propriétaire du portefeuille EST l'utilisateur du jeton. Il n'y a
        // donc aucun lien à vérifier, et surtout aucune raison d'exposer une surface
        // où il faudrait le vérifier : l'identité vient de `CurrentUserId`, point.
        // Un administrateur qui a besoin de voir le portefeuille d'un client passera
        // par une route d'administration dédiée — elle n'existe pas encore, et
        // l'ajouter sera une décision, pas un effet de bord.
        // ═════════════════════════════════════════════════════════════════════
        // SOUS `/api/financial/wallets`, ET SURTOUT PAS SOUS UN PRÉFIXE NEUF.
        //
        // La passerelle ne relaie que ce qu'elle connaît : `/api/wallet/{**}` est
        // réécrit vers `/api/financial/wallets/{**}`, et rien d'autre ne mène à ce
        // service. Un groupe `/api/v1/wallet` aurait répondu depuis le conteneur et
        // rendu 404 depuis un téléphone — sans la moindre erreur de configuration
        // pour l'expliquer, puisque le cluster et la destination sont corrects.
        // C'est le défaut que la note « Six routes sur vingt-quatre » décrit dans
        // `apps/api-gateway/.../appsettings.json`, et il ne se voit qu'à l'usage.
        //
        // Le chemin public est donc `/api/wallet/me`.
        wallet.MapGet("/me", GetMyWalletAsync).WithName("GetMyWallet");
        wallet.MapGet("/me/transactions", ListMyWalletTransactionsAsync).WithName("ListMyWalletTransactions");
        wallet.MapGet("/me/withdrawals", ListMyWithdrawalsAsync).WithName("ListMyCustomerWithdrawals");

        // `.RequireIdempotency()` — CETTE ROUTE RETIENT LES FONDS DU CLIENT.
        //
        // Un double-clic ou un réessai réseau retiendrait DEUX fois le solde et
        // poserait deux demandes identiques dans la file d'administration, que rien
        // ne permettrait de distinguer — et que l'administrateur paierait
        // probablement toutes les deux. Le §5 rend l'en-tête obligatoire sur les POST
        // de création ; celui-ci en est un, et il déplace de l'argent.
        wallet.MapPost("/me/withdrawals", RequestMyWithdrawalAsync)
            .WithName("RequestCustomerWithdrawal")
            .RequireIdempotency();

        // ═════════════════════════════════════════════════════════════════════
        // LA FILE D'ADMINISTRATION — C'EST ELLE QUI PAIE, IL N'Y A RIEN D'AUTRE.
        //
        // Aucun mécanisme n'exécute ces virements : c'est la décision D33, et c'est
        // le même point de contrôle des sorties d'argent que sur les retraits
        // vendeur. Une demande qui ne s'affiche pas ici est un client dont les fonds
        // sont retenus et que personne ne verra jamais.
        // ═════════════════════════════════════════════════════════════════════
        // Même groupe, même raison qu'au-dessus : la file d'administration voisine
        // celle des retraits vendeur (`/withdrawals/pending`), et elle est atteinte
        // par le même chemin public.
        wallet.MapGet("/customer-withdrawals/pending", ListCustomerWithdrawalQueueAsync)
            .WithName("ListCustomerWithdrawalQueue").RequireAdmin();
        wallet.MapPost("/customer-withdrawals/{id:guid}/paid", MarkCustomerWithdrawalPaidAsync)
            .WithName("MarkCustomerWithdrawalPaid").RequireAdmin();
        wallet.MapPost("/customer-withdrawals/{id:guid}/reject", RejectCustomerWithdrawalAsync)
            .WithName("RejectCustomerWithdrawal").RequireAdmin();

        var settlements = app.MapAuthenticatedGroup("/api/financial/settlements").WithTags("Financial · Settlements");
        settlements.MapGet("/", ListSettlementBatchesAsync).RequireAdmin();
        settlements.MapGet("/{id:guid}", GetSettlementBatchAsync).RequireAdmin();
        // TROIS LECTURES FINANCIÈRES SANS CONTRÔLE D'APPARTENANCE, et le
        // métacommentaire de la passerelle affirmait le contraire : il nommait une
        // méthode de vérification qui n'a jamais été écrite. Un commentaire qui
        // certifie une garde absente est pire qu'un silence — il fait passer la
        // relecture.
        //
        // ON NE RÉÉCRIT PAS ICI LE NOM DE CETTE MÉTHODE FANTÔME, ET C'EST
        // DÉLIBÉRÉ. `scripts/check-config-and-guards.py` refuse qu'un nom en
        // `Ensure*Async`/`Deny*Async` soit cité sans exister — y compris pour le
        // dénoncer. Le citer laisserait deux occurrences à quiconque cherche la
        // méthode, et lui ferait croire qu'elle existe quelque part.
        //
        // Ce qui fuyait : chiffre d'affaires brut, commissions et net d'un
        // concurrent, ligne par ligne, avec ses identifiants de commande — à qui
        // connaît un `sellerId`, que la vitrine publique rend.
        settlements.MapGet("/sellers/{sellerId:guid}/statement", GetSellerStatementAsync);
        settlements.MapGet("/sellers/{sellerId:guid}/statement/lines", GetSellerStatementLinesAsync);
        settlements.MapGet("/sellers/{sellerId:guid}/payouts", ListSellerPayoutsAsync);
        // ═════════════════════════════════════════════════════════════════════
        // CES TROIS ÉCRITURES ÉTAIENT DANS LE GROUPE AUTHENTIFIÉ. CE N'EST PAS
        //    UNE FUITE DE LECTURE : C'EST LE POUVOIR DE DÉPLACER DE L'ARGENT.
        //
        // N'importe quel compte — un acheteur inscrit en trente secondes —
        // pouvait :
        //
        //   • LANCER un lot de règlement sur la période de son choix, donc
        //     déclencher les versements de TOUS les vendeurs de la plateforme ;
        //   • MARQUER un versement comme PAYÉ sans qu'aucun franc ne soit parti —
        //     le vendeur est alors débité de son solde et n'a rien reçu, SANS retour
        //     possible : déclarer ensuite ce versement échoué est refusé, puisque
        //     du point de vue du système l'argent est parti (ISSUE-015) ;
        //   • ANNULER un lot en cours.
        //
        // Aucune de ces trois n'a de sens pour un vendeur, même sur ses propres
        // données : un règlement est un geste d'exploitation, décidé par la
        // plateforme sur une période. Elles rejoignent donc `MapAdminGroup`.
        //
        // La QUATRIÈME route du groupe — déclarer un versement échoué — n'a jamais
        // été ailleurs : elle est née ici (ISSUE-015). Elle RECRÉDITE le vendeur et
        // rend ses gains payables, soit le geste inverse de `.../paid`, et donc
        // exactement aussi sensible.
        //
        // LE GROUPE DE LECTURE RESTE AUTHENTIFIÉ : un vendeur doit voir son
        // relevé. C'est l'appartenance qui manque, et elle est traitée juste
        // au-dessus, sur les trois routes `/sellers/{sellerId}/…`.
        // ═════════════════════════════════════════════════════════════════════
        var settlementAdmin = app.MapAdminGroup("/api/financial/settlements")
            .WithTags("Admin · Financial · Settlements");
        settlementAdmin.MapPost("/", RunSettlementAsync);
        settlementAdmin.MapPost("/{batchId:guid}/payouts/{payoutId:guid}/paid", MarkPayoutPaidAsync);

        // LA COMPENSATION D'UN VIREMENT REFUSÉ (ISSUE-015).
        //
        // `SettlementBatch.MarkPayoutFailed` existait sans aucun appelant : un
        // virement refusé par l'opérateur ne recréditait rien, le vendeur restait
        // débité et jamais payé. Cette route est la moitié manquante de
        // `.../paid`, et elle vit dans le MÊME groupe admin — elle déplace de
        // l'argent dans l'autre sens.
        //
        // COMME `.../paid`, ELLE N'EST PAS RELAYÉE PAR LA PASSERELLE : la route
        // `settlements` d'`api-gateway` est GET seulement, délibérément. Elle n'est
        // donc atteignable que depuis le réseau interne.
        settlementAdmin.MapPost("/{batchId:guid}/payouts/{payoutId:guid}/failed", MarkPayoutFailedAsync);
        settlementAdmin.MapPost("/{id:guid}/cancel", CancelSettlementBatchAsync);

        return app;
    }

    private static async Task<IResult> ListPaymentsAsync(int page, int pageSize, string? search, string? status, string? sort, string? dir, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPaymentsQuery(page, pageSize, search, status, sort, dir), ct)).Match(Results.Ok);

    private static async Task<IResult> GetPaymentStatsAsync(string? search, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetPaymentStatsQuery(search), ct)).Match(Results.Ok);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CES DEUX LECTURES RENDAIENT LE PAIEMENT DE N'IMPORTE QUI.
    ///
    /// `PaymentSummary` porte le montant, le prestataire, la référence externe et
    /// le statut. Les identifiants de commande circulent — l'acheteur les voit
    /// dans ses propres URL, et le vendeur dans son carnet. Tout inscrit lisait
    /// donc le détail de règlement d'un tiers en changeant un GUID.
    ///
    /// 404 ET NON 403 : un paiement n'est pas une ressource publique, et
    /// confirmer son existence sur un identifiant deviné est déjà une fuite.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> GetPaymentAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var resultat = await sender.Send(new GetPaymentQuery(id), ct);

        return resultat.IsSuccess && !PeutVoirLePaiement(user, resultat.Value.BuyerId)
            ? ApiResults.NotFound(ServiceCodes.Payment)
            : resultat.Match(Results.Ok);
    }

    private static async Task<IResult> GetPaymentByOrderAsync(
        Guid orderId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var resultat = await sender.Send(new GetPaymentByOrderQuery(orderId), ct);

        return resultat.IsSuccess && !PeutVoirLePaiement(user, resultat.Value.BuyerId)
            ? ApiResults.NotFound(ServiceCodes.Payment)
            : resultat.Match(Results.Ok);
    }

    /// <summary>L'appelant est-il l'acheteur, ou l'administration ?</summary>
    /// <remarks>
    /// LE VENDEUR N'EST PAS DANS CETTE LISTE, ET C'EST DÉLIBÉRÉ.
    ///
    /// `Payment` ne porte pas de `SellerId` — seulement `OrderId` et `BuyerId`. Y
    /// donner accès au vendeur exigerait de remonter la commande, donc un appel
    /// inter-services dans une garde de lecture. Le vendeur a de toute façon son
    /// relevé et son portefeuille, qui lui disent ce qu'il a encaissé sans lui
    /// donner les références de règlement de l'acheteur.
    /// </remarks>
    private static bool PeutVoirLePaiement(ClaimsPrincipal user, Guid buyerId)
        => user.IsInRole("Admin") || user.IsInRole("Moderator") || CurrentUserId(user) == buyerId;

    /// <summary>
    /// L'IDENTITÉ EST ÉCRASÉE, PAS LUE.
    ///
    /// `RequestedByUserId` appartient à un objet lié depuis le CORPS : un client
    /// peut l'envoyer. On le remplace donc systématiquement par l'identité du
    /// jeton avant d'envoyer la commande. C'est le §36 : un identifiant venu du
    /// client ne prouve rien.
    /// </summary>
    private static async Task<IResult> InitiatePaymentAsync(
        InitiatePaymentCommand command, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => (await sender.Send(command with { RequestedByUserId = CurrentUserId(user) }, ct))
            .Match(result => Results.Created($"/api/financial/payments/{result.PaymentId}", result));

    private static async Task<IResult> CapturePaymentAsync(Guid id, ProviderReferenceRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CapturePaymentCommand(id, request.ProviderReference), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> FailPaymentAsync(Guid id, ReasonRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new FailPaymentCommand(id, request.Reason), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> RefundPaymentAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new RefundPaymentCommand(id), ct)).Match(_ => Results.NoContent());

    /// <summary>
    /// GARDÉE PAR LA PROPRIÉTÉ DU PAIEMENT.
    ///
    /// Sans ce contrôle, elle valait `capture` pour qui devine un identifiant : la
    /// confirmation de redirection fait avancer le paiement exactement de la même
    /// façon. C'est le seul geste du cycle de vie qui reste à l'acheteur, et il ne
    /// lui reste que sur SON paiement.
    /// </summary>
    private static async Task<IResult> ConfirmPaymentFromRedirectAsync(Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var paiement = await sender.Send(new GetPaymentQuery(id), ct);

        if (paiement.IsFailure || !PeutVoirLePaiement(user, paiement.Value.BuyerId))
        {
            return ApiResults.NotFound(ServiceCodes.Payment);
        }

        return (await sender.Send(new ConfirmPaymentFromRedirectCommand(id), ct)).Match(() => Results.NoContent());
    }

    private static async Task<IResult> ProcessGatewayWebhookAsync(string provider, HttpRequest request, ISender sender, CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync(ct);
        
        return (await sender.Send(new ProcessGatewayWebhookCommand(provider, body, request.Headers["X-Signature"].FirstOrDefault()), ct))
            .Match(() => Results.Accepted());
    }

    private static async Task<IResult> ListPaymentMethodsAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListPaymentMethodsQuery(userId), ct)).Match(Results.Ok);

    private static async Task<IResult> AddPaymentMethodAsync(ClaimsPrincipal user, AddPaymentMethodRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new AddPaymentMethodCommand(
                userId,
                request.Type,
                request.Label,
                request.Provider,
                request.Msisdn,
                request.CardNumber,
                request.ExpiryMonth,
                request.ExpiryYear,
                request.HolderName,
                request.MakeDefault), ct))
                .Match(id => Results.Created($"/api/financial/payment-methods/{id}", new { id }));

    private static async Task<IResult> UpdatePaymentMethodAsync(Guid id, ClaimsPrincipal user, UpdatePaymentMethodRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new UpdatePaymentMethodCommand(
                userId,
                id,
                request.Label,
                request.Provider,
                request.Msisdn,
                request.ExpiryMonth,
                request.ExpiryYear,
                request.HolderName,
                request.MakeDefault), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> SetDefaultPaymentMethodAsync(Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new SetDefaultPaymentMethodCommand(userId, id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> DeletePaymentMethodAsync(Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new DeletePaymentMethodCommand(userId, id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ListCommissionRulesAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListCommissionRulesQuery(), ct)).Match(Results.Ok);

    /// <summary>
    /// ELLE RENDAIT LE TAUX NÉGOCIÉ DE N'IMPORTE QUEL VENDEUR.
    ///
    /// Tout inscrit calculait la commission d'un concurrent, catégorie par
    /// catégorie, en faisant varier le montant — c'est-à-dire la donnée sur
    /// laquelle on décide de casser un prix. Le `sellerId` venait de la requête et
    /// rien ne le confrontait au jeton.
    ///
    /// Elle reste ouverte au vendeur SUR SON PROPRE DOSSIER : simuler sa commission
    /// avant de fixer un prix est un usage légitime, et `FINANCE_VIEW` l'autorise.
    /// </summary>
    private static async Task<IResult> ComputeCommissionAsync(
        Guid sellerId, Guid categoryId, decimal grossAmount, string currency,
        ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(sellerId, user, access, MerchantCapabilities.FinanceView, ct)
        ?? (await sender.Send(new ComputeCommissionQuery(sellerId, categoryId, grossAmount, currency), ct))
            .Match(Results.Ok);

    private static async Task<IResult> CreateCommissionRuleAsync(CreateCommissionRuleCommand command, ISender sender, CancellationToken ct)
        => (await sender.Send(command, ct)).Match(id => Results.Created($"/api/financial/commissions/{id}", new { id }));

    private static async Task<IResult> UpdateCommissionRuleAsync(Guid id, UpdateCommissionRuleRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpdateCommissionRuleCommand(id, request.Rate, request.FixedFee, request.Currency, request.MinFee, request.MaxFee, request.EffectiveFromUtc), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> DeactivateCommissionRuleAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeactivateCommissionRuleCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ReactivateCommissionRuleAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ReactivateCommissionRuleCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> DeleteCommissionRuleAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeleteCommissionRuleCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>
    /// ELLE RENDAIT LA FACTURE DE N'IMPORTE QUEL VENDEUR.
    ///
    /// C'était la dernière des lectures financières non gardées : montant, période,
    /// statut, c'est-à-dire le chiffre d'affaires commissionné d'un concurrent, à
    /// qui devine un GUID. Les trois voisines avaient été refermées ; celle-ci
    /// était restée parce qu'elle prend un identifiant de FACTURE et non de
    /// vendeur — le `sellerId` n'apparaît que dans la réponse.
    ///
    /// D'où la forme de la garde : on exécute la requête, puis on confronte le
    /// `SellerId` du résultat à l'appelant. Le 404 est rendu APRÈS lecture, ce qui
    /// est sans conséquence — rien n'est muté, et l'alternative serait un second
    /// aller-retour pour la même information.
    /// </summary>
    private static async Task<IResult> GetInvoiceAsync(Guid id, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
    {
        var facture = await sender.Send(new GetInvoiceQuery(id), ct);
        if (facture.IsFailure)
        {
            return facture.Match(Results.Ok);
        }

        var refus = await DenyUnlessOwnSellerAsync(facture.Value.SellerId, user, access, MerchantCapabilities.FinanceView, ct);

        return refus ?? facture.Match(Results.Ok);
    }

    /// <summary>
    /// LA DERNIÈRE DES « TROIS LECTURES FINANCIÈRES SANS CONTRÔLE D'APPARTENANCE »
    /// que le commentaire du fichier signalait, et la seule qui restait.
    ///
    /// Les factures d'un vendeur portent son chiffre d'affaires ligne à ligne. Sans
    /// ce garde, il suffisait d'un identifiant de vendeur — visible dans n'importe
    /// quelle fiche boutique — pour lire le carnet de commandes d'un concurrent.
    /// </summary>
    // ───────────────────────────────────────────────────────── §10.12 (v1)

    /// <summary>
    /// `POST /api/v1/payments/intents`. Rend 201 avec l'enveloppe du §5.
    ///
    /// L'`Idempotency-Key` est obligatoire ici : c'est la route qui crée une
    /// intention de paiement, et une reprise réseau sans clé en crée une seconde —
    /// donc un second débit pour la même commande.
    /// </summary>
    /// <summary>Même garde que `InitiatePaymentAsync` : l'appelant est imposé, jamais lu.</summary>
    private static async Task<IResult> CreatePaymentIntentAsync(
        InitiatePaymentCommand command, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => (await sender.Send(command with { RequestedByUserId = CurrentUserId(user) }, ct))
            .Match(intent => ApiResults.Created(intent, $"/api/v1/payments/intents/{intent.PaymentId}"));

    /// <summary>
    /// `GET /api/v1/payments/intents/{id}` — état d'un paiement (§10.12).
    ///
    /// CETTE ROUTE N'AVAIT PAS LA GARDE DE SA JUMELLE.
    ///
    /// `GetPaymentAsync`, quelques lignes plus haut, applique `PeutVoirLePaiement`
    /// depuis la correction de la fuite qu'elle décrit longuement. Cette route-ci,
    /// ajoutée ensuite pour la surface versionnée, rendait le MÊME
    /// `PaymentSummary` — montant, prestataire, référence externe, statut — à tout
    /// compte inscrit qui changeait un GUID. La garde avait été écrite une fois et
    /// pas recopiée : c'est le mode d'échec habituel des routes jumelles.
    ///
    /// 404 ET NON 403, pour la même raison qu'à côté : confirmer l'existence
    /// d'un paiement sur un identifiant deviné est déjà une fuite.
    /// </summary>
    private static async Task<IResult> GetPaymentIntentAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var resultat = await sender.Send(new GetPaymentQuery(id), ct);

        return resultat.IsSuccess && !PeutVoirLePaiement(user, resultat.Value.BuyerId)
            ? ApiResults.NotFound(ServiceCodes.Payment)
            : resultat.Match(payment => ApiResults.Ok(payment));
    }

    /// <summary>
    /// `POST /api/v1/payments/{id}/refunds` — 202 Accepted (§10.12).
    ///
    /// 202 et non 200 : le remboursement part chez le prestataire et n'est pas
    /// acquis au retour de l'appel. Rendre 200 laisserait croire que l'argent est
    /// reparti, alors que seul l'ordre a été accepté.
    /// </summary>
    private static async Task<IResult> CreateRefundAsync(
        Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new RefundPaymentCommand(id), ct))
            .Match(refund => ApiResults.Accepted(new { paymentId = id, refundId = refund.RefundId, status = refund.Status }));

    /// <summary>Page de factures, tous vendeurs confondus (Admin).</summary>
    /// <remarks>
    /// TOUS LES PARAMÈTRES SONT NULLABLES : UN APPEL NU REND LA PREMIÈRE PAGE.
    ///
    /// `sellerId` est un FILTRE, pas une garde : cette route est admin, et le
    /// vendeur qui veut ses propres factures passe par `/seller/{sellerId}`, où
    /// une garde d'appartenance existe. Deux routes, deux régimes — les fondre
    /// donnerait une seule surface à deux autorisations, et c'est ainsi qu'on
    /// ouvre une fuite sans s'en apercevoir.
    /// </remarks>
    private static async Task<IResult> ListInvoicesAsync(
        int? page, int? pageSize, string? status, Guid? sellerId,
        ISender sender, CancellationToken ct)
    {
        var demande = new ListInvoicesQuery(Page: page ?? 1, Status: status, SellerId: sellerId);

        var resultat = await sender.Send(
            pageSize is { } taille ? demande with { PageSize = taille } : demande, ct);

        return resultat.Match(donnees => ApiResults.Page(donnees));
    }

    private static async Task<IResult> ListInvoicesBySellerAsync(
        Guid sellerId, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
    {
        if (await DenyUnlessOwnSellerAsync(sellerId, user, access, MerchantCapabilities.FinanceView, ct) is { } refus)
        {
            return refus;
        }

        return (await sender.Send(new ListInvoicesBySellerQuery(sellerId), ct)).Match(Results.Ok);
    }

    private static async Task<IResult> CreateInvoiceAsync(CreateInvoiceCommand command, ISender sender, CancellationToken ct)
        => (await sender.Send(command, ct)).Match(id => Results.Created($"/api/financial/invoices/{id}", new { id }));

    private static async Task<IResult> AddInvoiceLineAsync(Guid id, InvoiceLineRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new AddInvoiceLineCommand(id, request.Description, request.Amount), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> IssueInvoiceAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new IssueInvoiceCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> MarkInvoicePaidAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new MarkInvoicePaidCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> GetSellerWalletAsync(Guid sellerId, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
    {
        // Garde d'appartenance — voir DenyUnlessOwnSellerAsync. Il existait et
        // n'était appelé nulle part : n'importe quel compte authentifié lisait le
        // portefeuille, le relevé et les retraits de n'importe quel vendeur.
        if (await DenyUnlessOwnSellerAsync(sellerId, user, access, MerchantCapabilities.WalletView, ct) is { } refus)
        {
            return refus;
        }

        return (await sender.Send(new GetSellerWalletQuery(sellerId), ct)).Match(Results.Ok);
    }

    private static async Task<IResult> ListSellerWalletTransactionsAsync(Guid sellerId, int take, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
    {
        // Garde d'appartenance — voir DenyUnlessOwnSellerAsync. Il existait et
        // n'était appelé nulle part : n'importe quel compte authentifié lisait le
        // portefeuille, le relevé et les retraits de n'importe quel vendeur.
        if (await DenyUnlessOwnSellerAsync(sellerId, user, access, MerchantCapabilities.WalletView, ct) is { } refus)
        {
            return refus;
        }

        return (await sender.Send(new ListSellerWalletTransactionsQuery(sellerId, take), ct)).Match(Results.Ok);
    }

    private static async Task<IResult> ListWithdrawalsAsync(Guid sellerId, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
    {
        // Garde d'appartenance — voir DenyUnlessOwnSellerAsync. Il existait et
        // n'était appelé nulle part : n'importe quel compte authentifié lisait le
        // portefeuille, le relevé et les retraits de n'importe quel vendeur.
        if (await DenyUnlessOwnSellerAsync(sellerId, user, access, MerchantCapabilities.PayoutView, ct) is { } refus)
        {
            return refus;
        }

        return (await sender.Send(new ListWithdrawalsQuery(sellerId), ct)).Match(Results.Ok);
    }

    /// <summary>
    /// LA ROUTE LA PLUS DANGEREUSE DU SERVICE, ET ELLE N'AVAIT AUCUN GARDE.
    ///
    /// Elle déplace un solde vers un compte bancaire. Sans contrôle d'appartenance,
    /// n'importe quel compte inscrit pouvait déclencher un retrait sur le
    /// portefeuille de n'importe quel vendeur, en devinant un identifiant.
    /// </summary>
    private static async Task<IResult> RequestWithdrawalAsync(Guid sellerId, AmountRequest request, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
    {
        if (await DenyUnlessOwnSellerAsync(sellerId, user, access, MerchantCapabilities.WithdrawalRequest, ct) is { } refus)
        {
            return refus;
        }

        return (await sender.Send(new RequestWithdrawalCommand(sellerId, request.Amount), ct))
            .Match(result => Results.Created($"/api/financial/wallets/withdrawals/{result.Id}", result));
    }

    private static async Task<IResult> GetDriverWalletAsync(Guid driverId, ClaimsPrincipal user, IDeliveryModuleApi deliveries, ISender sender, CancellationToken ct)
    {
        if (await DenyUnlessOwnDriverAsync(driverId, user, deliveries, ct) is { } refus)
        {
            return refus;
        }

        return (await sender.Send(new GetDriverWalletQuery(driverId), ct)).Match(Results.Ok);
    }

    private static async Task<IResult> ListDriverWalletTransactionsAsync(Guid driverId, int take, ClaimsPrincipal user, IDeliveryModuleApi deliveries, ISender sender, CancellationToken ct)
    {
        if (await DenyUnlessOwnDriverAsync(driverId, user, deliveries, ct) is { } refus)
        {
            return refus;
        }

        return (await sender.Send(new ListDriverWalletTransactionsQuery(driverId, take), ct)).Match(Results.Ok);
    }

    private static async Task<IResult> GetPlatformWalletAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new GetPlatformWalletQuery(), ct)).Match(Results.Ok);

    private static async Task<IResult> ListPlatformWalletTransactionsAsync(int take, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPlatformWalletTransactionsQuery(take), ct)).Match(Results.Ok);

    private static async Task<IResult> ListPendingWithdrawalsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPendingWithdrawalsQuery(), ct)).Match(Results.Ok);

    private static async Task<IResult> ListProcessingWithdrawalsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListProcessingWithdrawalsQuery(), ct)).Match(Results.Ok);

    private static async Task<IResult> ApproveWithdrawalAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ApproveWithdrawalCommand(id), ct)).Match(Results.Ok);

    private static async Task<IResult> RejectWithdrawalAsync(Guid id, ReasonRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RejectWithdrawalCommand(id, request.Reason), ct)).Match(Results.Ok);

    // ════════════════════════════════════════════════════════════════════════
    // PORTEFEUILLE CLIENT — L'IDENTITÉ VIENT DU JETON, JAMAIS DE LA REQUÊTE.
    //
    // Aucune de ces quatre routes ne prend d'identifiant de client : ni en
    // paramètre de route, ni dans le corps. C'est la seule façon de rendre
    // impossible, par construction, la lecture du portefeuille de quelqu'un
    // d'autre — la faille ISSUE-017/018. Un identifiant accepté puis « vérifié »
    // dépend d'une garde qu'il suffit d'oublier une fois.
    // ════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> GetMyWalletAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        return (await sender.Send(new GetCustomerWalletQuery(userId), ct)).Match(Results.Ok);
    }

    /// <remarks>
    /// `take` PORTE UNE VALEUR PAR DÉFAUT, CONTRAIREMENT AUX ROUTES VENDEUR ET
    /// LIVREUR CI-DESSUS.
    ///
    /// Un `int` non nullable sans défaut est un paramètre REQUIS pour la liaison des
    /// API minimales : `GET .../transactions` sans `?take=` répond 400. Les routes
    /// existantes vivent avec ; on ne les modifie pas ici — ce fichier est déjà
    /// partagé avec un autre lot — mais on ne recopie pas le défaut non plus.
    /// La requête reborne la valeur de toute façon (1..200).
    /// </remarks>
    private static async Task<IResult> ListMyWalletTransactionsAsync(
        ClaimsPrincipal user, ISender sender, CancellationToken ct, int take = 50)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        return (await sender.Send(new ListCustomerWalletTransactionsQuery(userId, take), ct)).Match(Results.Ok);
    }

    private static async Task<IResult> ListMyWithdrawalsAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        return (await sender.Send(new ListCustomerWithdrawalsQuery(userId), ct)).Match(Results.Ok);
    }

    /// <summary>
    /// Demande de virement du solde vers le Mobile Money du client.
    ///
    /// LE NUMÉRO VIENT DU CORPS, ET C'EST VOULU — MAIS PAS L'IDENTITÉ.
    ///
    /// La plateforme ne connaît AUCUN numéro Mobile Money de client : rien dans le
    /// parcours d'achat ne le lui demande. Le client le saisit donc ici, et il est
    /// FIGÉ sur la demande — c'est ce numéro-là, et pas un compte relu plus tard,
    /// que l'administrateur recopiera chez le prestataire. Voir l'encadré de
    /// `CustomerWithdrawal.Msisdn` : c'est la faille de `Withdrawal.PayoutProvider`
    /// qu'on ne réintroduit pas.
    ///
    /// L'identité, elle, ne vient jamais du corps : le portefeuille débité est
    /// celui du jeton.
    /// </summary>
    private static async Task<IResult> RequestMyWithdrawalAsync(
        CustomerWithdrawalRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        // `IdempotencyKey` laissée à null : le gestionnaire la lit dans
        // `HbaRequestContext`, donc dans l'en-tête `Idempotency-Key` que
        // `.RequireIdempotency()` vient d'exiger. Absente des deux côtés, il refuse.
        return (await sender.Send(
                new RequestCustomerWithdrawalCommand(userId, request.Amount, request.Msisdn, request.Provider), ct))
            .Match(result => Results.Created($"/api/financial/wallets/me/withdrawals/{result.Id}", result));
    }

    /// <summary>
    /// File des demandes de virement, par statut. `Requested` par défaut : c'est la
    /// file de travail, et c'est la seule où des fonds sont retenus sans que le
    /// client ait rien reçu.
    /// </summary>
    private static async Task<IResult> ListCustomerWithdrawalQueueAsync(
        string? status, ISender sender, CancellationToken ct)
        => (await sender.Send(
                new ListCustomerWithdrawalsByStatusQuery(
                    string.IsNullOrWhiteSpace(status) ? "Requested" : status), ct))
            .Match(Results.Ok);

    /// <summary>
    /// L'administrateur a exécuté le virement chez le prestataire et le marque payé.
    ///
    /// L'AUTEUR VIENT DU JETON, PAS DU CORPS.
    ///
    /// Le laisser passer dans la charge utile permettrait de signer une sortie
    /// d'argent au nom d'un autre administrateur — et c'est précisément la seule
    /// chose, avec la référence du virement, qui rattache ce mouvement à une
    /// personne.
    /// </summary>
    private static async Task<IResult> MarkCustomerWithdrawalPaidAsync(
        Guid id, CustomerWithdrawalPaidRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } adminId)
        {
            return ApiResults.Unauthorized();
        }

        return (await sender.Send(
                new MarkCustomerWithdrawalPaidCommand(id, adminId, request.ExternalReference), ct))
            .Match(Results.Ok);
    }

    /// <summary>Refus : les fonds retenus sont restitués au portefeuille du client.</summary>
    private static async Task<IResult> RejectCustomerWithdrawalAsync(
        Guid id, ReasonRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } adminId)
        {
            return ApiResults.Unauthorized();
        }

        return (await sender.Send(
                new RejectCustomerWithdrawalCommand(id, adminId, request.Reason), ct))
            .Match(Results.Ok);
    }

    private static async Task<IResult> ListSettlementBatchesAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListSettlementBatchesQuery(), ct)).Match(Results.Ok);

    private static async Task<IResult> GetSettlementBatchAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetSettlementBatchQuery(id), ct)).Match(Results.Ok);

    private static async Task<IResult> GetSellerStatementAsync(
        Guid sellerId, DateTime periodStartUtc, DateTime periodEndUtc,
        ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(sellerId, user, access, MerchantCapabilities.FinanceView, ct)
        ?? (await sender.Send(new GetSellerStatementQuery(sellerId, periodStartUtc, periodEndUtc), ct)).Match(Results.Ok);

    private static async Task<IResult> GetSellerStatementLinesAsync(
        Guid sellerId, DateTime periodStartUtc, DateTime periodEndUtc,
        ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(sellerId, user, access, MerchantCapabilities.FinanceView, ct)
        ?? (await sender.Send(new GetSellerStatementLinesQuery(sellerId, periodStartUtc, periodEndUtc), ct)).Match(Results.Ok);

    private static async Task<IResult> ListSellerPayoutsAsync(
        Guid sellerId, ClaimsPrincipal user, IMerchantAccessApi access,
        ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(sellerId, user, access, MerchantCapabilities.PayoutView, ct)
        ?? (await sender.Send(new ListSellerPayoutsQuery(sellerId), ct)).Match(Results.Ok);

    private static async Task<IResult> RunSettlementAsync(RunSettlementCommand command, ISender sender, CancellationToken ct)
        => (await sender.Send(command, ct)).Match(id => Results.Created($"/api/financial/settlements/{id}", new { id }));

    private static async Task<IResult> MarkPayoutPaidAsync(Guid batchId, Guid payoutId, ProviderReferenceRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new MarkPayoutPaidCommand(batchId, payoutId, request.ProviderReference), ct)).Match(() => Results.NoContent());

    /// <summary>
    /// Déclare un virement de lot REFUSÉ par l'opérateur : le vendeur est recrédité,
    /// une contre-écriture est portée au grand livre et SES gains du lot redeviennent
    /// payables. Le motif ne vit que dans le journal (voir le gestionnaire).
    ///
    /// Un versement déjà marqué payé est REFUSÉ en 409 : l'argent est parti, le
    /// recréditer le ferait sortir deux fois.
    /// </summary>
    private static async Task<IResult> MarkPayoutFailedAsync(Guid batchId, Guid payoutId, ReasonRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new MarkPayoutFailedCommand(batchId, payoutId, request.Reason), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> CancelSettlementBatchAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new CancelSettlementBatchCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>
    /// Refuse la lecture du portefeuille d'un livreur qui n'est pas l'appelant.
    /// </summary>
    /// <remarks>
    /// UN BANDEAU « 404, JAMAIS 403 » ÉTAIT POSÉ AU-DESSUS DE CETTE MÉTHODE.
    ///
    /// Il décrivait `DenyUnlessOwnSellerAsync`, déclarée soixante lignes plus bas,
    /// et se retrouvait à documenter une méthode qui rend 403. Un relecteur qui
    /// auditait la garde livreur lisait un engagement que le corps contredisait
    /// trois lignes plus loin — et concluait à la conformité. C'est exactement le
    /// défaut que ce fichier dénonce ailleurs : « un commentaire qui certifie une
    /// garde absente est pire qu'un silence, il fait passer la relecture ».
    ///
    /// 403 DANS LES DEUX CAS — livreur inconnu ET livreur d'autrui.
    ///
    /// Distinguer les deux dirait à l'appelant si un identifiant correspond à un
    /// livreur réel, ce qui suffit à énumérer la flotte. L'essentiel est que la
    /// réponse ne dépende pas de l'existence.
    ///
    /// ENVELOPPÉ, PAS `Results.Forbid()`.
    ///
    /// Le 403 nu n'a ni `error.code` ni `meta.requestId` : c'est la réponse qu'un
    /// livreur envoie en capture d'écran au support, et la seule qu'aucune trace
    /// ne permette de retrouver.
    /// </remarks>
    private static async Task<IResult?> DenyUnlessOwnDriverAsync(
        Guid driverId, ClaimsPrincipal user, IDeliveryModuleApi deliveries, CancellationToken ct)
    {
        if (user.IsInRole("Admin") || user.IsInRole("Moderator"))
        {
            return null;
        }

        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        var compte = await deliveries.GetDriverAccountAsync(driverId, ct);

        return compte is null || compte.UserId != userId
            ? ApiResults.Failure(
                ErrorCodes.Forbidden,
                "Ce compte livreur n'est pas le vôtre.",
                StatusCodes.Status403Forbidden)
            : null;
    }

    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// `GetAccessAsync` ET NON `GetSellerByUserIdAsync` (lot D1).
    ///
    /// La seconde ne résout que les PROPRIÉTAIRES. Les huit routes gardées ici
    /// sont pourtant l'écran de travail du FINANCE_MANAGER — un rôle que le §10
    /// crée explicitement pour lire relevés et versements, et que cette garde
    /// renvoyait en 404 faute de dossier vendeur à son nom.
    ///
    /// ET LA CAPACITÉ N'EST PAS LA MÊME POUR LES HUIT.
    ///
    /// Lire un relevé, consulter un portefeuille et DEMANDER UN VIREMENT ne sont
    /// pas le même geste. Les ranger sous un contrôle unique reviendrait à dire
    /// qu'un comptable autorisé à lire les comptes peut aussi vider le solde.
    /// C'est précisément la confusion que le §10 sépare en `FINANCE_VIEW`,
    /// `WALLET_VIEW`, `PAYOUT_VIEW` et `WITHDRAWAL_REQUEST`.
    ///
    /// LE 404 RESTE POUR L'APPARTENANCE, LE 403 EST POUR LA CAPACITÉ.
    ///
    /// Un 403 sur l'appartenance confirmerait qu'un vendeur porte cet identifiant,
    /// ce qui suffit à énumérer la place de marché — et ici la confirmation porte
    /// sur des données financières. Une fois le dossier reconnu comme celui de
    /// l'appelant, le 404 n'a plus rien à cacher : c'est SON dossier, et lui
    /// répondre « introuvable » masquerait un rôle trop étroit derrière une panne.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static async Task<IResult?> DenyUnlessOwnSellerAsync(
        Guid sellerId, ClaimsPrincipal user, IMerchantAccessApi access, string capacite, CancellationToken ct)
    {
        if (user.IsInRole("Admin") || user.IsInRole("Moderator"))
        {
            return null;
        }

        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        // 403 ENVELOPPÉ, ET NON UN 404 NU — ALIGNEMENT ISSU DE L'AUDIT.
        //
        // Ce fichier écrivait « 404, JAMAIS 403 : un 403 confirmerait qu'un vendeur
        // porte cet identifiant ». L'argument ne tenait pas. `sellerId` vient de
        // l'URL, les identifiants de vendeurs circulent dans les liens de boutique,
        // et order-service répond 403 sur exactement la même question : le secret
        // n'était pas tenu, il suffisait d'interroger l'autre service. En face, le
        // membre légitime qui s'était trompé d'identifiant recevait deux
        // diagnostics contradictoires selon l'écran qu'il ouvrait.
        //
        // La règle du dépôt est désormais explicite :
        //   identifiant de VENDEUR venu de l'URL      → 403 enveloppé, motif clair ;
        //   identifiant de RESSOURCE (offre, fiche,
        //   lieu, facture, paiement)                  → 404, car il n'est pas public
        //                                               et le confirmer est la fuite.
        var acces = await access.GetAccessAsync(userId, ct);
        if (acces is null || acces.SellerId != sellerId)
        {
            return ApiResults.Failure(
                ErrorCodes.Forbidden,
                "Ce dossier vendeur n'est pas le vôtre.",
                StatusCodes.Status403Forbidden);
        }

        if (!acces.Can(capacite))
        {
            return ApiResults.MissingCapability(capacite);
        }

        // ═════════════════════════════════════════════════════════════════════
        // LE STEP-UP DU §37 — `WITHDRAWAL_REQUEST` EST LA SEULE CONCERNÉE ICI.
        //
        // LA PERMISSION NE SUFFIT PAS POUR CE GESTE-LÀ.
        //
        // Demander un virement déplace le solde vers un compte bancaire. Le rôle
        // dit « ce membre a le droit » ; il ne dit pas « c'est bien lui qui est
        // devant l'écran ». Sur un poste laissé ouvert au marché, la différence
        // est tout ce qui reste entre un compte et un portefeuille vidé.
        //
        // ELLE VIENT APRÈS LA CAPACITÉ, ET C'EST L'ORDRE UTILE.
        //
        // Un membre qui n'a pas `WITHDRAWAL_REQUEST` doit lire « votre rôle ne
        // l'autorise pas », pas « ressaisissez votre mot de passe » — l'inverse
        // l'enverrait prouver son identité pour se voir refuser ensuite.
        //
        // ET L'ADMINISTRATION EST DÉJÀ SORTIE PLUS HAUT.
        //
        // Un modérateur ne passe pas par ce contrôle : il n'a pas de rôle vendeur,
        // et son propre parcours d'authentification n'est pas celui-ci.
        // ═════════════════════════════════════════════════════════════════════
        if (MerchantCapabilities.RequiresStepUp(capacite) && !user.HasRecentAuthentication())
        {
            return ApiResults.ReauthenticationRequired(capacite);
        }

        return null;
    }

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public sealed record ProviderReferenceRequest(string ProviderReference);

    /// <summary>
    /// Demande de virement d'un client. AUCUN identifiant de client : il vient du
    /// jeton (voir `RequestMyWithdrawalAsync`).
    /// </summary>
    public sealed record CustomerWithdrawalRequest(decimal Amount, string Msisdn, string Provider);

    /// <summary>
    /// Référence du virement saisie par l'administrateur : la SEULE preuve que
    /// l'argent est parti — aucun webhook ne confirmera ce versement.
    /// </summary>
    public sealed record CustomerWithdrawalPaidRequest(string ExternalReference);
    public sealed record ReasonRequest(string Reason);
    public sealed record AmountRequest(decimal Amount);
    public sealed record InvoiceLineRequest(string Description, decimal Amount);
    public sealed record UpdateCommissionRuleRequest(decimal Rate, decimal FixedFee, string Currency, decimal? MinFee, decimal? MaxFee, DateTime? EffectiveFromUtc);
    public sealed record AddPaymentMethodRequest(
        string Type,
        string? Label,
        string Provider,
        string? Msisdn,
        string? CardNumber,
        int? ExpiryMonth,
        int? ExpiryYear,
        string? HolderName,
        bool MakeDefault);

    public sealed record UpdatePaymentMethodRequest(
        string? Label,
        string? Provider,
        string? Msisdn,
        int? ExpiryMonth,
        int? ExpiryYear,
        string? HolderName,
        bool MakeDefault);
}
