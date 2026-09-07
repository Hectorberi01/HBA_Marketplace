using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using HBA.Drivers.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Deliveries.Application.Drivers;

namespace HBA.Deliveries.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>LA PROJECTION DISPATCHABLE, ALIMENTÉE PAR LE DOSSIER.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Deliveries.Application.Drivers.ProjectDriverOnDossierVerified")]
public sealed class ProjectDriverOnDossierVerified
    : IIntegrationEventHandler<DriverDossierVerifiedIntegrationEvent>
{
    private readonly IDriverRepository _drivers;
    private readonly IDeliveryUnitOfWork _unitOfWork;
    private readonly ILogger<ProjectDriverOnDossierVerified> _logger;

    public ProjectDriverOnDossierVerified(
        IDriverRepository drivers,
        IDeliveryUnitOfWork unitOfWork,
        ILogger<ProjectDriverOnDossierVerified> logger)
    {
        _drivers = drivers;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        DriverDossierVerifiedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // IDEMPOTENT PAR CONSTRUCTION, ET PAS SEULEMENT PAR L'INBOX.
        var existant = await _drivers.GetByUserIdAsync(integrationEvent.UserId, cancellationToken);
        if (existant is not null)
        {
            var revérifié = existant.Verify();
            if (revérifié.IsFailure)
            {
                // Un compte BLOQUÉ refuse d'être vérifié, et c'est la bonne
                // conduite : la décision de blocage est prise ici, par
                // l'exploitation de la livraison, et un dossier revérifié en amont
                // ne doit pas la lever en silence.
                _logger.LogWarning(
                    "Dossier {DriverId} vérifié en amont mais refusé ici : {Code}.",
                    integrationEvent.DriverId, revérifié.Error.Code);
                return;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        // LE VÉHICULE ARRIVE EN TEXTE, ET LES DEUX ÉNUMÉRATIONS SONT DISTINCTES.
        if (!Enum.TryParse<VehicleType>(integrationEvent.VehicleType, ignoreCase: true, out var vehicule))
        {
            vehicule = VehicleType.Motorcycle;
            _logger.LogWarning(
                "Véhicule « {Vehicule} » inconnu de ce module pour le livreur {DriverId} : repli sur Motorcycle."
                + " Les deux énumérations de véhicule ont divergé — voir D34.",
                integrationEvent.VehicleType, integrationEvent.DriverId);
        }

        // L'IDENTIFIANT DU DOSSIER EST REPRIS TEL QUEL, PAS RETIRÉ AU HASARD.
        var driver = Driver.Register(
            integrationEvent.UserId,
            integrationEvent.FullName,
            integrationEvent.Phone,
            vehicule,
            new DriverId(integrationEvent.DriverId));

        if (driver.IsFailure)
        {
            // ON N'ÉCHOUE PAS, ON JOURNALISE. Rejeter le message le ferait
            // réessayer sans fin : le défaut est dans la donnée émise — un
            // téléphone que `BeninGeography` refuse, par exemple —, et aucun
            // réessai ne le corrigera.
            _logger.LogError(
                "Projection impossible pour le livreur {DriverId} : {Code} — {Message}.",
                integrationEvent.DriverId, driver.Error.Code, driver.Error.Message);
            return;
        }

        // VÉRIFIÉ DANS LE MÊME GESTE. Sans cela, la ligne naîtrait
        // `PendingVerification` et `GoOnline` refuserait — alors que le dossier
        // vient précisément d'être vérifié.
        var verified = driver.Value.Verify();
        if (verified.IsFailure)
        {
            _logger.LogError(
                "Livreur {DriverId} projeté mais non vérifiable : {Code}.",
                integrationEvent.DriverId, verified.Error.Code);
            return;
        }

        await _drivers.AddAsync(driver.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Livreur {DriverId} projeté dans deliveries.drivers depuis son dossier (compte {UserId}).",
            integrationEvent.DriverId, integrationEvent.UserId);
    }
}


/// <summary>Le dossier d'un livreur est SUSPENDU → il cesse d'être dispatchable ici.</summary>
[NomDeConsommateur("HBA.Deliveries.Application.Drivers.WithdrawDriverOnDossierSuspended")]
public sealed class WithdrawDriverOnDossierSuspended
    : IIntegrationEventHandler<DriverSuspendedIntegrationEvent>
{
    private readonly IDriverRepository _drivers;
    private readonly IDeliveryUnitOfWork _unitOfWork;
    private readonly ILogger<WithdrawDriverOnDossierSuspended> _logger;

    public WithdrawDriverOnDossierSuspended(
        IDriverRepository drivers,
        IDeliveryUnitOfWork unitOfWork,
        ILogger<WithdrawDriverOnDossierSuspended> logger)
    {
        _drivers = drivers;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        DriverSuspendedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // Le dossier et la projection partagent le MÊME identifiant : c'est
        // `ProjectDriverOnDossierVerified` qui l'impose en passant le `DriverId` du
        // dossier à `Driver.Register`.
        var driver = await _drivers.GetByIdAsync(new DriverId(integrationEvent.DriverId), cancellationToken);

        if (driver is null)
        {
            // Un dossier suspendu avant d'avoir jamais été vérifié n'a pas de
            // projection : il n'a donc jamais été dispatchable.
            _logger.LogInformation(
                "Livreur {DriverId} suspendu ({Motif}) : aucune projection dispatchable, rien à retirer.",
                integrationEvent.DriverId, integrationEvent.Reason);
            return;
        }

        // Lu AVANT la suspension : `Suspend` écrase la disponibilité par `Offline`,
        // et l'information « il était en course » serait perdue juste après.
        var etaitEnCourse = driver.Availability is DriverAvailability.Busy;

        var retrait = driver.Suspend(integrationEvent.Reason);

        if (retrait.IsFailure)
        {
            // Seul refus possible : le compte est déjà bloqué — donc déjà hors
            // dispatch.
            _logger.LogInformation(
                "Livreur {DriverId} déjà hors dispatch ({Code}) : suspension sans effet supplémentaire.",
                integrationEvent.DriverId, retrait.Error.Code);
            return;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (etaitEnCourse)
        {
            _logger.LogCritical(
                "Livreur {DriverId} suspendu ({Motif}) ALORS QU'IL PORTE UN COLIS. Il ne recevra plus "
                + "de proposition, mais la course en cours n'est ni réaffectée ni annulée : le colis "
                + "doit être repris ou mené à son terme par une décision humaine.",
                integrationEvent.DriverId, integrationEvent.Reason);

            return;
        }

        _logger.LogWarning(
            "Livreur {DriverId} retiré du dispatch : dossier suspendu ({Motif}).",
            integrationEvent.DriverId, integrationEvent.Reason);
    }
}
