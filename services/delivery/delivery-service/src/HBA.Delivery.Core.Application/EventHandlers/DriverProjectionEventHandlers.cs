using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using HBA.Drivers.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace HBA.Deliveries.Application.Drivers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA PROJECTION DISPATCHABLE, ALIMENTÉE PAR LE DOSSIER.
///
/// AVANT CE LOT, `deliveries.drivers` N'AVAIT AUCUN ÉCRIVAIN.
///
/// `IDriverRepository.AddAsync` n'était appelé de nulle part, et le
/// `RegisterDriverCommandHandler` que cite `DriverConfiguration` n'a jamais
/// existé. La table était configurée, migrée, lue par le dispatch — et vide pour
/// toujours. Même une fois le cache de positions alimenté, aucun livreur n'aurait
/// pu être retenu : `ListByIdsAsync` n'aurait rendu personne.
///
/// POURQUOI PAR ÉVÉNEMENT ET NON PAR APPEL gRPC.
///
/// C'est le sens de D34 : deux tables, deux propriétaires, reliés par contrat ou
/// par événement — jamais par une référence de projet, qui remettrait les deux
/// services dans le même déploiement. Entre les deux formes, l'événement gagne
/// ici parce que la vérification d'un dossier est un fait RARE et DÉFINITIF : il
/// n'y a rien à interroger en temps réel, il y a un fait à recevoir. Un appel
/// gRPC obligerait en plus delivery-service à connaître l'adresse de
/// driver-service, donc à ne pas démarrer sans lui.
///
/// CE QUE CE CHEMIN COÛTE, ET IL FAUT L'ANNONCER.
///
///   • Il est ASYNCHRONE. Entre la vérification du dossier et le moment où le
///     livreur peut prendre son service, il s'écoule le temps de l'outbox et du
///     bus. Quelques secondes en régime normal ; indéfiniment si le drain est
///     arrêté. Le livreur voit alors « aucun livreur rattaché à ce compte » sur
///     `POST /api/deliveries/mine/online`, ce qui ne lui dit pas grand-chose.
///
///   • Il ne couvre QUE la vérification. `DriverSuspendedIntegrationEvent` est
///     publié par driver-service et N'EST CONSOMMÉ PAR PERSONNE : un livreur
///     suspendu dans son dossier continue donc de recevoir des propositions ici.
///     C'est le manque le plus sérieux que ce lot laisse ouvert, et le geste qui
///     le ferme est un second gestionnaire dans ce fichier appelant
///     `Driver.Suspend`.
///
///   • Le TÉLÉPHONE et le NOM sont recopiés une fois, à la vérification. Les
///     modifier ensuite dans le dossier ne les met pas à jour ici. Le client
///     appellerait alors un ancien numéro.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
        //
        // L'inbox du module (`EfConsumerInbox`) écarte déjà les rejeux du MÊME
        // message. Elle n'écarte pas deux vérifications successives du même
        // dossier, qui produisent deux messages DIFFÉRENTS portant le même fait —
        // le cas exact d'un dossier rouvert puis revérifié. La recherche par
        // compte, puis `Verify()` qui est lui-même idempotent, couvrent les deux.
        var existant = await _drivers.GetByUserIdAsync(integrationEvent.UserId, cancellationToken);
        if (existant is not null)
        {
            var revérifié = existant.Verify();
            if (revérifié.IsFailure)
            {
                // Un compte BLOQUÉ refuse d'être vérifié, et c'est la bonne
                // conduite : la décision de blocage est prise ici, par
                // l'exploitation de la livraison, et un dossier revérifié
                // en amont ne doit pas la lever en silence.
                _logger.LogWarning(
                    "Dossier {DriverId} vérifié en amont mais refusé ici : {Code}.",
                    integrationEvent.DriverId, revérifié.Error.Code);
                return;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        // LE VÉHICULE ARRIVE EN TEXTE, ET LES DEUX ÉNUMÉRATIONS SONT DISTINCTES.
        //
        // driver-service a la sienne (`DriverVehicleType`), ce module a
        // `VehicleType`. Elles portent aujourd'hui les mêmes six valeurs, mais rien
        // au compilateur ne le garantit depuis que les deux services ne partagent
        // plus de projet de domaine (D34). Une valeur inconnue retombe sur
        // `Motorcycle` — le mode écrasant de la flotte à Cotonou — et le journalise
        // BRUYAMMENT : dispatcher un tricycle comme une moto lui refuse les courses
        // lourdes qu'il est justement le seul à pouvoir prendre.
        if (!Enum.TryParse<VehicleType>(integrationEvent.VehicleType, ignoreCase: true, out var vehicule))
        {
            vehicule = VehicleType.Motorcycle;
            _logger.LogWarning(
                "Véhicule « {Vehicule} » inconnu de ce module pour le livreur {DriverId} : repli sur Motorcycle."
                + " Les deux énumérations de véhicule ont divergé — voir D34.",
                integrationEvent.VehicleType, integrationEvent.DriverId);
        }

        // L'IDENTIFIANT DU DOSSIER EST REPRIS TEL QUEL, PAS RETIRÉ AU HASARD.
        //
        // Un livreur doit porter UN identifiant dans toute la plateforme : c'est
        // celui que `DriverAccountView` expose et sous lequel financial-service
        // tient son portefeuille. Deux identifiants auraient donné deux
        // portefeuilles, dont un seul se remplirait.
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
            // réessai ne le corrigera. Le livreur reste non dispatchable, et la
            // ligne de journal dit pourquoi.
            _logger.LogError(
                "Projection impossible pour le livreur {DriverId} : {Code} — {Message}.",
                integrationEvent.DriverId, driver.Error.Code, driver.Error.Message);
            return;
        }

        // VÉRIFIÉ DANS LE MÊME GESTE. Sans cela, la ligne naîtrait
        // `PendingVerification` et `GoOnline` refuserait — alors que le dossier
        // vient précisément d'être vérifié. `Verify()` lève ici
        // `DriverVerifiedDomainEvent`, que ce module traduit déjà pour Identity.
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


/// <summary>
/// Le dossier d'un livreur est SUSPENDU → il cesse d'être dispatchable ici.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE FIL MANQUAIT, ET C'ÉTAIT LE PLUS GRAVE DES DEUX (lot 5.2).
///
/// `DriverSuspendedIntegrationEvent` était publié par driver-service et n'avait
/// AUCUN consommateur. Un exploitant suspendait un livreur pour faute grave — un
/// permis retiré, un colis volé, une agression signalée —, le dossier passait en
/// suspendu, et la projection dispatchable de ce module ne bougeait pas. Le
/// livreur continuait de recevoir des propositions et d'aller chez les clients.
///
/// Suspendre quelqu'un et le laisser travailler, ce n'est pas une suspension
/// partielle : c'est une suspension qui n'existe pas.
///
/// POURQUOI PAR ÉVÉNEMENT ET NON PAR APPEL SYNCHRONE — ET CE QUE ÇA COÛTE.
///
/// L'appel synchrone serait plus sûr : la suspension mordrait à l'instant où
/// l'exploitant clique. Il inverserait la dépendance — driver-service appellerait
/// delivery-service —, ce que D34 et D36 refusent précisément pour que les deux
/// services restent déployables séparément.
///
/// Le prix est une FENÊTRE : le temps de l'outbox et d'un aller Kafka, quelques
/// secondes, pendant lesquelles une course peut encore être proposée. C'est borné
/// et c'est acceptable pour un motif administratif. Ça ne l'est PAS pour une
/// faute grave en cours — et rien ici ne distingue les deux. Si ce besoin
/// apparaît, il faudra un geste d'écartement immédiat côté exploitation, pas un
/// raccourcissement de cette fenêtre.
///
/// CE QUE CE GESTIONNAIRE NE FAIT PAS : LA COURSE EN COURS.
///
/// `Driver.Suspend` ne refuse PAS un livreur occupé — vérifié dans le domaine, pas
/// supposé : elle pose `Offline` quel que soit l'état de disponibilité. Le livreur
/// cesse donc de recevoir de NOUVELLES propositions, mais **le colis qu'il porte
/// déjà reste entre ses mains**. Ce gestionnaire ne le réaffecte pas, ne l'annule
/// pas, ne prévient pas l'acheteur.
///
/// Ce n'est pas un oubli mais une limite de portée : reprendre une course en vol
/// suppose de décider ce qu'on fait du colis — le récupérer, le laisser finir, le
/// déclarer perdu — et c'est un geste d'exploitation, pas une conséquence
/// automatique d'une suspension administrative.
///
/// Le cas est donc DÉTECTÉ et journalisé en `Critical` : c'est la seule situation
/// où un livreur suspendu reste en activité, et elle doit se voir.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
        // `ProjectDriverOnDossierVerified` qui l'impose en passant le `DriverId`
        // du dossier à `Driver.Register`. Sans ce partage, il n'y aurait ici
        // aucun moyen de retrouver qui suspendre.
        var driver = await _drivers.GetByIdAsync(new DriverId(integrationEvent.DriverId), cancellationToken);

        if (driver is null)
        {
            // Un dossier suspendu avant d'avoir jamais été vérifié n'a pas de
            // projection : il n'a donc jamais été dispatchable. Rien à faire, et
            // ce n'est pas une anomalie.
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
            // dispatch. L'objectif est atteint, il n'y a rien à réparer.
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
