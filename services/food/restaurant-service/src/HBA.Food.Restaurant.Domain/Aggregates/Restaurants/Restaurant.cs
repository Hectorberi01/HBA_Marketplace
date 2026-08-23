using HBA.Food.Domain.Restaurants.Events;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Restaurants;

public readonly record struct RestaurantId(Guid Value)
{
    public static RestaurantId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN ÉTABLISSEMENT HBA FOOD.
///
/// POURQUOI CE N'EST PAS UN <c>Store</c>, MALGRÉ DES CHAMPS IDENTIQUES
///
/// Une boutique et un restaurant portent tous deux un nom, un contact, des
/// horaires et un statut ouvert/fermé. Les RÈGLES, elles, sont opposées :
///
///   • hors horaires, une boutique DIFFÈRE la commande — le vendeur expédiera
///     demain, et refuser ferait perdre les commandes du soir ;
///   • hors horaires, un restaurant la REFUSE — un repas ne se prépare pas en
///     différé, et un client qui a payé à deux heures du matin attendra seul.
///
/// Les fondre aurait obligé chaque lecture d'horaires à demander « suis-je un
/// restaurant ? ». Le premier <c>if</c> de ce genre dans Sellers aurait signé la
/// perte de la frontière — c'est la leçon déjà écrite dans DeliveryIds.
///
/// IL NE PORTE PAS D'ADRESSE, comme Store : le lieu physique vit dans
/// Inventory (<c>FulfillmentLocation</c>), et c'est lui que HBA Delivery
/// interroge pour bâtir la course. Deux adresses pour un même lieu divergent au
/// premier déménagement.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class Restaurant : AggregateRoot<RestaurantId>
{
    private readonly List<ServiceHours> _serviceHours = new();
    private readonly List<SpecialOpeningHour> _specialHours = new();

    private Restaurant()
    {
    }

    private Restaurant(RestaurantId id, Guid ownerUserId, string name, string phone)
        : base(id)
    {
        OwnerUserId = ownerUserId;
        Name = name;
        Phone = phone;
        Status = RestaurantStatus.Draft;
        PreparationMinutes = DefaultPreparationMinutes;

        // Manuel par défaut : un maquis qui découvre l'application ne doit pas se
        // retrouver engagé sur des commandes qu'il n'a pas vues passer.
        AcceptanceMode = OrderAcceptanceMode.Manual;
        MaximumActiveOrders = DefaultMaximumActiveOrders;

        CreatedOnUtc = DateTime.UtcNow;

        Raise(new RestaurantRegisteredDomainEvent(id.Value, ownerUserId, name));
    }

    /// <summary>Compte HBA du restaurateur. Le rôle FoodPartner lui est attribué à la validation.</summary>
    public Guid OwnerUserId { get; private set; }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE DOSSIER VENDEUR QUI ENCAISSE LES RECETTES DE CET ÉTABLISSEMENT.
    ///
    /// SANS LUI, LE RESTAURATEUR N'ÉTAIT PAYÉ PAR AUCUN CHEMIN.
    ///
    /// La chaîne de reversement — gains, portefeuille, demande de retrait, payout
    /// Mobile Money — est entièrement indexée sur un identifiant de VENDEUR, et
    /// résout le compte de destination par `ISellerModuleApi`. Un restaurant sans
    /// dossier vendeur n'a donc ni portefeuille, ni relevé, ni destination : ses
    /// ventes étaient encaissées par la plateforme et s'arrêtaient là.
    ///
    /// SIMPLE GUID : FOOD NE CONNAÎT PAS SELLERS.
    ///
    /// C'est ce qui rend le module extractible. L'existence du dossier, sa
    /// validation et son compte de reversement sont vérifiés par la couche qui
    /// voit les deux — le composition root, à la validation de l'établissement.
    ///
    /// Nul tant qu'aucun dossier n'est rattaché, et l'établissement ne peut alors
    /// pas entrer en service : encaisser sans pouvoir reverser, c'est fabriquer
    /// une dette envers un restaurateur qui a déjà servi les repas.
    /// </summary>
    public Guid? PayoutSellerId { get; private set; }

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE LOGO, PAR RÉFÉRENCE AU SERVICE MÉDIA (cahier Food §3, cahier Media §1).
    ///
    /// UN IDENTIFIANT, PAS UNE URL, ET SURTOUT PAS UNE NAVIGATION.
    ///
    /// « Les autres services gardent seulement un MediaId, mais ne stockent jamais
    /// les octets du fichier dans leur base métier. » Food ne référence donc pas
    /// HBA.Media — sa frontière l'interdit, comme pour Inventory. C'est la couche
    /// qui voit les deux qui résout l'URL, et qui vérifie que le média appartient
    /// bien à cet établissement.
    ///
    /// Stocker l'URL, c'est ce que faisait la version précédente : le jour où le
    /// domaine du CDN change, ou qu'un fichier est retraité, chaque table du dépôt
    /// porte une URL périmée qu'aucune migration ne saura toutes retrouver.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Guid? LogoMediaId { get; private set; }

    /// <summary>Image de couverture (§3). Même règle : un identifiant.</summary>
    public Guid? CoverMediaId { get; private set; }

    /// <summary>
    /// TRANSITOIRE — l'URL d'avant la bascule vers le service média.
    ///
    /// Conservée pour ne pas perdre les logos déjà en ligne : une migration ne
    /// peut pas fabriquer de MediaAsset à partir d'une URL, faute d'empreinte et
    /// de taille. La projection préfère <c>LogoMediaId</c> et retombe ici.
    ///
    /// À SUPPRIMER une fois les logos existants reversés dans Media. Tant que
    /// cette colonne vit, elle est une seconde vérité — c'est le prix d'une
    /// migration sans perte, pas un état souhaitable.
    /// </summary>
    public string? LegacyLogoUrl { get; private set; }

    /// <summary>Adresse publique de <see cref="LogoMediaId"/>, recopiée au dépôt.</summary>
    /// <remarks>
    /// `LogoMediaId` RESTE LA VÉRITÉ ; ceci n'est qu'une commodité d'affichage
    /// qui peut devenir obsolète (bucket renommé, `PublicBaseUrl` réécrite). Voir
    /// `MenuItem.ImagePublicUrl` pour le raisonnement complet.
    /// </remarks>
    public string? LogoPublicUrl { get; private set; }

    /// <summary>Numéro de l'ÉTABLISSEMENT — celui qu'appelle un livreur devant la porte.</summary>
    public string Phone { get; private set; } = default!;

    public RestaurantStatus Status { get; private set; }
    public string? StatusReason { get; private set; }

    /// <summary>
    /// Lieu de collecte (<c>Inventory.FulfillmentLocation</c>). Nul tant qu'aucun
    /// n'est rattaché — et le restaurant ne peut alors pas entrer en service.
    ///
    /// Simple Guid : Food ne connaît pas Inventory. L'existence du lieu est
    /// vérifiée par la couche qui voit les deux.
    /// </summary>
    public Guid? FulfillmentLocationId { get; private set; }

    /// <summary>
    /// Délai de préparation annoncé, en minutes.
    ///
    /// C'EST UNE PROMESSE FAITE AU CLIENT, PAS UN ORNEMENT. Elle alimente
    /// l'heure de livraison affichée avant paiement, et le dispatch d'une course
    /// qui ne doit pas arriver avant que le plat soit prêt — un livreur qui
    /// attend vingt minutes devant un comptoir est une course perdue pour tout le
    /// monde.
    /// </summary>
    public int PreparationMinutes { get; private set; }

    /// <summary>
    /// Fin de la pause déclarée par le restaurateur. Nulle si aucune pause.
    ///
    /// Une pause n'est PAS un changement de statut : le coup de feu, la panne de
    /// gaz et la rupture générale durent une heure, pas une saison. Basculer le
    /// statut les rendrait indiscernables d'une suspension dans l'historique.
    /// </summary>
    public DateTime? PausedUntilUtc { get; private set; }

    /// <summary>
    /// Manuel ou automatique (§3). Manuel par défaut.
    ///
    /// CE RÉGLAGE N'EST JAMAIS RÉÉCRIT PAR LA SATURATION. L'auto-acceptation se
    /// SUSPEND quand la cuisine est pleine, et reprend d'elle-même quand elle se
    /// vide — voir <see cref="AssessLoad"/>. Basculer le mode en base obligerait
    /// quelqu'un à le remettre à la main après le coup de feu, et personne n'y
    /// penserait avant de constater que plus rien ne part tout seul.
    /// </summary>
    public OrderAcceptanceMode AcceptanceMode { get; private set; }

    /// <summary>
    /// Montant minimum d'une commande, hors livraison (§3). Nul = aucun minimum.
    ///
    /// Dans la devise des lignes de commande — XOF partout au Bénin. Un minimum
    /// existe parce qu'un plat à 500 F ne paie ni le gaz ni le temps du cuisinier.
    /// </summary>
    public decimal? MinimumOrderAmount { get; private set; }

    /// <summary>
    /// Combien de commandes la cuisine peut tenir en parallèle (§14). Nul = pas de
    /// plafond.
    ///
    /// NULLABLE PLUTÔT QU'UN ZÉRO SENTINELLE : « zéro commande maximum » et
    /// « pas de limite » sont deux phrases opposées, et un jour quelqu'un aurait
    /// lu le zéro dans le mauvais sens.
    /// </summary>
    public int? MaximumActiveOrders { get; private set; }

    /// <summary>
    /// À saturation, refuser les nouvelles commandes plutôt que de les empiler.
    ///
    /// C'est l'« éventuellement » du §14 — un CHOIX du restaurateur. Faux par
    /// défaut : refuser fait perdre une vente, et le restaurateur garde de toute
    /// façon la pause manuelle. Ceux qui tiennent à leur délai annoncé
    /// l'activeront.
    /// </summary>
    public bool BlocksOrdersWhenSaturated { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }

    public IReadOnlyCollection<ServiceHours> ServiceHours => _serviceHours.AsReadOnly();

    /// <summary>Les exceptions datées (§4) : jours fériés, inventaires, fermetures ponctuelles.</summary>
    public IReadOnlyCollection<SpecialOpeningHour> SpecialHours => _specialHours.AsReadOnly();

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CET ÉTABLISSEMENT A-T-IL SA PLACE DANS LA VITRINE ?
    ///
    /// CETTE QUESTION N'ÉTAIT POSÉE NULLE PART, ET LA VITRINE RÉPONDAIT « OUI »
    /// À TOUT LE MONDE.
    ///
    /// Les routes publiques chargeaient un établissement par son identifiant et le
    /// rendaient tel quel. Conséquences, toutes réelles :
    ///
    ///   • un dossier en BROUILLON — jamais examiné, adresse jamais vérifiée —
    ///     était consultable par n'importe qui, avec son numéro de téléphone ;
    ///   • un établissement SUSPENDU l'était aussi : la sanction ne retirait rien
    ///     de la vitrine.
    ///
    /// Seul un établissement EN SERVICE a été vu par quelqu'un chez HBA. C'est la
    /// seule chose que la vitrine a le droit de montrer.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public bool IsPubliclyVisible => Status == RestaurantStatus.Active;

    /// <summary>Délai par défaut : un plat se prépare rarement en moins d'un quart d'heure.</summary>
    public const int DefaultPreparationMinutes = 30;

    /// <summary>Bornes du délai annoncé. Au-delà de trois heures, ce n'est plus de la restauration livrée.</summary>
    public const int MinPreparationMinutes = 5;
    public const int MaxPreparationMinutes = 180;

    /// <summary>Plafond par défaut, repris du cahier (§14).</summary>
    public const int DefaultMaximumActiveOrders = 15;

    /// <summary>
    /// À partir de quelle proportion du plafond la cuisine est « en forte demande ».
    ///
    /// 70 % : assez tôt pour que le client soit prévenu avant que le délai ne
    /// dérape, assez tard pour ne pas afficher l'alerte à la moindre pointe.
    /// </summary>
    public const double HighLoadRatio = 0.7;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA CHARGE DE LA CUISINE (cahier §14).
    ///
    /// LE NOMBRE DE COMMANDES EN COURS VIENT DE L'APPELANT.
    ///
    /// Cet agrégat ne voit pas les commandes — elles sont une autre racine. Lui
    /// donner un dépôt en ferait un service déguisé. Même forme que
    /// <c>CanAcceptOrders(nowUtc, hasOrderableItem)</c> : le fait manquant est
    /// fourni, la règle reste ici.
    ///
    /// L'ATTENTE SUPPLÉMENTAIRE EST DÉRIVÉE DU RYTHME DU RESTAURANT, PAS D'UNE
    /// CONSTANTE.
    ///
    /// Une demi-préparation en forte demande, une préparation entière à
    /// saturation. Un maquis qui sort ses plats en quinze minutes ajoute sept
    /// minutes puis quinze ; une table qui met quarante-cinq minutes ajoute vingt
    /// puis quarante-cinq. Un « +10 minutes » global aurait été juste pour l'un et
    /// absurde pour l'autre.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public KitchenLoad AssessLoad(int activeOrders)
    {
        if (MaximumActiveOrders is not { } plafond || plafond <= 0)
        {
            // Sans plafond déclaré, il n'y a pas de saturation à constater. Inventer
            // un seuil par défaut ferait apparaître « forte demande » chez un
            // restaurateur qui n'a jamais rien réglé, et il ne saurait pas d'où ça
            // vient ni comment l'éteindre.
            return new KitchenLoad(KitchenLoadLevel.Normal, activeOrders, 0, false, false);
        }

        if (activeOrders >= plafond)
        {
            return new KitchenLoad(
                KitchenLoadLevel.Saturated,
                activeOrders,
                PreparationMinutes,
                AutoAcceptSuspended: true,
                BlocksNewOrders: BlocksOrdersWhenSaturated);
        }

        if (activeOrders >= (int)Math.Ceiling(plafond * HighLoadRatio))
        {
            return new KitchenLoad(
                KitchenLoadLevel.High,
                activeOrders,
                PreparationMinutes / 2,

                // L'AUTO-ACCEPTATION SURVIT À LA FORTE DEMANDE. Le cahier ne la
                // coupe qu'à SATURATION. La couper plus tôt renverrait le
                // restaurateur à des acceptations manuelles au pire moment — celui
                // où il a le moins le temps de regarder un écran.
                AutoAcceptSuspended: false,
                BlocksNewOrders: false);
        }

        return new KitchenLoad(KitchenLoadLevel.Normal, activeOrders, 0, false, false);
    }

    /// <summary>
    /// Déclare une exception datée (§4). Remplace celle du même jour s'il y en a une.
    ///
    /// UNE SEULE EXCEPTION PAR DATE. Deux exceptions le même jour — l'une
    /// « fermé », l'autre « 18 h – 23 h » — n'auraient aucun ordre de priorité
    /// évident, et la réponse dépendrait de l'ordre de lecture en base.
    /// </summary>
    public Result SetSpecialHours(SpecialOpeningHour exception)
    {
        _specialHours.RemoveAll(e => e.Date == exception.Date);
        _specialHours.Add(exception);
        Touch();

        return Result.Success();
    }

    /// <summary>Retire l'exception d'une date : le jour redevient un jour ordinaire.</summary>
    public Result ClearSpecialHours(DateOnly date)
    {
        _specialHours.RemoveAll(e => e.Date == date);
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Oublie les exceptions PASSÉES.
    ///
    /// SANS CE MÉNAGE, LA TABLE GROSSIT SANS FIN — un jour férié par an et par
    /// restaurant, pour toujours. Elles ne servent à rien une fois la date
    /// dépassée : les horaires du 15 août 2024 n'expliquent plus rien.
    ///
    /// Appelé à chaque enregistrement d'exception plutôt que par une tâche de
    /// fond : le volume est minuscule, et une tâche de fond de plus serait une
    /// tâche de fond à surveiller.
    /// </summary>
    public int PurgePastSpecialHours(DateTime nowUtc)
        => _specialHours.RemoveAll(e => e.Date < BeninTime.LocalDate(nowUtc));

    /// <summary>
    /// Rattache le logo et la couverture (§3).
    ///
    /// L'EXISTENCE ET L'APPARTENANCE DU MÉDIA NE SONT PAS VÉRIFIÉES ICI : Food
    /// ne connaît pas le service média. C'est l'appelant, qui voit les deux, qui
    /// s'en charge — même règle que pour le lieu de collecte côté Inventory.
    ///
    /// Sans ce contrôle en amont, un restaurateur afficherait la photo d'un
    /// concurrent, ou pire une pièce d'identité dont il aurait deviné l'identifiant.
    /// </summary>
    /// <param name="logoPublicUrl">
    /// L'adresse publique du logo, recopiée au rattachement.
    ///
    /// MÊME DÉNORMALISATION QUE `MenuItem.ImagePublicUrl`, ET POUR LA MÊME
    /// RAISON — voir son encadré. Sans elle, `GetMerchantActivitiesHandler` continue
    /// de forcer `LogoUrl: null` sur toute activité RESTAURANT, avec ce commentaire :
    /// « food-service ne rend qu'un identifiant de média, pas d'URL ». Le sélecteur
    /// d'activité, premier écran après connexion, n'affiche donc jamais le logo d'un
    /// restaurant. C'était vrai ; ça n'a plus à l'être.
    /// </param>
    public Result SetMedia(Guid? logoMediaId, Guid? coverMediaId, string? logoPublicUrl = null)
    {
        LogoMediaId = logoMediaId == Guid.Empty ? null : logoMediaId;
        CoverMediaId = coverMediaId == Guid.Empty ? null : coverMediaId;

        if (LogoMediaId is null)
        {
            // Retirer le logo retire son adresse : la garder afficherait encore
            // l'image d'un restaurant qui n'en a plus.
            LogoPublicUrl = null;
        }
        else
        {
            // Le média prend le relais : l'URL héritée n'a plus lieu d'être, et la
            // laisser ferait réapparaître l'ancien logo au premier retrait du nouveau.
            LegacyLogoUrl = null;
            LogoPublicUrl = string.IsNullOrWhiteSpace(logoPublicUrl) ? null : logoPublicUrl.Trim();
        }

        Touch();
        return Result.Success();
    }

    /// <summary>Manuel ou automatique (§3).</summary>
    public Result SetAcceptanceMode(OrderAcceptanceMode mode)
    {
        AcceptanceMode = mode;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Minimum de commande et plafond de charge (§3, §14).
    ///
    /// Les deux ensemble : ce sont les deux curseurs commerciaux de la même page
    /// de réglages, et les séparer ferait deux appels pour un seul geste.
    /// </summary>
    public Result SetOrderLimits(decimal? minimumOrderAmount, int? maximumActiveOrders, bool blockWhenSaturated)
    {
        if (minimumOrderAmount is { } minimum && minimum < 0m)
        {
            return Result.Failure(Error.Validation(
                "food.restaurant.minimum_invalid", "Le minimum de commande ne peut pas être négatif."));
        }

        if (maximumActiveOrders is { } plafond && plafond < 1)
        {
            // Zéro voudrait dire « aucune commande acceptée », ce qui se dit par une
            // pause ou une fermeture — pas par un plafond.
            return Result.Failure(Error.Validation(
                "food.restaurant.max_orders_invalid",
                "Le plafond de commandes doit valoir au moins 1. Laissez-le vide pour ne pas en fixer."));
        }

        MinimumOrderAmount = minimumOrderAmount is > 0m ? minimumOrderAmount : null;
        MaximumActiveOrders = maximumActiveOrders;
        BlocksOrdersWhenSaturated = blockWhenSaturated;
        Touch();

        return Result.Success();
    }

    public static Result<Restaurant> Register(Guid ownerUserId, string name, string phone)
    {
        if (ownerUserId == Guid.Empty)
        {
            return Error.Validation("food.restaurant.owner_required", "Le compte du restaurateur est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("food.restaurant.name_required", "Le nom de l'établissement est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(phone) || phone.Trim().Length is < 8 or > 20)
        {
            // Obligatoire dès la création, contrairement à bien des champs : sans
            // numéro, un livreur devant une porte close n'a personne à appeler.
            return Error.Validation("food.restaurant.phone_required", "Le téléphone de l'établissement est obligatoire.");
        }

        return new Restaurant(RestaurantId.New(), ownerUserId, name.Trim(), phone.Trim());
    }

    public Result UpdateProfile(string name, string? description, string phone)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("food.restaurant.name_required", "Le nom de l'établissement est obligatoire."));
        }

        if (string.IsNullOrWhiteSpace(phone) || phone.Trim().Length is < 8 or > 20)
        {
            return Result.Failure(Error.Validation("food.restaurant.phone_required", "Le téléphone de l'établissement est obligatoire."));
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        // Le logo ne se règle plus par le profil : il se téléverse, et
        // `SetMediaCommand` le rattache. Mélanger les deux gestes ferait passer
        // une URL arbitraire là où l'on attend un média validé.
        Phone = phone.Trim();
        Touch();

        return Result.Success();
    }

    public Result AttachFulfillmentLocation(Guid fulfillmentLocationId)
    {
        if (fulfillmentLocationId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("food.restaurant.location_required", "Le lieu de collecte est obligatoire."));
        }

        FulfillmentLocationId = fulfillmentLocationId;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Fixe le délai de préparation annoncé.
    ///
    /// BORNÉ DES DEUX CÔTÉS. Un « 0 » promettrait un plat instantané et ferait
    /// partir le livreur avant que la cuisine ait commencé ; un « 600 » n'est pas
    /// une commande de restauration livrée mais une erreur de saisie, et
    /// l'acheteur verrait une heure de livraison absurde avant de payer.
    /// </summary>
    public Result SetPreparationTime(int minutes)
    {
        if (minutes is < MinPreparationMinutes or > MaxPreparationMinutes)
        {
            return Result.Failure(Error.Validation(
                "food.restaurant.preparation_invalid",
                $"Le délai de préparation doit être compris entre {MinPreparationMinutes} et {MaxPreparationMinutes} minutes."));
        }

        PreparationMinutes = minutes;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Remplace la grille de service.
    ///
    /// Remplacement et non ajout : un écran d'horaires se saisit en entier, et une
    /// méthode qui ajoute obligerait l'appelant à effacer d'abord — le jour où il
    /// l'oublierait, le restaurant afficherait deux grilles superposées et
    /// accepterait des commandes à des heures qu'il n'a pas choisies.
    /// </summary>
    public Result SetServiceHours(IReadOnlyList<ServiceHours> hours)
    {
        foreach (var (creneau, index) in hours.Select((h, i) => (h, i)))
        {
            if (hours.Where((_, j) => j != index).Any(creneau.Overlaps))
            {
                return Result.Failure(Error.Validation(
                    "food.restaurant.hours_overlap", $"Deux créneaux se chevauchent le {creneau.Day}."));
            }
        }

        _serviceHours.Clear();
        _serviceHours.AddRange(hours);
        Touch();

        return Result.Success();
    }

    // ── Cycle de vie ────────────────────────────────────────────────────────

    /// <summary>Le restaurateur soumet son établissement à la validation de HBA.</summary>
    public Result SubmitForApproval()
    {
        if (Status is not (RestaurantStatus.Draft or RestaurantStatus.Closed))
        {
            return Result.Failure(Error.Conflict(
                "food.restaurant.not_submittable", "Cet établissement n'est pas en attente de soumission."));
        }

        if (_serviceHours.Count == 0)
        {
            // Sans horaires, CanAcceptOrders refuserait TOUJOURS : le restaurant
            // serait validé, visible, et n'accepterait jamais rien. Le dire ici
            // vaut mieux que de le laisser découvrir par l'absence de commandes.
            return Result.Failure(Error.Conflict(
                "food.restaurant.hours_required", "Renseignez vos heures de service avant de soumettre l'établissement."));
        }

        if (FulfillmentLocationId is null)
        {
            return Result.Failure(Error.Conflict(
                "food.restaurant.location_required", "Renseignez l'adresse de collecte avant de soumettre l'établissement."));
        }

        // ENCAISSER SANS POUVOIR REVERSER EST LE PIRE DES ÉTATS.
        //
        // Il ne se voit pas tout de suite — les commandes passent, les clients sont
        // servis — et se découvre des semaines plus tard, quand le restaurateur
        // réclame son argent et qu'aucun chemin ne permet de le lui verser.
        //
        // CETTE GARDE VIENT EN DERNIER, ET C'EST DÉLIBÉRÉ.
        //
        // Placée avant celle du lieu de collecte, elle rendait « dossier de
        // reversement manquant » sur un établissement à qui il manquait AUSSI
        // l'adresse — masquant le premier motif derrière le second. Un message
        // d'erreur qui change d'objet selon l'ordre du code est un message qu'on
        // finit par ne plus lire.
        if (PayoutSellerId is null)
        {
            return Result.Failure(Error.Conflict(
                "food.restaurant.payout_required",
                "Rattachez un dossier vendeur validé avant de soumettre l'établissement : c'est lui qui recevra les recettes."));
        }

        Status = RestaurantStatus.PendingApproval;
        StatusReason = null;
        Touch();

        return Result.Success();
    }

    /// <summary>HBA valide l'établissement : il entre en service.</summary>
    public Result Approve()
    {
        if (Status != RestaurantStatus.PendingApproval)
        {
            return Result.Failure(Error.Conflict(
                "food.restaurant.not_pending", "Cet établissement n'attend pas de validation."));
        }

        Status = RestaurantStatus.Active;
        StatusReason = null;
        Touch();

        Raise(new RestaurantApprovedDomainEvent(Id.Value, OwnerUserId, Name));
        return Result.Success();
    }

    /// <summary>
    /// HBA refuse le dossier. Le motif est transmis au restaurateur : sans lui, il
    /// resoumet le même dossier et les deux s'épuisent.
    /// </summary>
    public Result Reject(string? reason)
    {
        if (Status != RestaurantStatus.PendingApproval)
        {
            return Result.Failure(Error.Conflict(
                "food.restaurant.not_pending", "Cet établissement n'attend pas de validation."));
        }

        Status = RestaurantStatus.Draft;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Touch();

        Raise(new RestaurantRejectedDomainEvent(Id.Value, OwnerUserId, StatusReason));
        return Result.Success();
    }

    /// <summary>
    /// L'exploitation écarte l'établissement. Ses commandes en cours ne sont PAS
    /// annulées ici — un plat déjà en préparation se termine, et un client déjà
    /// débité doit être livré ou remboursé, ce qui n'est pas la décision de cet
    /// agrégat.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// SEUL UN ÉTABLISSEMENT **EN SERVICE** PEUT ÊTRE SUSPENDU.
    ///
    /// Cette garde manquait, et son absence ouvrait un chemin de VALIDATION
    /// DÉTOURNÉE, pas seulement une incohérence de vocabulaire :
    ///
    ///     Draft → Suspend() → LiftSuspension() → **Active**
    ///
    /// `LiftSuspension` rend l'établissement ACTIF — au motif, écrit dans sa
    /// documentation, que « son dossier avait déjà été validé ». Sur un brouillon,
    /// c'était faux. Un dossier que personne n'a examiné devenait actif sans
    /// jamais passer par `Approve` : `RestaurantApprovedDomainEvent` n'était pas
    /// levé, le rôle FoodPartner n'était pas attribué, et l'établissement
    /// apparaissait dans la vitrine — avec une adresse et un téléphone que
    /// personne n'avait vérifiés.
    ///
    /// Le même détour ramenait en service un établissement FERMÉ, alors que
    /// `SubmitForApproval` exige précisément qu'un établissement fermé repasse par
    /// la validation.
    ///
    /// CHAQUE ÉTAT A DÉJÀ SON GESTE
    ///
    ///   • un dossier en attente qui pose problème se REFUSE — `Reject` porte un
    ///     motif que le restaurateur peut corriger ;
    ///   • un brouillon n'est visible de personne, il n'y a rien à écarter ;
    ///   • un établissement fermé est déjà parti.
    ///
    /// Suspendre est le geste qui RETIRE DU SERVICE. On ne retire pas du service
    /// ce qui n'y est pas.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result Suspend(string? reason)
    {
        // IDEMPOTENT : réappliquer une sanction déjà en vigueur n'est pas une
        // erreur de l'exploitation, et ne doit pas renotifier le restaurateur.
        if (Status == RestaurantStatus.Suspended)
        {
            return Result.Success();
        }

        if (Status != RestaurantStatus.Active)
        {
            // ÉCHEC, ET NON SUCCÈS SILENCIEUX : l'exploitation a cliqué sur le
            // mauvais bouton. Ne rien faire en répondant « c'est fait » la
            // laisserait croire l'établissement écarté.
            return Result.Failure(Error.Conflict(
                "food.restaurant.not_active",
                "Seul un établissement en service peut être suspendu. "
                + "Un dossier en attente se refuse, un brouillon n'est visible de personne."));
        }

        Status = RestaurantStatus.Suspended;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Touch();

        Raise(new RestaurantSuspendedDomainEvent(Id.Value, OwnerUserId, StatusReason));
        return Result.Success();
    }

    /// <summary>
    /// Lève la suspension. L'établissement redevient ACTIF, et non « en attente » :
    /// son dossier avait déjà été validé, et lui refaire passer la validation le
    /// punirait une seconde fois pour une sanction qu'on retire.
    ///
    /// CE « SON DOSSIER AVAIT DÉJÀ ÉTÉ VALIDÉ » N'EST PAS VÉRIFIÉ ICI — IL EST
    /// GARANTI PAR `Suspend`, QUI N'ACCEPTE QUE `Active`.
    ///
    /// C'est la seule chose qui empêche cette méthode d'être une porte d'entrée
    /// vers l'état actif. Elle l'a été : tant que n'importe quel statut pouvait
    /// être suspendu, un brouillon devenait actif en deux appels, sans validation
    /// et sans que `Approve` ne soit jamais franchi.
    ///
    /// Quiconque élargirait la garde de `Suspend` rouvrirait ce chemin, ici, sans
    /// toucher à cette méthode. Le test qui le pin s'appelle
    /// « Un_BROUILLON_ne_peut_pas_devenir_ACTIF_par_suspension_puis_levee ».
    /// </summary>
    public Result LiftSuspension()
    {
        if (Status != RestaurantStatus.Suspended)
        {
            return Result.Failure(Error.Conflict(
                "food.restaurant.not_suspended", "Cet établissement n'est pas suspendu."));
        }

        Status = RestaurantStatus.Active;
        StatusReason = null;
        Touch();

        Raise(new RestaurantReopenedDomainEvent(Id.Value, OwnerUserId));
        return Result.Success();
    }

    /// <summary>Le restaurateur quitte la plateforme.</summary>
    public Result Close(string? reason)
    {
        if (Status == RestaurantStatus.Suspended)
        {
            // Sans cette garde, un restaurateur sanctionné « fermerait » puis
            // resoumettrait son dossier : la suspension se contournerait par un
            // détour. Même défaut que celui corrigé côté vendeur.
            return Result.Failure(Error.Conflict(
                "food.restaurant.suspended", "Un établissement suspendu ne peut pas être fermé par son propriétaire."));
        }

        if (Status == RestaurantStatus.Closed)
        {
            return Result.Success();
        }

        Status = RestaurantStatus.Closed;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Touch();

        Raise(new RestaurantClosedDomainEvent(Id.Value, OwnerUserId));
        return Result.Success();
    }

    // ── Pause et disponibilité ──────────────────────────────────────────────

    /// <summary>
    /// Le restaurateur met le service en pause pour un temps court.
    ///
    /// BORNÉE DANS LE TEMPS, ET C'EST L'ESSENTIEL. Une pause sans échéance
    /// serait oubliée un soir de coup de feu, et l'établissement resterait
    /// invisible des jours durant sans que personne comprenne pourquoi. Elle
    /// expire d'elle-même.
    /// </summary>
    public Result PauseUntil(DateTime untilUtc, DateTime nowUtc)
    {
        if (untilUtc <= nowUtc)
        {
            return Result.Failure(Error.Validation(
                "food.restaurant.pause_invalid", "La fin de pause doit être dans le futur."));
        }

        if (untilUtc > nowUtc.AddHours(MaxPauseHours))
        {
            return Result.Failure(Error.Validation(
                "food.restaurant.pause_too_long",
                $"Une pause ne peut excéder {MaxPauseHours} h. Au-delà, fermez l'établissement : "
                + "vos clients sauront que vous ne servez pas aujourd'hui."));
        }

        PausedUntilUtc = untilUtc;
        Touch();
        return Result.Success();
    }

    /// <summary>Fin anticipée de la pause.</summary>
    public Result Resume()
    {
        PausedUntilUtc = null;
        Touch();
        return Result.Success();
    }

    /// <summary>Durée maximale d'une pause. Au-delà, c'est une fermeture.</summary>
    public const int MaxPauseHours = 12;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// PEUT-IL PRENDRE UNE COMMANDE, MAINTENANT ?
    ///
    /// LA MÉTHODE LA PLUS IMPORTANTE DE CET AGRÉGAT.
    ///
    /// Elle décide si un client peut payer. Se tromper dans un sens perd une
    /// commande ; se tromper dans l'autre encaisse un repas que personne ne
    /// préparera — et c'est celui-là qui coûte un remboursement, un avis, et un
    /// client.
    ///
    /// Elle rend un MOTIF et non un booléen : « indisponible » sur un écran de
    /// commande est la réponse la plus frustrante qui soit, parce qu'elle ne dit
    /// pas s'il faut revenir dans dix minutes, demain, ou jamais.
    ///
    /// L'HEURE EST PASSÉE, JAMAIS LUE ICI. Un domaine qui appelle
    /// <c>DateTime.UtcNow</c> ne se teste qu'en attendant l'heure qui l'intéresse.
    ///
    /// CETTE SURCHARGE NE REGARDE QUE L'ÉTABLISSEMENT — pas sa carte.
    ///
    /// Elle ne peut donc JAMAIS rendre <c>NothingAvailable</c> : les articles sont
    /// un autre agrégat. Pour la réponse complète, voir la surcharge à deux
    /// paramètres. Celle-ci reste utile là où interroger la carte serait circulaire
    /// — un article est commandable si le restaurant sert, le restaurant sert s'il
    /// reste un article commandable.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public OrderingBlockedReason CanAcceptOrders(DateTime nowUtc)
    {
        if (Status != RestaurantStatus.Active)
        {
            return OrderingBlockedReason.NotInService;
        }

        // La pause l'emporte sur les horaires : elle est déclarée PENDANT le
        // service, et c'est justement à ce moment qu'elle doit valoir.
        if (PausedUntilUtc is { } fin && fin > nowUtc)
        {
            return OrderingBlockedReason.TemporarilyPaused;
        }

        return IsWithinServiceHours(nowUtc)
            ? OrderingBlockedReason.None
            : OrderingBlockedReason.OutsideServiceHours;
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA RÉPONSE COMPLÈTE : L'ÉTABLISSEMENT **ET** SA CARTE.
    ///
    /// POURQUOI CETTE SURCHARGE EXISTE
    ///
    /// <c>NothingAvailable</c> était déclaré dans l'énumération, documenté
    /// — « aucun article disponible : le menu entier est épuisé » — et AUCUN
    /// chemin ne pouvait le rendre. La méthode qui rend ce type ne voit pas les
    /// articles. La valeur promettait une distinction que le code ne savait pas
    /// faire.
    ///
    /// LA SITUATION EST RÉELLE, ET FRÉQUENTE
    ///
    /// 21 h, le maquis est ouvert, dans ses horaires, sans pause déclarée — et il
    /// n'a plus rien. Sans cette valeur, l'écran affiche « Ouvert », le client
    /// parcourt une carte vide ou grisée, choisit quand même, et son panier est
    /// refusé sans qu'il comprenne. « Tout est épuisé pour ce soir » se comprend
    /// du premier coup d'œil.
    ///
    /// LE FAIT VIENT DE L'APPELANT, PAS D'UN APPEL CACHÉ
    ///
    /// Cet agrégat ne charge rien : les articles sont un autre agrégat, et lui
    /// donner un dépôt en ferait un service déguisé. L'appelant, qui voit les deux,
    /// fournit le seul fait manquant.
    ///
    /// L'ORDRE DES MOTIFS N'EST PAS ARBITRAIRE
    ///
    /// Un restaurant FERMÉ dont la carte est vide répond « fermé », pas « épuisé ».
    /// C'est le motif actionnable : « revenez demain » aide, « tout est épuisé »
    /// laisse croire qu'il suffirait d'attendre le prochain plat.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <param name="hasOrderableItem">
    /// Reste-t-il au moins UN article commandable ? À l'appelant de l'établir en
    /// n'écartant ni les sections masquées ni les options épuisées — voir
    /// <c>MenuItem.IsOrderableAt</c>.
    /// </param>
    public OrderingBlockedReason CanAcceptOrders(DateTime nowUtc, bool hasOrderableItem)
    {
        var raison = CanAcceptOrders(nowUtc);
        if (raison != OrderingBlockedReason.None)
        {
            return raison;
        }

        return hasOrderableItem ? OrderingBlockedReason.None : OrderingBlockedReason.NothingAvailable;
    }

    /// <summary>
    /// L'instant tombe-t-il dans un créneau de service ?
    ///
    /// COMPARAISON EN HEURE LOCALE DU BÉNIN (UTC+1, sans heure d'été).
    ///
    /// Les créneaux sont saisis par le restaurateur dans SON heure : « j'ouvre à
    /// 11 h ». Les comparer à une heure UTC décalerait tout le service d'une
    /// heure — le restaurant refuserait des commandes à 11 h et en accepterait à
    /// 23 h. Le Bénin n'a pas d'heure d'été, ce décalage est donc constant.
    /// </summary>
    private bool IsWithinServiceHours(DateTime nowUtc)
    {
        var local = BeninTime.ToLocal(nowUtc);
        var heure = TimeOnly.FromDateTime(local);

        // ═════════════════════════════════════════════════════════════════════
        // L'EXCEPTION DATÉE PRIME SUR L'HORAIRE HEBDOMADAIRE, ET C'EST TOUT
        // SON INTÉRÊT.
        //
        // La consulter APRÈS le créneau hebdomadaire, ou la combiner par un « et »,
        // reviendrait à ne rien faire : un restaurant déclaré fermé le 1er août
        // resterait ouvert parce que son vendredi l'est. Elle REMPLACE la règle
        // du jour, elle ne s'y ajoute pas.
        //
        // Et c'est un retour anticipé, pas un filtre : une exception « fermé »
        // ferme, une exception avec horaires impose les siens. Dans les deux cas,
        // le créneau hebdomadaire n'est plus consulté du tout.
        // ═════════════════════════════════════════════════════════════════════
        var exception = _specialHours.FirstOrDefault(e => e.Date == DateOnly.FromDateTime(local));
        if (exception is not null)
        {
            return exception.Covers(heure);
        }

        return _serviceHours.Any(c => c.Covers(local.DayOfWeek, heure));
    }

    /// <summary>Décalage horaire du Bénin (UTC+1), constant : pas d'heure d'été.</summary>
    /// <summary>
    /// CONSERVÉE POUR LES APPELANTS EXISTANTS, MAIS LA SOURCE EST AILLEURS.
    ///
    /// Le décalage vit dans <see cref="BeninTime"/> depuis que les créneaux de
    /// carte en ont besoin eux aussi. Deux constantes séparées se seraient
    /// contredites au premier changement — et le symptôme aurait été un menu du
    /// midi servi à la bonne heure et une commande refusée à la mauvaise.
    /// </summary>
    public const int BeninUtcOffsetHours = BeninTime.UtcOffsetHours;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// QUAND FINIT « AUJOURD'HUI » POUR CE RESTAURANT ?
    ///
    /// C'est l'échéance d'un « épuisé pour aujourd'hui » : le plat revient de
    /// lui-même à cet instant. Sans cette réponse, la disponibilité redeviendrait
    /// un booléen que personne ne relève.
    ///
    /// CE N'EST PAS SIMPLEMENT MINUIT.
    ///
    /// Un maquis ouvert le vendredi de 19 h à 23 h 59, puis le samedi de 0 h à
    /// 2 h, sert d'une traite : pour le cuisinier comme pour le client, c'est UNE
    /// soirée. Rendre le plat à minuit le ferait réapparaître au milieu du
    /// service, alors qu'il n'y a toujours pas de poisson en cuisine.
    ///
    /// On avance donc jusqu'au minuit suivant, PUIS on prolonge tant qu'un
    /// créneau démarre exactement à 0 h — la façon dont un service qui passe
    /// minuit est nécessairement saisi ici, puisqu'un créneau ne peut pas
    /// enjamber la date (voir ServiceHours).
    ///
    /// LE PLAT REVIENT DONC AVANT LE PROCHAIN SERVICE, ET JAMAIS PENDANT CELUI-CI.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public DateTime EndOfServiceDayUtc(DateTime nowUtc)
    {
        var local = BeninTime.ToLocal(nowUtc);

        // Minuit suivant, en heure locale.
        var fin = local.Date.AddDays(1);

        // Puis on absorbe LA queue du service : un créneau qui commence à 0 h pile
        // le lendemain est la seconde moitié d'une soirée commencée la veille.
        //
        // UNE SEULE FOIS, ET JAMAIS SUR UN CRÉNEAU QUI COUVRE TOUTE LA JOURNÉE.
        //
        // La première version enchaînait les prolongations tant qu'un créneau
        // commençait à minuit. Sur un établissement déclaré ouvert 0 h – 24 h tous
        // les jours, elle repoussait le retour du plat d'UNE SEMAINE : le plat
        // épuisé un mercredi midi ne serait revenu que le mercredi suivant.
        //
        // Un service qui passe minuit n'a qu'une queue. Et pour un restaurant
        // ouvert en continu, il n'existe aucune frontière de service — minuit est
        // alors la réponse la moins fausse.
        var queue = _serviceHours.FirstOrDefault(c =>
            c.Day == fin.DayOfWeek
            && c.OpensAt == TimeOnly.MinValue
            && c.ClosesAt != TimeOnly.MaxValue);

        if (queue is not null)
        {
            fin = fin.Date.Add(queue.ClosesAt.ToTimeSpan());
        }

        return BeninTime.ToUtc(fin);
    }

    /// <summary>
    /// Rattache le dossier vendeur qui encaissera les recettes.
    ///
    /// CETTE MÉTHODE NE VÉRIFIE NI L'EXISTENCE NI LA VALIDATION DU DOSSIER.
    ///
    /// Food ne connaît pas Sellers. C'est l'appelant — la couche qui voit les
    /// deux — qui contrôle que le dossier existe, qu'il appartient bien au
    /// propriétaire de l'établissement, et qu'il porte un compte de reversement.
    /// Sans ce contrôle en amont, on rattacherait le dossier d'un tiers et ses
    /// recettes partiraient sur le compte de quelqu'un d'autre.
    /// </summary>
    public Result AttachPayoutSeller(Guid sellerId)
    {
        if (sellerId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "food.restaurant.payout_seller_required", "Le dossier vendeur est obligatoire."));
        }

        // ON NE CHANGE PAS DE COMPTE ENCAISSEUR EN PLEIN SERVICE.
        //
        // Les gains déjà comptabilisés portent l'ANCIEN dossier ; les suivants
        // iraient au nouveau. Le solde d'un restaurant se retrouverait réparti sur
        // deux comptes, dont un que plus rien ne désigne — et le rapprochement
        // deviendrait impossible sans relire l'historique des modifications, qui
        // n'existe pas.
        //
        // Un changement de compte est une opération d'exploitation : elle suppose
        // de solder les gains en cours, donc de mettre l'établissement en pause.
        if (PayoutSellerId is { } actuel && actuel != sellerId && Status != RestaurantStatus.Draft)
        {
            return Result.Failure(Error.Conflict(
                "food.restaurant.payout_seller_locked",
                "Mettez l'établissement en pause avant de changer le compte qui reçoit les recettes."));
        }

        PayoutSellerId = sellerId;
        Touch();
        return Result.Success();
    }

    private void Touch() => UpdatedOnUtc = DateTime.UtcNow;
}

/// <summary>Accès aux établissements.</summary>
public interface IRestaurantRepository
{
    Task<Restaurant?> GetByIdAsync(RestaurantId id, CancellationToken cancellationToken = default);

    /// <summary>L'établissement d'un restaurateur, résolu depuis son compte HBA.</summary>
    Task<Restaurant?> GetByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Restaurant>> ListByStatusAsync(
        RestaurantStatus status, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// La VITRINE : les établissements qu'un client a le droit de voir, paginés.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE FILTRE EST DANS LE DÉPÔT, PAS CHEZ L'APPELANT.
    ///
    /// <c>ListByStatusAsync</c> laisse l'appelant choisir le statut : c'est
    /// correct pour la file de validation, qui doit voir des dossiers en attente.
    /// Ce serait dangereux ici. Une vitrine dont le filtre est passé en paramètre
    /// finit, un jour, par recevoir le mauvais paramètre — et expose des dossiers
    /// suspendus ou jamais validés à des clients.
    ///
    /// La règle est donc figée dans la signature : cette méthode ne peut rendre
    /// QUE des établissements actifs. Elle correspond exactement à
    /// <see cref="Restaurant.IsPubliclyVisible"/>, et les deux doivent évoluer
    /// ensemble.
    ///
    /// UN ÉTABLISSEMENT FERMÉ CE SOIR RESTE DANS LA VITRINE.
    ///
    /// « Visible » n'est pas « accepte des commandes ». Le client consulte la
    /// carte et reviendra demain. Filtrer sur les horaires ici viderait
    /// l'application chaque nuit.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    Task<IReadOnlyList<Restaurant>> ListPubliclyVisibleAsync(
        int skip, int take, CancellationToken cancellationToken = default);

    Task AddAsync(Restaurant restaurant, CancellationToken cancellationToken = default);
}
