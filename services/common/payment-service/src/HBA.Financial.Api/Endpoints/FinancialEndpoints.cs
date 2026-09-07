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

        // `capture` ET `fail` ÉTAIENT OUVERTES À TOUT COMPTE INSCRIT.
        payments.MapPost("/{id:guid}/capture", CapturePaymentAsync).RequireAdmin();
        payments.MapPost("/{id:guid}/fail", FailPaymentAsync).RequireAdmin();
        payments.MapPost("/{id:guid}/refund", RefundPaymentAsync).RequireAdmin();

        // CELLE-CI RESTE À L'ACHETEUR, ET ELLE EST GARDÉE DANS LE HANDLER.
        payments.MapPost("/{id:guid}/redirect/confirm", ConfirmPaymentFromRedirectAsync);
        // LE WEBHOOK DU PRESTATAIRE ÉTAIT DERRIÈRE L'AUTHENTIFICATION.
        payments.MapPost("/webhooks/{provider}", ProcessGatewayWebhookAsync).AllowAnonymous();

        // SURFACE DU §10.12, EN PARALLÈLE DE `/api/financial/payments`.
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
        commissions.MapGet("/", ListCommissionRulesAsync).RequireAdmin();
        commissions.MapGet("/compute", ComputeCommissionAsync);
        commissions.MapPost("/", CreateCommissionRuleAsync).RequireAdmin();
        commissions.MapPut("/{id:guid}", UpdateCommissionRuleAsync).RequireAdmin();
        commissions.MapPost("/{id:guid}/deactivate", DeactivateCommissionRuleAsync).RequireAdmin();
        commissions.MapPost("/{id:guid}/reactivate", ReactivateCommissionRuleAsync).RequireAdmin();
        commissions.MapDelete("/{id:guid}", DeleteCommissionRuleAsync).RequireAdmin();

        // QUATRE ÉCRITURES DE FACTURE ÉTAIENT OUVERTES À TOUT COMPTE INSCRIT.
        var invoices = app.MapAuthenticatedGroup("/api/financial/invoices").WithTags("Financial · Invoices");

        // LA LISTE PLATEFORME MANQUAIT, ET AUCUN ÉCRAN NE POUVAIT EXISTER.
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
        // SOUS `/api/financial/wallets`, ET SURTOUT PAS SOUS UN PRÉFIXE NEUF.
        //
        // La passerelle ne relaie que ce qu'elle connaît : `/api/wallet/{**}` est
        // réécrit vers `/api/financial/wallets/{**}`, et rien d'autre ne mène à ce
        // service. Un groupe `/api/v1/wallet` aurait répondu depuis le conteneur et
        // rendu 404 depuis un téléphone — sans la moindre erreur de configuration
        // pour l'expliquer, puisque le cluster et la destination sont corrects.
        // C'est le défaut que la note « Six routes sur vingt-quatre » décrit dans
        // `bff/.../appsettings.json`, et il ne se voit qu'à l'usage.
        //
        // Le chemin public est donc `/api/wallet/me`.
        wallet.MapGet("/me", GetMyWalletAsync).WithName("GetMyWallet");
        wallet.MapGet("/me/transactions", ListMyWalletTransactionsAsync).WithName("ListMyWalletTransactions");
        wallet.MapGet("/me/withdrawals", ListMyWithdrawalsAsync).WithName("ListMyCustomerWithdrawals");

        // `.RequireIdempotency()` — CETTE ROUTE RETIENT LES FONDS DU CLIENT.
        wallet.MapPost("/me/withdrawals", RequestMyWithdrawalAsync)
            .WithName("RequestCustomerWithdrawal")
            .RequireIdempotency();

        // LA FILE D'ADMINISTRATION — C'EST ELLE QUI PAIE, IL N'Y A RIEN D'AUTRE.
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
        // méthode de vérification qui n'a jamais été écrite.
        settlements.MapGet("/sellers/{sellerId:guid}/statement", GetSellerStatementAsync);
        settlements.MapGet("/sellers/{sellerId:guid}/statement/lines", GetSellerStatementLinesAsync);
        settlements.MapGet("/sellers/{sellerId:guid}/payouts", ListSellerPayoutsAsync);
        // CES TROIS ÉCRITURES ÉTAIENT DANS LE GROUPE AUTHENTIFIÉ.
        var settlementAdmin = app.MapAdminGroup("/api/financial/settlements")
            .WithTags("Admin · Financial · Settlements");
        settlementAdmin.MapPost("/", RunSettlementAsync);
        settlementAdmin.MapPost("/{batchId:guid}/payouts/{payoutId:guid}/paid", MarkPayoutPaidAsync);

        // LA COMPENSATION D'UN VIREMENT REFUSÉ (ISSUE-015).
        settlementAdmin.MapPost("/{batchId:guid}/payouts/{payoutId:guid}/failed", MarkPayoutFailedAsync);
        settlementAdmin.MapPost("/{id:guid}/cancel", CancelSettlementBatchAsync);

        return app;
    }

    private static async Task<IResult> ListPaymentsAsync(int page, int pageSize, string? search, string? status, string? sort, string? dir, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPaymentsQuery(page, pageSize, search, status, sort, dir), ct)).Match(Results.Ok);

    private static async Task<IResult> GetPaymentStatsAsync(string? search, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetPaymentStatsQuery(search), ct)).Match(Results.Ok);

    /// <summary>CES DEUX LECTURES RENDAIENT LE PAIEMENT DE N'IMPORTE QUI.</summary>
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
    private static bool PeutVoirLePaiement(ClaimsPrincipal user, Guid buyerId)
        => user.IsInRole("Admin") || user.IsInRole("Moderator") || CurrentUserId(user) == buyerId;

    /// <summary>L'IDENTITÉ EST ÉCRASÉE, PAS LUE.</summary>
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

    /// <summary>GARDÉE PAR LA PROPRIÉTÉ DU PAIEMENT.</summary>
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

    /// <summary>ELLE RENDAIT LE TAUX NÉGOCIÉ DE N'IMPORTE QUEL VENDEUR.</summary>
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

    /// <summary>ELLE RENDAIT LA FACTURE DE N'IMPORTE QUEL VENDEUR.</summary>
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

    // ───────────────────────────────────────────────────────── §10.12 (v1)

    /// <summary>`POST /api/v1/payments/intents`.</summary>
    /// <summary>Même garde que `InitiatePaymentAsync` : l'appelant est imposé, jamais lu.</summary>
    private static async Task<IResult> CreatePaymentIntentAsync(
        InitiatePaymentCommand command, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => (await sender.Send(command with { RequestedByUserId = CurrentUserId(user) }, ct))
            .Match(intent => ApiResults.Created(intent, $"/api/v1/payments/intents/{intent.PaymentId}"));

    /// <summary>`GET /api/v1/payments/intents/{id}` — état d'un paiement (§10.12).</summary>
    private static async Task<IResult> GetPaymentIntentAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var resultat = await sender.Send(new GetPaymentQuery(id), ct);

        return resultat.IsSuccess && !PeutVoirLePaiement(user, resultat.Value.BuyerId)
            ? ApiResults.NotFound(ServiceCodes.Payment)
            : resultat.Match(payment => ApiResults.Ok(payment));
    }

    /// <summary>`POST /api/v1/payments/{id}/refunds` — 202 Accepted (§10.12).</summary>
    private static async Task<IResult> CreateRefundAsync(
        Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new RefundPaymentCommand(id), ct))
            .Match(refund => ApiResults.Accepted(new { paymentId = id, refundId = refund.RefundId, status = refund.Status }));

    /// <summary>Page de factures, tous vendeurs confondus (Admin).</summary>
    private static async Task<IResult> ListInvoicesAsync(
        int? page, int? pageSize, string? status, Guid? sellerId,
        ISender sender, CancellationToken ct)
    {
        var demande = new ListInvoicesQuery(Page: page ?? 1, Status: status, SellerId: sellerId);

        var resultat = await sender.Send(
            pageSize is { } taille ? demande with { PageSize = taille } : demande, ct);

        return resultat.Match(donnees => ApiResults.Page(donnees));
    }

    /// <summary>
    /// LA DERNIÈRE DES « TROIS LECTURES FINANCIÈRES SANS CONTRÔLE D'APPARTENANCE »
    /// que le commentaire du fichier signalait, et la seule qui restait.
    ///
    /// Les factures d'un vendeur portent son chiffre d'affaires ligne à ligne. Sans
    /// ce garde, il suffisait d'un identifiant de vendeur — visible dans n'importe
    /// quelle fiche boutique — pour lire le carnet de commandes d'un concurrent.
    /// </summary>
    /// <remarks>
    /// CE BLOC AVAIT ÉTÉ SÉPARÉ DE SA MÉTHODE, ET LE COMPILATEUR LE DISAIT.
    ///
    /// Il vivait quatre-vingts lignes plus haut, collé sous `GetInvoiceAsync` et
    /// suivi d'un séparateur `//` — donc rattaché à rien : CS1587, « le
    /// commentaire XML n'est pas placé dans un élément valide du langage ».
    ///
    /// L'avertissement ne coûtait pas une compilation ; il coûtait la
    /// DOCUMENTATION. Le garde qu'il justifie était décrit au-dessus d'une
    /// méthode qui ne le porte pas, et la méthode qui le porte n'avait rien.
    /// Quiconque relisait `ListInvoicesBySellerAsync` pour savoir pourquoi ce
    /// `DenyUnlessOwnSellerAsync` est là ne trouvait aucune réponse.
    /// </remarks>
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

    /// <summary>LA ROUTE LA PLUS DANGEREUSE DU SERVICE, ET ELLE N'AVAIT AUCUN GARDE.</summary>
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

    // PORTEFEUILLE CLIENT — L'IDENTITÉ VIENT DU JETON, JAMAIS DE LA REQUÊTE.

    private static async Task<IResult> GetMyWalletAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        return (await sender.Send(new GetCustomerWalletQuery(userId), ct)).Match(Results.Ok);
    }

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

    /// <summary>Demande de virement du solde vers le Mobile Money du client.</summary>
    private static async Task<IResult> RequestMyWithdrawalAsync(
        CustomerWithdrawalRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        // `IdempotencyKey` laissée à null : le gestionnaire la lit dans
        // `HbaRequestContext`, donc dans l'en-tête `Idempotency-Key` que
        // `.RequireIdempotency()` vient d'exiger.
        return (await sender.Send(
                new RequestCustomerWithdrawalCommand(userId, request.Amount, request.Msisdn, request.Provider), ct))
            .Match(result => Results.Created($"/api/financial/wallets/me/withdrawals/{result.Id}", result));
    }

    /// <summary>File des demandes de virement, par statut.</summary>
    private static async Task<IResult> ListCustomerWithdrawalQueueAsync(
        string? status, ISender sender, CancellationToken ct)
        => (await sender.Send(
                new ListCustomerWithdrawalsByStatusQuery(
                    string.IsNullOrWhiteSpace(status) ? "Requested" : status), ct))
            .Match(Results.Ok);

    /// <summary>
    /// L'administrateur a exécuté le virement chez le prestataire et le marque
    /// payé.
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
    /// Déclare un virement de lot REFUSÉ par l'opérateur : le vendeur est
    /// recrédité, une contre-écriture est portée au grand livre et SES gains du lot
    /// redeviennent payables.
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

        // LE STEP-UP DU §37 — `WITHDRAWAL_REQUEST` EST LA SEULE CONCERNÉE ICI.
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

    /// <summary>Demande de virement d'un client.</summary>
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
