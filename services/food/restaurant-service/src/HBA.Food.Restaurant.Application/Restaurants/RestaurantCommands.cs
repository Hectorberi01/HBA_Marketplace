using System.Globalization;
using HBA.Food.Application.Abstractions;
using HBA.Food.Domain.Restaurants;
using HBA.Food.Domain.Staff;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Application.Restaurants;

/// <summary>
/// Candidature d'un restaurateur.
///
/// OUVERTE À TOUT COMPTE AUTHENTIFIÉ, et c'est délibéré : s'inscrire est une
/// CANDIDATURE. Le rôle FoodPartner n'est attribué qu'à la validation du dossier
/// — sinon chacun se décernerait sa propre habilitation. Même raisonnement que
/// pour les livreurs.
/// </summary>
public sealed record RegisterRestaurantCommand(Guid OwnerUserId, string Name, string Phone) : ICommand<Guid>;

public sealed record UpdateRestaurantProfileCommand(
    Guid RestaurantId, string Name, string? Description, string Phone) : ICommand;

/// <summary>
/// Rattache le logo et la couverture (§3), par IDENTIFIANT de média.
///
/// SÉPARÉE DU PROFIL, DÉLIBÉRÉMENT. Une image se téléverse d'abord, se
/// rattache ensuite : les mélanger ferait passer une URL arbitraire là où l'on
/// attend un média validé, et ferait perdre la photo à chaque changement de nom.
/// </summary>
public sealed record SetRestaurantMediaCommand(
    Guid RestaurantId, Guid? LogoMediaId, Guid? CoverMediaId) : ICommand;

public sealed record AttachRestaurantLocationCommand(Guid RestaurantId, Guid FulfillmentLocationId) : ICommand;

/// <summary>
/// Rattache le dossier vendeur qui encaissera les recettes de l'établissement.
///
/// L'EXISTENCE ET LA VALIDITÉ DU DOSSIER SONT VÉRIFIÉES PAR L'APPELANT.
///
/// Food ne connaît pas Sellers. C'est la route — la couche qui voit les deux —
/// qui contrôle que le dossier appartient au propriétaire de l'établissement,
/// qu'il est validé, et qu'il porte un compte de reversement. Sans ce contrôle,
/// les recettes d'un restaurant partiraient sur le compte d'un tiers.
/// </summary>
public sealed record AttachRestaurantPayoutSellerCommand(Guid RestaurantId, Guid SellerId) : ICommand;

public sealed record SetPreparationTimeCommand(Guid RestaurantId, int Minutes) : ICommand;

/// <summary>Manuel ou automatique (§3). Le mode voyage en chaîne depuis la route.</summary>
public sealed record SetAcceptanceModeCommand(Guid RestaurantId, OrderAcceptanceMode Mode) : ICommand;

/// <summary>
/// Minimum de commande et plafond de charge (§3, §14).
///
/// Les trois curseurs commerciaux de la même page de réglages : les séparer
/// ferait trois appels pour un seul geste, et l'un des trois serait oublié.
/// </summary>
public sealed record SetOrderLimitsCommand(
    Guid RestaurantId, decimal? MinimumOrderAmount, int? MaximumActiveOrders, bool BlockWhenSaturated) : ICommand;

/// <summary>Un créneau, en entrée. Heures au format « HH:mm ».</summary>
public sealed record ServiceHoursInput(string Day, string OpensAt, string ClosesAt);

public sealed record SetServiceHoursCommand(Guid RestaurantId, IReadOnlyList<ServiceHoursInput> Hours) : ICommand;

/// <summary>Rattache le logo de l'établissement, ou le retire.</summary>
/// <remarks>
/// `Restaurant.SetMedia` EXISTAIT SANS AUCUN APPELANT — dixième occurrence de ce
/// motif dans ce dépôt : une couche applicative écrite, joignable, et que rien
/// n'atteint. Le sélecteur d'activité affiche donc les restaurants sans logo depuis
/// le début, et le commentaire de `GetMerchantActivitiesHandler` l'attribuait à une
/// limite du contrat plutôt qu'à une route manquante.
///
/// LE MÉDIA ET SON ADRESSE ENSEMBLE — voir `Restaurant.SetMedia`.
/// </remarks>
public sealed record SetRestaurantLogoCommand(
    Guid RestaurantId, Guid? LogoMediaId, string? LogoPublicUrl) : ICommand;

/// <summary>
/// Une exception d'horaire datée (§4) : « 15 août → fermé », « 31 décembre → 18 h – 23 h ».
///
/// <c>IsClosed</c> vrai ignore les heures. Date au format « aaaa-mm-jj », heures
/// « HH:mm », en heure LOCALE du Bénin — c'est ainsi que le restaurateur les
/// saisit, et les convertir en UTC décalerait un jour férié d'une heure.
/// </summary>
public sealed record SetSpecialHoursCommand(
    Guid RestaurantId, string Date, bool IsClosed, string? OpensAt, string? ClosesAt, string? Reason) : ICommand;

public sealed record ClearSpecialHoursCommand(Guid RestaurantId, string Date) : ICommand;

public sealed record SubmitRestaurantCommand(Guid RestaurantId) : ICommand;

/// <summary>Pause courte déclarée par le restaurateur : coup de feu, panne de gaz.</summary>
public sealed record PauseRestaurantCommand(Guid RestaurantId, int Minutes) : ICommand;

public sealed record ResumeRestaurantCommand(Guid RestaurantId) : ICommand;

// ── Décisions de l'exploitation ─────────────────────────────────────────────

public sealed record ApproveRestaurantCommand(Guid RestaurantId) : ICommand;

public sealed record RejectRestaurantCommand(Guid RestaurantId, string? Reason) : ICommand;

public sealed record SuspendRestaurantCommand(Guid RestaurantId, string? Reason) : ICommand;

public sealed record LiftRestaurantSuspensionCommand(Guid RestaurantId) : ICommand;

internal sealed class RestaurantCommandHandler
    : ICommandHandler<RegisterRestaurantCommand, Guid>,
      ICommandHandler<UpdateRestaurantProfileCommand>,
      ICommandHandler<AttachRestaurantLocationCommand>,
      ICommandHandler<AttachRestaurantPayoutSellerCommand>,
      ICommandHandler<SetRestaurantMediaCommand>,
      ICommandHandler<SetPreparationTimeCommand>,
      ICommandHandler<SetAcceptanceModeCommand>,
      ICommandHandler<SetOrderLimitsCommand>,
      ICommandHandler<SetServiceHoursCommand>,
      ICommandHandler<SetRestaurantLogoCommand>,
      ICommandHandler<SetSpecialHoursCommand>,
      ICommandHandler<ClearSpecialHoursCommand>,
      ICommandHandler<SubmitRestaurantCommand>,
      ICommandHandler<PauseRestaurantCommand>,
      ICommandHandler<ResumeRestaurantCommand>,
      ICommandHandler<ApproveRestaurantCommand>,
      ICommandHandler<RejectRestaurantCommand>,
      ICommandHandler<SuspendRestaurantCommand>,
      ICommandHandler<LiftRestaurantSuspensionCommand>
{
    private readonly IRestaurantRepository _restaurants;
    private readonly IRestaurantStaffRepository _staff;
    private readonly IFoodUnitOfWork _unitOfWork;

    public RestaurantCommandHandler(
        IRestaurantRepository restaurants, IRestaurantStaffRepository staff, IFoodUnitOfWork unitOfWork)
    {
        _restaurants = restaurants;
        _staff = staff;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(RegisterRestaurantCommand command, CancellationToken cancellationToken)
    {
        // UN SEUL ÉTABLISSEMENT PAR COMPTE.
        //
        // L'index unique en base le garantit, mais un doublon y devient une
        // exception de contrainte — illisible pour l'appelant. On répond ici par
        // un conflit explicite.
        var existant = await _restaurants.GetByOwnerAsync(command.OwnerUserId, cancellationToken);
        if (existant is not null)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "food.restaurant.already_registered", "Ce compte a déjà un établissement."));
        }

        var restaurant = Restaurant.Register(command.OwnerUserId, command.Name, command.Phone);
        if (restaurant.IsFailure)
        {
            return Result.Failure<Guid>(restaurant.Error);
        }

        await _restaurants.AddAsync(restaurant.Value, cancellationToken);

        // ═════════════════════════════════════════════════════════════════════
        // LE FONDATEUR EST CRÉÉ ICI, DANS LA MÊME TRANSACTION.
        //
        // Sans cette ligne, l'établissement naîtrait sans personnel — et depuis
        // que les routes de l'espace restaurateur autorisent sur l'APPARTENANCE
        // et non plus sur `OwnerUserId`, son propre créateur ne pourrait plus y
        // entrer. Il aurait déposé une candidature à laquelle il n'a pas accès.
        //
        // Même unité de travail, délibérément : un restaurant enregistré dont
        // l'amorçage du personnel aurait échoué serait exactement cet
        // établissement inaccessible, et rien ne le signalerait.
        // ═════════════════════════════════════════════════════════════════════
        await _staff.AddAsync(
            RestaurantStaff.Founder(restaurant.Value.Id.Value, command.OwnerUserId), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return restaurant.Value.Id.Value;
    }

    public Task<Result> Handle(UpdateRestaurantProfileCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.RestaurantId, cancellationToken,
            r => r.UpdateProfile(command.Name, command.Description, command.Phone));

    public Task<Result> Handle(AttachRestaurantLocationCommand command, CancellationToken cancellationToken)
        // L'appartenance du lieu au restaurateur est vérifiée par l'appelant, qui
        // voit Food ET Inventory. Ce module ne connaît pas Inventory.
        => MutateAsync(command.RestaurantId, cancellationToken,
            r => r.AttachFulfillmentLocation(command.FulfillmentLocationId));

    public Task<Result> Handle(AttachRestaurantPayoutSellerCommand command, CancellationToken cancellationToken)
        // La validité du dossier vendeur est vérifiée par l'appelant, qui voit
        // Food ET Sellers. Ce module ne connaît pas Sellers.
        => MutateAsync(command.RestaurantId, cancellationToken,
            r => r.AttachPayoutSeller(command.SellerId));

    public Task<Result> Handle(SetRestaurantMediaCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.RestaurantId, cancellationToken,
            r => r.SetMedia(command.LogoMediaId, command.CoverMediaId));

    public Task<Result> Handle(SetPreparationTimeCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.RestaurantId, cancellationToken, r => r.SetPreparationTime(command.Minutes));

    public Task<Result> Handle(SetAcceptanceModeCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.RestaurantId, cancellationToken, r => r.SetAcceptanceMode(command.Mode));

    public Task<Result> Handle(SetOrderLimitsCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.RestaurantId, cancellationToken, r => r.SetOrderLimits(
            command.MinimumOrderAmount, command.MaximumActiveOrders, command.BlockWhenSaturated));

    public Task<Result> Handle(SetRestaurantLogoCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.RestaurantId, cancellationToken, r =>
            // LA COUVERTURE EST PRÉSERVÉE, ET C'EST UN PIÈGE DE `SetMedia` : elle
            // prend les DEUX médias et écrase celui qu'on ne lui passe pas. Envoyer
            // `null` ici effacerait la photo de couverture à chaque changement de
            // logo, sans que rien ne le signale.
            r.SetMedia(command.LogoMediaId, r.CoverMediaId, command.LogoPublicUrl));

    public Task<Result> Handle(SetServiceHoursCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.RestaurantId, cancellationToken, r =>
        {
            var creneaux = new List<ServiceHours>();

            foreach (var entree in command.Hours)
            {
                if (!Enum.TryParse<DayOfWeek>(entree.Day, ignoreCase: true, out var jour))
                {
                    return Result.Failure(Error.Validation(
                        "food.restaurant.day_invalid", $"Jour invalide : « {entree.Day} »."));
                }

                // Culture INVARIANTE : le projet tourne en InvariantGlobalization,
                // et faire dépendre la lecture d'un horaire d'un réglage serveur
                // ferait cesser « 14:30 » d'être lu un jour, sans qu'aucun code ait
                // changé.
                if (!TimeOnly.TryParse(entree.OpensAt, CultureInfo.InvariantCulture, out var ouverture)
                    || !TimeOnly.TryParse(entree.ClosesAt, CultureInfo.InvariantCulture, out var fermeture))
                {
                    return Result.Failure(Error.Validation(
                        "food.restaurant.hours_invalid", "Heures attendues au format « HH:mm »."));
                }

                var creneau = ServiceHours.Create(jour, ouverture, fermeture);
                if (creneau.IsFailure)
                {
                    return Result.Failure(creneau.Error);
                }

                creneaux.Add(creneau.Value);
            }

            return r.SetServiceHours(creneaux);
        });

    public Task<Result> Handle(SetSpecialHoursCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.RestaurantId, cancellationToken, r =>
        {
            if (!DateOnly.TryParse(command.Date, CultureInfo.InvariantCulture, out var jour))
            {
                return Result.Failure(Error.Validation(
                    "food.special_hours.date_invalid", $"Date attendue au format « aaaa-mm-jj » : « {command.Date} »."));
            }

            // LE MÉNAGE DES EXCEPTIONS PASSÉES SE FAIT ICI.
            //
            // Un jour férié par an et par restaurant, conservé pour toujours,
            // finirait par peser — et les horaires du 15 août 2024 n'expliquent
            // plus rien. Le faire à l'écriture plutôt que par une tâche de fond :
            // le volume est minuscule, et une tâche de fond de plus serait une
            // tâche de fond à surveiller.
            r.PurgePastSpecialHours(DateTime.UtcNow);

            if (command.IsClosed)
            {
                var fermeture = SpecialOpeningHour.Closed(jour, command.Reason);
                return fermeture.IsFailure ? Result.Failure(fermeture.Error) : r.SetSpecialHours(fermeture.Value);
            }

            if (!TimeOnly.TryParse(command.OpensAt, CultureInfo.InvariantCulture, out var ouverture)
                || !TimeOnly.TryParse(command.ClosesAt, CultureInfo.InvariantCulture, out var fermeture2))
            {
                return Result.Failure(Error.Validation(
                    "food.special_hours.time_invalid",
                    "Heures attendues au format « HH:mm », ou cochez « fermé »."));
            }

            var exception = SpecialOpeningHour.Open(jour, ouverture, fermeture2, command.Reason);
            return exception.IsFailure ? Result.Failure(exception.Error) : r.SetSpecialHours(exception.Value);
        });

    public Task<Result> Handle(ClearSpecialHoursCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.RestaurantId, cancellationToken, r =>
            DateOnly.TryParse(command.Date, CultureInfo.InvariantCulture, out var jour)
                ? r.ClearSpecialHours(jour)
                : Result.Failure(Error.Validation(
                    "food.special_hours.date_invalid", $"Date attendue au format « aaaa-mm-jj » : « {command.Date} ».")));

    public Task<Result> Handle(SubmitRestaurantCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.RestaurantId, cancellationToken, r => r.SubmitForApproval());

    public Task<Result> Handle(PauseRestaurantCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.RestaurantId, cancellationToken, r =>
        {
            // La DURÉE est fournie, l'instant est calculé ici : demander une date
            // absolue à un restaurateur en plein coup de feu, sur un téléphone,
            // c'est demander une erreur de fuseau.
            var maintenant = DateTime.UtcNow;
            return r.PauseUntil(maintenant.AddMinutes(command.Minutes), maintenant);
        });

    public Task<Result> Handle(ResumeRestaurantCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.RestaurantId, cancellationToken, r => r.Resume());

    public Task<Result> Handle(ApproveRestaurantCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.RestaurantId, cancellationToken, r => r.Approve());

    public Task<Result> Handle(RejectRestaurantCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.RestaurantId, cancellationToken, r => r.Reject(command.Reason));

    public Task<Result> Handle(SuspendRestaurantCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.RestaurantId, cancellationToken, r => r.Suspend(command.Reason));

    public Task<Result> Handle(LiftRestaurantSuspensionCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.RestaurantId, cancellationToken, r => r.LiftSuspension());

    /// <summary>
    /// Charge, applique, enregistre.
    ///
    /// AUCUN CONTRÔLE DE PROPRIÉTÉ ICI, ET C'EST UN CHOIX À CONNAÎTRE.
    ///
    /// Ces commandes sont appelées soit par le restaurateur lui-même — l'appelant
    /// ayant alors résolu l'établissement DEPUIS SON JETON, donc sans identifiant
    /// falsifiable —, soit par l'exploitation, qui agit légitimement sur
    /// l'établissement d'autrui. Ajouter un OwnerId ici forcerait l'admin à en
    /// fabriquer un.
    ///
    /// La conséquence : une route qui accepterait un RestaurantId venu du client
    /// SANS le résoudre depuis le jeton ouvrirait un IDOR. C'est la règle que le
    /// BFF doit tenir.
    /// </summary>
    private async Task<Result> MutateAsync(
        Guid restaurantId, CancellationToken cancellationToken, Func<Restaurant, Result> action)
    {
        var restaurant = await _restaurants.GetByIdAsync(new RestaurantId(restaurantId), cancellationToken);
        if (restaurant is null)
        {
            return Result.Failure(Error.NotFound("food.restaurant.not_found", "Établissement introuvable."));
        }

        var result = action(restaurant);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
