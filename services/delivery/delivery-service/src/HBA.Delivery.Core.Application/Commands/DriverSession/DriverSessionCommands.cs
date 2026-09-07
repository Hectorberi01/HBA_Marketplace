using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using Microsoft.Extensions.Logging;

namespace HBA.Deliveries.Application.Drivers;

// LA SESSION DE TRAVAIL DU LIVREUR — ET LE CHAÎNON QUI MANQUAIT.

/// <summary>Traduit le compte du jeton en identifiant de livreur.</summary>
public sealed record ResolveDriverQuery(Guid UserId) : IQuery<Guid>;

/// <summary>Le livreur prend son service : il devient dispatchable.</summary>
public sealed record GoOnlineCommand(Guid DriverId) : ICommand;

/// <summary>Le livreur termine son service.</summary>
public sealed record GoOfflineCommand(Guid DriverId) : ICommand;

/// <summary>Le livreur reste en ligne mais ne reçoit plus de propositions.</summary>
public sealed record TakeBreakCommand(Guid DriverId) : ICommand;

/// <summary>Le livreur transmet sa position.</summary>
public sealed record ReportDriverPositionCommand(Guid DriverId, double Latitude, double Longitude) : ICommand;

internal sealed class DriverSessionCommandHandler
    : IQueryHandler<ResolveDriverQuery, Guid>,
      ICommandHandler<GoOnlineCommand>,
      ICommandHandler<GoOfflineCommand>,
      ICommandHandler<TakeBreakCommand>,
      ICommandHandler<ReportDriverPositionCommand>
{
    /// <summary>À QUELLE FRÉQUENCE LA POSITION EST RECOPIÉE EN BASE.</summary>
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
        // ligne n'existe ici.
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
        // actif.
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

    /// <summary>LE BATTEMENT DE POSITION.</summary>
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
        // pour deux minutes seulement.
        await _locations.SetAsync(driverId, position.Value, cancellationToken);

        // La recopie en base est ÉPISODIQUE — voir `IntervalleDeRecopie`.
        var derniere = driver.LastPositionAtUtc;
        if (derniere is null || DateTime.UtcNow - derniere.Value >= IntervalleDeRecopie)
        {
            driver.RecordPosition(position.Value);

            // CETTE ÉCRITURE-LÀ PEUT ÊTRE PERDUE, ET C'EST LA SEULE DU MODULE.
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
