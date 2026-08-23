using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using Microsoft.Extensions.Logging;

namespace HBA.Deliveries.Application.Drivers;

// ═════════════════════════════════════════════════════════════════════════════
// LA SESSION DE TRAVAIL DU LIVREUR — ET LE CHAÎNON QUI MANQUAIT.
//
// `IDriverLocationCache.SetAsync` N'AVAIT AUCUN APPELANT (ISSUE-030, D30).
//
// `DispatchDeliveryCommandHandler` lit ce cache pour trouver les livreurs proches
// du point de collecte. Personne n'y écrivait jamais. `FindNearbyAsync` rendait
// donc systématiquement une liste vide, le dispatch concluait « aucun livreur
// disponible », réessayait cinq fois et abandonnait la course. AUCUNE COURSE
// N'ÉTAIT JAMAIS PROPOSÉE À PERSONNE, sur une plateforme dont tout le reste du
// code de livraison est correct. Ce n'était pas une fonctionnalité manquante :
// c'était le domaine entier qui était inerte.
//
// POURQUOI CES COMMANDES SONT ICI ET NON DANS driver-service.
//
// Trois raisons, dans cet ordre :
//
//   1. `IDriverLocationCache` est un PORT DE CE MODULE. Le faire appeler depuis
//      driver-service exigerait soit une référence de projet vers ce domaine —
//      c'est le cycle que le lot 5.4 vient de couper (D34) —, soit un appel gRPC
//      supplémentaire toutes les cinq à quinze secondes PAR LIVREUR, sur le
//      chemin le plus sensible à la latence de la plateforme, avec un mode de
//      panne de plus.
//
//   2. La position n'a de sens que rapprochée de la DISPONIBILITÉ et de la
//      MISSION EN COURS, qui vivent toutes deux sur l'agrégat `Driver` de ce
//      module. Un livreur suspendu ne doit pas voir sa position conservée
//      (`RecordPosition` le refuse) ; un livreur hors ligne doit sortir du cache.
//      Ces trois décisions se prennent ensemble ou pas du tout.
//
//   3. tracking-service aurait été l'autre candidat naturel, et il ne convient
//      PAS : c'est encore une maquette dont l'état vit dans un
//      `ConcurrentDictionary` de processus (D35), sans base ni outbox. Faire
//      passer l'alimentation du dispatch par lui, ce serait faire dépendre
//      l'attribution des courses d'un service qui perd son état à chaque
//      redémarrage et ne le partage pas entre réplicas. Le suivi CLIENT reste
//      son métier ; l'alimentation du dispatch ne l'est pas.
//
// CE QUE CE DÉCOUPAGE COÛTE : le livreur parle à DEUX services. Son dossier —
// inscription, pièces, vérification — est chez driver-service ; sa session de
// travail est ici. C'est le prix de deux propriétaires, et il est payé par le
// client mobile, pas par le dispatch.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Traduit le compte du jeton en identifiant de livreur.
///
/// TOUTES LES ROUTES LIVREUR PASSENT PAR ELLE, ET C'EST LA GARDE. Le jeton
/// porte un `userId` ; les courses portent un `driverId`. Accepter le second en
/// paramètre laisserait n'importe quel compte authentifié faire progresser — donc
/// encaisser — la course d'un autre.
/// </summary>
public sealed record ResolveDriverQuery(Guid UserId) : IQuery<Guid>;

/// <summary>Le livreur prend son service : il devient dispatchable.</summary>
public sealed record GoOnlineCommand(Guid DriverId) : ICommand;

/// <summary>Le livreur termine son service.</summary>
public sealed record GoOfflineCommand(Guid DriverId) : ICommand;

/// <summary>Le livreur reste en ligne mais ne reçoit plus de propositions.</summary>
public sealed record TakeBreakCommand(Guid DriverId) : ICommand;

/// <summary>
/// Le livreur transmet sa position. C'est l'appelant qui manquait à
/// <see cref="IDriverLocationCache.SetAsync"/>.
/// </summary>
public sealed record ReportDriverPositionCommand(Guid DriverId, double Latitude, double Longitude) : ICommand;

internal sealed class DriverSessionCommandHandler
    : IQueryHandler<ResolveDriverQuery, Guid>,
      ICommandHandler<GoOnlineCommand>,
      ICommandHandler<GoOfflineCommand>,
      ICommandHandler<TakeBreakCommand>,
      ICommandHandler<ReportDriverPositionCommand>
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// À QUELLE FRÉQUENCE LA POSITION EST RECOPIÉE EN BASE.
    ///
    /// La position COURANTE vit dans Redis, et elle y va à chaque battement — c'est
    /// ce que le dispatch lit. `Driver.LastKnownPosition` n'existe que pour
    /// survivre à un vidage du cache, et l'écrire à chaque battement serait
    /// exactement ce que l'encadré de `IDriverLocationCache` interdit : sept à
    /// vingt écritures PostgreSQL par seconde pour cent livreurs, sur une donnée
    /// dont on ne garde jamais l'historique.
    ///
    /// Cinq minutes est un compromis : après un redémarrage de Redis, le dispatch
    /// repart avec des positions vieilles d'au plus cinq minutes — inutilisables
    /// telles quelles, puisque `MaxPositionAge` vaut deux minutes, mais suffisantes
    /// pour l'exploitation qui cherche où était un livreur.
    ///
    /// CE QUE CE CHOIX NE COUVRE PAS : la copie en base n'est PAS réinjectée
    /// dans le cache au démarrage. Après un vidage de Redis, aucun livreur n'est
    /// dispatchable tant qu'il n'a pas émis à nouveau — c'est-à-dire au plus
    /// quinze secondes. C'est acceptable, et c'est assumé plutôt que découvert.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static readonly TimeSpan IntervalleDeRecopie = TimeSpan.FromMinutes(5);

    private readonly IDriverRepository _drivers;
    private readonly IDriverLocationCache _locations;
    private readonly IDeliveryUnitOfWork _unitOfWork;
    private readonly ILogger<DriverSessionCommandHandler> _logger;

    public DriverSessionCommandHandler(
        IDriverRepository drivers,
        IDriverLocationCache locations,
        IDeliveryUnitOfWork unitOfWork,
        ILogger<DriverSessionCommandHandler> logger)
    {
        _drivers = drivers;
        _locations = locations;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(ResolveDriverQuery query, CancellationToken cancellationToken)
    {
        if (query.UserId == Guid.Empty)
        {
            return Result.Failure<Guid>(
                Error.Unauthorized("driver.unauthenticated", "Aucun compte dans le jeton présenté."));
        }

        var driver = await _drivers.GetByUserIdAsync(query.UserId, cancellationToken);

        // « INTROUVABLE » COUVRE DEUX CAS, ET C'EST VOULU : le compte n'a pas de
        // dossier livreur, ou son dossier n'a pas encore été vérifié — auquel cas
        // driver-service n'a pas encore publié `driver.dossier-verified` et aucune
        // ligne n'existe ici. Distinguer les deux dirait à un compte quelconque
        // quels autres comptes sont des livreurs.
        return driver is null
            ? Result.Failure<Guid>(NotADriver())
            : Result.Success(driver.Id.Value);
    }

    public async Task<Result> Handle(GoOnlineCommand command, CancellationToken cancellationToken)
    {
        var driver = await _drivers.GetByIdAsync(new DriverId(command.DriverId), cancellationToken);
        if (driver is null)
        {
            return Result.Failure(NotADriver());
        }

        // La garde est dans l'agrégat : `GoOnline` refuse si le compte n'est pas
        // actif. C'est ce qui empêche un livreur suspendu de se remettre en ligne
        // depuis son téléphone — l'incident que la séparation statut/disponibilité
        // existe pour éviter.
        var online = driver.GoOnline();
        if (online.IsFailure)
        {
            return online;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> Handle(GoOfflineCommand command, CancellationToken cancellationToken)
    {
        var driver = await _drivers.GetByIdAsync(new DriverId(command.DriverId), cancellationToken);
        if (driver is null)
        {
            return Result.Failure(NotADriver());
        }

        var offline = driver.GoOffline();
        if (offline.IsFailure)
        {
            return offline;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // LE RETRAIT DU CACHE VIENT APRÈS L'ÉCRITURE, ET PAS AVANT.
        //
        // Si `SaveChanges` échoue, le livreur reste « disponible » en base ; l'avoir
        // déjà retiré du cache l'aurait rendu invisible au dispatch sans que rien
        // ne le dise, et il aurait attendu des courses qui ne venaient plus.
        //
        // ET SI C'EST LE RETRAIT QUI ÉCHOUE, ON NE DÉFAIT RIEN : la clé
        // horodatée expire d'elle-même en deux minutes (`MaxPositionAge`), et
        // `DispatchPolicy` écarte de toute façon un livreur dont la ligne dit
        // « hors ligne ». Le cache est un index, pas une source de vérité.
        await _locations.RemoveAsync(new DriverId(command.DriverId), cancellationToken);

        return Result.Success();
    }

    public async Task<Result> Handle(TakeBreakCommand command, CancellationToken cancellationToken)
    {
        var driver = await _drivers.GetByIdAsync(new DriverId(command.DriverId), cancellationToken);
        if (driver is null)
        {
            return Result.Failure(NotADriver());
        }

        var pause = driver.TakeBreak();
        if (pause.IsFailure)
        {
            return pause;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE BATTEMENT DE POSITION.
    ///
    /// C'EST L'UNIQUE ÉCRIVAIN DE `IDriverLocationCache`, ET IL DOIT LE RESTER.
    ///
    /// Un second mécanisme — une route sur tracking-service, un consommateur
    /// Kafka, un travailleur qui recopierait la base vers Redis — donnerait deux
    /// sources pour la même donnée, dont l'une serait toujours en retard sur
    /// l'autre. La question « pourquoi ce livreur n'est-il pas proposé ? »
    /// deviendrait alors sans réponse.
    ///
    /// UNE POSITION DE LIVREUR HORS LIGNE N'EST PAS ENREGISTRÉE.
    ///
    /// C'est une donnée personnelle de géolocalisation : la conserver quand le
    /// livreur n'est pas en service serait une collecte sans finalité. L'agrégat
    /// refuse déjà de la recopier si le compte n'est pas actif (`RecordPosition`) ;
    /// ce handler refuse en plus de la mettre en cache si le livreur n'est pas en
    /// service. Les deux gardes ne disent pas la même chose : la première protège
    /// des comptes bloqués, la seconde de la collecte hors service.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<Result> Handle(ReportDriverPositionCommand command, CancellationToken cancellationToken)
    {
        var position = Coordinates.Create(command.Latitude, command.Longitude);
        if (position.IsFailure)
        {
            return Result.Failure(position.Error);
        }

        var driverId = new DriverId(command.DriverId);

        var driver = await _drivers.GetByIdAsync(driverId, cancellationToken);
        if (driver is null)
        {
            return Result.Failure(NotADriver());
        }

        if (driver.Availability is DriverAvailability.Offline)
        {
            return Result.Failure(Error.Conflict(
                "driver.offline",
                "Prenez votre service avant de transmettre votre position."));
        }

        // Redis d'abord : c'est LA donnée que le dispatch lit, et elle est bonne
        // pour deux minutes seulement. Retarder son écriture derrière un
        // `SaveChanges` ferait payer à chaque battement la latence d'une
        // transaction PostgreSQL dont il n'a pas besoin.
        await _locations.SetAsync(driverId, position.Value, cancellationToken);

        // La recopie en base est ÉPISODIQUE — voir `IntervalleDeRecopie`.
        var derniere = driver.LastPositionAtUtc;
        if (derniere is null || DateTime.UtcNow - derniere.Value >= IntervalleDeRecopie)
        {
            driver.RecordPosition(position.Value);

            // ═════════════════════════════════════════════════════════════════
            // CETTE ÉCRITURE-LÀ PEUT ÊTRE PERDUE, ET C'EST LA SEULE DU MODULE.
            //
            // `drivers` porte désormais un jeton de concurrence (§6) : il protège
            // la DISPONIBILITÉ, écrite depuis trois endroits qui ne s'attendent pas
            // — le dispatch qui marque occupé, le livreur qui se met en pause, la
            // course qui se termine.
            //
            // Ce jeton s'applique à TOUT `UPDATE` de la ligne, recopie de position
            // comprise. Sans cette tolérance, un battement GPS qui croiserait un
            // changement de statut rendrait un 409 à l'application du livreur —
            // pour une écriture qui n'a aucune importance.
            //
            // Elle n'en a aucune parce que la donnée que le dispatch LIT est déjà
            // écrite : Redis l'a reçue quelques lignes plus haut, et c'est sa seule
            // source. Cette recopie n'est qu'un instantané de confort, refait au
            // plus tard dans cinq minutes.
            //
            // `TrySaveChangesAsync` ET NON UN `try/catch` : la couche Application
            // ne référence pas EF Core — règle du dépôt, rappelée en toutes lettres
            // dans `ExecuteRefundCommandHandler`. `DbUpdateConcurrencyException` ne
            // peut être nommée que dans Infrastructure. La tolérance est déclarée
            // dans le contrat et implémentée là-bas.
            //
            // ON NE RÉESSAIE PAS : recharger et réécrire dans le même scope
            // re-dispatcherait les événements de domaine et dupliquerait l'outbox.
            // ═════════════════════════════════════════════════════════════════
            if (!await _unitOfWork.TrySaveChangesAsync(cancellationToken))
            {
                _logger.LogDebug(
                    "Recopie de position ignorée pour le livreur {DriverId} : la ligne a changé "
                    + "entre-temps. La position est en cache, la recopie attendra le prochain "
                    + "battement.",
                    command.DriverId);
            }
        }

        _logger.LogDebug(
            "Position reçue pour le livreur {DriverId} (disponibilité {Availability}).",
            command.DriverId, driver.Availability);

        return Result.Success();
    }

    /// <summary>
    /// 404 et non 403 : confirmer qu'un livreur existe derrière un identifiant
    /// donnerait à tout compte authentifié le moyen d'énumérer la flotte.
    /// </summary>
    private static Error NotADriver()
        => Error.NotFound("driver.not_found", "Aucun livreur n'est rattaché à ce compte.");
}
