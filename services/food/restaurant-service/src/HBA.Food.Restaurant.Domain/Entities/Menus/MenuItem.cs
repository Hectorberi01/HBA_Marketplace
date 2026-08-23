using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Menus;

public readonly record struct MenuItemId(Guid Value)
{
    public static MenuItemId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Ce qu'une sélection d'options coûte, et ce qu'elle contient.
///
/// Le LIBELLÉ des options est repris ici parce que le panier et la commande
/// doivent garder ce que le client a choisi AU MOMENT du choix. Le restaurateur
/// renommera « Piment » en « Piment fort » un jour ; la commande d'hier doit
/// continuer de dire ce que le client avait sous les yeux.
/// </summary>
public sealed record PricedSelection(
    decimal UnitPrice,
    string Currency,
    IReadOnlyList<SelectedOption> Options);

/// <summary>Une option retenue, figée telle qu'elle était au moment du choix.</summary>
public sealed record SelectedOption(Guid OptionId, string GroupName, string OptionName, decimal PriceDelta);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN ARTICLE DE LA CARTE.
///
/// Agrégat racine qui possède ses groupes d'options : valider le panier d'un
/// client, c'est charger UN article et lui soumettre une sélection.
///
/// LE PRIX EST CALCULÉ ICI, JAMAIS TRANSMIS PAR L'APPELANT.
///
/// C'est la même règle que pour le prix acheteur d'une offre marketplace, et pour
/// la même raison : un prix qui voyage depuis le client est un prix qu'on peut
/// réécrire. Le panier envoie des IDENTIFIANTS d'options ; le montant sort d'ici.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class MenuItem : AggregateRoot<MenuItemId>
{
    private readonly List<OptionGroup> _optionGroups = new();

    private MenuItem()
    {
    }

    private MenuItem(MenuItemId id, Guid restaurantId, Guid menuCategoryId, string name, Money basePrice)
        : base(id)
    {
        RestaurantId = restaurantId;
        MenuCategoryId = menuCategoryId;
        Name = name;
        BasePrice = basePrice;
        Availability = ItemAvailability.Available();
        CreatedOnUtc = DateTime.UtcNow;
    }

    public Guid RestaurantId { get; private set; }

    /// <summary>
    /// Section de rattachement. Un simple identifiant : la section n'est pas le parent.
    ///
    /// C'EST LA SECTION, PAS LA CARTE. L'article ne connaît pas le menu du midi
    /// ni celui du soir — il connaît « Plats », et c'est « Plats » qui appartient à
    /// une carte. Déplacer une section d'une carte à l'autre emporte donc ses
    /// articles sans qu'aucune ligne d'article ne change.
    /// </summary>
    public Guid MenuCategoryId { get; private set; }

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    /// <summary>
    /// La photo du plat, PAR RÉFÉRENCE au service média (§6).
    ///
    /// Un identifiant, pas une URL — voir <c>Restaurant.LogoMediaId</c> pour le
    /// raisonnement complet. La couche qui voit Food ET Media résout l'adresse.
    /// </summary>
    public Guid? ImageMediaId { get; private set; }

    /// <summary>TRANSITOIRE : l'URL d'avant la bascule. À supprimer après reversement.</summary>
    public string? LegacyImageUrl { get; private set; }

    /// <summary>
    /// L'adresse publique de <see cref="ImageMediaId"/>, recopiée au rattachement.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// OUI, C'EST UNE DÉNORMALISATION, ET ELLE EST DÉLIBÉRÉE.
    ///
    /// Le commentaire d'`ImageMediaId` dit « la couche qui voit Food ET Media
    /// résout l'adresse ». Cette couche N'EXISTAIT PAS : `AddMediaGrpcClient`, qui
    /// brancherait `IMediaModuleApi` sur media-service, n'avait aucun appelant, et
    /// aucune carte n'a jamais affiché de photo. La règle était juste et personne ne
    /// l'appliquait — le pire des deux états.
    ///
    /// L'appliquer maintenant coûterait, sur la LECTURE LA PLUS CHAUDE DE L'APP —
    /// la vitrine d'un restaurant, appelée sans compte, à chaque ouverture — un
    /// aller-retour gRPC par carte, et ferait dépendre l'affichage d'un menu de la
    /// disponibilité de media-service. Une photo indisponible ferait alors
    /// disparaître le plat.
    ///
    /// C'EST LE PATRON DÉJÀ EN PLACE AILLEURS, PAS UNE EXCEPTION.
    ///
    /// `ProductMedia.Url` (catalog) et `Seller.LogoUrl` (merchants) font exactement
    /// cela, et le premier le documente dans les mêmes termes : « UNE COPIE DE
    /// LECTURE, écrite au moment du dépôt ». Food était l'outsider.
    ///
    /// ELLE PEUT DEVENIR OBSOLÈTE, ET `ImageMediaId` RESTE LA VÉRITÉ.
    ///
    /// Bucket renommé, `PublicBaseUrl` réécrite en configuration : cette colonne
    /// pointera vers le vide. C'est pourquoi <see cref="HasImage"/> se prononce sur
    /// le `mediaId`, JAMAIS sur cette URL — sinon une réécriture de configuration
    /// retirerait de la vente toute la restauration d'un coup. Catalog a
    /// `RefreshMediaUrlCommand` pour ce cas ; Food l'aura le jour où il arrivera.
    ///
    /// ELLE NE S'APPELLE PAS `ImageUrl`, ET C'EST POUR NE PAS MENTIR À
    ///    L'HISTORIQUE.
    ///
    /// La migration `ImagesVersMedia` a RENOMMÉ `ImageUrl` en `LegacyImageUrl`.
    /// Réintroduire une colonne `ImageUrl` porteuse d'un autre sens ferait lire
    /// cette migration comme annulée — alors que les deux colonnes coexistent et
    /// disent des choses différentes : l'une l'adresse d'avant la bascule, l'autre
    /// celle d'un média repris. « Public » dit en plus ce qu'elle n'est jamais :
    /// une URL signée, qui expirerait en base.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public string? ImagePublicUrl { get; private set; }

    /// <summary>Prix sans option. Les options s'y ajoutent en écart.</summary>
    public Money BasePrice { get; private set; } = default!;

    /// <summary>
    /// Disponibilité de l'article.
    ///
    /// CE N'EST PAS DU STOCK, et c'est pourquoi Inventory ne s'en mêle pas. Un
    /// plat ne « décrémente » pas : le cuisinier sait qu'il n'a plus de poisson et
    /// le retire pour la journée. Le lendemain, il revient — sans qu'aucun
    /// réapprovisionnement n'ait été saisi, et SANS que personne ait à y penser.
    ///
    /// C'est ce « sans que personne ait à y penser » que le booléen initial ne
    /// tenait pas. Voir ItemAvailability.
    /// </summary>
    public ItemAvailability Availability { get; private set; } = ItemAvailability.Available();

    public int DisplayOrder { get; private set; }

    /// <summary>
    /// Temps de préparation propre à ce plat, en minutes (cahier §6). Nul = celui
    /// du restaurant.
    ///
    /// C'EST LUI QUI DONNE L'ETA D'UNE COMMANDE, et le cahier (§14) est
    /// explicite : « ETA = MAX(temps des articles) + attente ». Le maximum, pas la
    /// somme — les plats se préparent en parallèle, pas à la file. Sommer
    /// annoncerait une heure et demie pour trois plats de trente minutes, et le
    /// client irait ailleurs.
    /// </summary>
    public int? PreparationMinutes { get; private set; }

    /// <summary>
    /// Poste de préparation (§9) : GRILL, PIZZA, DRINKS. Nul = aucun poste
    /// particulier.
    ///
    /// Simple identifiant, pas de navigation : c'est ce qui permet à l'écran de
    /// cuisine de découper une commande par poste sans charger la carte.
    /// </summary>
    public Guid? PreparationStationId { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }

    public IReadOnlyCollection<OptionGroup> OptionGroups => _optionGroups.AsReadOnly();

    public static Result<MenuItem> Create(
        Guid restaurantId, Guid menuCategoryId, string name, decimal basePrice, string currency = "XOF")
    {
        if (restaurantId == Guid.Empty || menuCategoryId == Guid.Empty)
        {
            return Error.Validation("food.item.parent_required", "L'article doit appartenir à un restaurant et à une section.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("food.item.name_required", "Le nom de l'article est obligatoire.");
        }

        if (basePrice < 0m)
        {
            return Error.Validation("food.item.price_invalid", "Le prix ne peut pas être négatif.");
        }

        var prix = Money.Create(basePrice, currency);
        if (prix.IsFailure)
        {
            return prix.Error;
        }

        return new MenuItem(MenuItemId.New(), restaurantId, menuCategoryId, name.Trim(), prix.Value);
    }

    /// <param name="displayOrder">
    /// Le rang d'affichage, ou <c>null</c> pour NE PAS Y TOUCHER.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// IL ÉTAIT `int`, ET RENOMMER UN PLAT REMETTAIT SON RANG À ZÉRO.
    ///
    /// `MenuItemView` n'expose pas `DisplayOrder` — aucune application ne peut donc
    /// le relire pour le renvoyer inchangé. Toutes envoyaient la valeur par défaut,
    /// c'est-à-dire 0. Corriger une faute de frappe dans « Poulet braisé » le
    /// propulsait en tête de section, et le restaurateur devait réordonner sa carte
    /// à chaque correction — sans jamais faire le lien entre les deux.
    ///
    /// ON REND LE PARAMÈTRE NULLABLE PLUTÔT QUE D'EXPOSER `DisplayOrder`.
    ///
    /// Exposer le champ marcherait, et laisserait le défaut possible : il suffirait
    /// qu'un appelant oublie de le renvoyer. « Null = inchangé » rend l'oubli
    /// INOFFENSIF, ce qui est la seule garantie qui tient dans le temps.
    ///
    /// Le réordonnancement, lui, aura sa propre route quand l'écran existera —
    /// comme `PUT .../categories/{id}/position` pour les sections. Ce n'est pas le
    /// travail d'une mise à jour de libellé.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </param>
    public Result UpdateDetails(string name, string? description, int? displayOrder = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("food.item.name_required", "Le nom de l'article est obligatoire."));
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        if (displayOrder is { } rang)
        {
            DisplayOrder = rang;
        }

        Touch();

        return Result.Success();
    }

    public Result ChangePrice(decimal basePrice)
    {
        if (basePrice < 0m)
        {
            return Result.Failure(Error.Validation("food.item.price_invalid", "Le prix ne peut pas être négatif."));
        }

        var prix = Money.Create(basePrice, BasePrice.Currency);
        if (prix.IsFailure)
        {
            return Result.Failure(prix.Error);
        }

        BasePrice = prix.Value;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Rattache la photo du plat (§6).
    ///
    /// L'appartenance du média est vérifiée par l'appelant : ce module ne
    /// connaît pas le service média.
    /// </summary>
    /// <param name="imagePublicUrl">
    /// L'adresse publique du média, telle que le service média l'a rendue au dépôt.
    ///
    /// LES DEUX ENSEMBLE, JAMAIS L'UN SANS L'AUTRE. Un `mediaId` sans URL donne
    /// un plat dont on sait qu'il a une photo et qu'on ne peut pas afficher ; une
    /// URL sans `mediaId` donne une photo que `HasImage` ignore, donc un plat
    /// invendable qui paraît complet. C'est la signature qui l'impose.
    /// </param>
    public Result SetImage(Guid? imageMediaId, string? imagePublicUrl)
    {
        ImageMediaId = imageMediaId == Guid.Empty ? null : imageMediaId;

        if (ImageMediaId is null)
        {
            // Retirer la photo retire AUSSI son adresse. La laisser afficherait
            // encore l'image d'un plat que le domaine tient pour sans photo — et
            // `IsOrderableAt` le refuserait à la vente sans que rien ne l'explique.
            ImagePublicUrl = null;
        }
        else
        {
            LegacyImageUrl = null;
            ImagePublicUrl = string.IsNullOrWhiteSpace(imagePublicUrl)
                ? null
                : imagePublicUrl.Trim();
        }

        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Fixe le temps et le poste de préparation (§6, §9).
    ///
    /// LES DEUX ENSEMBLE, parce qu'ils se règlent au même moment et sur le même
    /// écran : « ce plat sort du grill en 12 minutes ». Les séparer en deux
    /// commandes ferait deux appels pour un seul geste, et l'un des deux serait
    /// oublié la moitié du temps.
    ///
    /// L'EXISTENCE DU POSTE N'EST PAS VÉRIFIÉE ICI : il est un autre agrégat.
    /// C'est l'appelant qui s'en charge — sans quoi un article partirait vers un
    /// poste inexistant et son ticket n'apparaîtrait sur aucun écran.
    /// </summary>
    public Result SetPreparation(int? minutes, Guid? preparationStationId)
    {
        if (minutes is { } valeur && valeur is < MinPreparationMinutes or > MaxPreparationMinutes)
        {
            return Result.Failure(Error.Validation(
                "food.item.preparation_invalid",
                $"Le temps de préparation doit être compris entre {MinPreparationMinutes} et {MaxPreparationMinutes} minutes."));
        }

        PreparationMinutes = minutes;
        PreparationStationId = preparationStationId == Guid.Empty ? null : preparationStationId;
        Touch();

        return Result.Success();
    }

    /// <summary>Une minute pour une boisson, trois heures au grand maximum pour un plat mijoté.</summary>
    public const int MinPreparationMinutes = 1;
    public const int MaxPreparationMinutes = 180;

    /// <summary>
    /// Déplace l'article vers une autre SECTION.
    ///
    /// PAS VERS UNE AUTRE CARTE : l'article ne connaît pas les cartes. Pour
    /// faire passer un plat du menu du midi à celui du soir, on le déplace vers
    /// une section rattachée à la carte du soir — ou l'on déplace la section
    /// entière, ce qui emporte tous ses articles d'un coup.
    /// </summary>
    public Result MoveToCategory(Guid menuCategoryId)
    {
        if (menuCategoryId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("food.item.parent_required", "Section de destination requise."));
        }

        MenuCategoryId = menuCategoryId;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Le plat est épuisé JUSQU'À une échéance, et revient tout seul.
    ///
    /// L'échéance est calculée par l'appelant à partir du restaurant — voir
    /// <c>Restaurant.EndOfServiceDayUtc</c>. Cet agrégat ne connaît pas les
    /// horaires de service ; il tient la promesse, il ne la calcule pas.
    /// </summary>
    public Result MarkUnavailableUntil(DateTime untilUtc, DateTime nowUtc)
    {
        var etat = ItemAvailability.UntilUtc(untilUtc, nowUtc);
        if (etat.IsFailure)
        {
            return Result.Failure(etat.Error);
        }

        Availability = etat.Value;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Le plat est retiré de la carte jusqu'à nouvel ordre.
    ///
    /// À DISTINGUER DE « épuisé aujourd'hui » : celui-ci NE REVIENDRA PAS seul.
    /// C'est la décision d'arrêter un plat, pas le constat d'une rupture.
    /// </summary>
    public Result MarkUnavailableIndefinitely()
    {
        Availability = ItemAvailability.Indefinitely();
        Touch();
        return Result.Success();
    }

    /// <summary>Le plat revient à la carte.</summary>
    public Result MarkAvailable()
    {
        Availability = ItemAvailability.Available();
        Touch();
        return Result.Success();
    }

    // ── Groupes d'options ───────────────────────────────────────────────────

    public Result<Guid> AddOptionGroup(string name, int minSelections, int maxSelections, int displayOrder = 0)
    {
        var groupe = OptionGroup.Create(name, minSelections, maxSelections, displayOrder);
        if (groupe.IsFailure)
        {
            return groupe.Error;
        }

        _optionGroups.Add(groupe.Value);
        Touch();
        return groupe.Value.Id;
    }

    public Result RemoveOptionGroup(Guid groupId)
    {
        var groupe = _optionGroups.FirstOrDefault(g => g.Id == groupId);
        if (groupe is null)
        {
            return Result.Failure(Error.NotFound("food.option_group.not_found", "Groupe d'options introuvable."));
        }

        _optionGroups.Remove(groupe);
        Touch();
        return Result.Success();
    }

    public Result<Guid> AddOption(Guid groupId, string name, decimal priceDelta)
    {
        var groupe = _optionGroups.FirstOrDefault(g => g.Id == groupId);
        if (groupe is null)
        {
            return Error.NotFound("food.option_group.not_found", "Groupe d'options introuvable.");
        }

        var option = groupe.AddOption(name, priceDelta);
        if (option.IsFailure)
        {
            return option.Error;
        }

        Touch();
        return option.Value.Id;
    }

    public Result RemoveOption(Guid groupId, Guid optionId)
    {
        var groupe = _optionGroups.FirstOrDefault(g => g.Id == groupId);
        if (groupe is null)
        {
            return Result.Failure(Error.NotFound("food.option_group.not_found", "Groupe d'options introuvable."));
        }

        var result = groupe.RemoveOption(optionId);
        if (result.IsSuccess)
        {
            Touch();
        }

        return result;
    }

    public Result SetOptionAvailability(Guid groupId, Guid optionId, ItemAvailability availability)
    {
        var groupe = _optionGroups.FirstOrDefault(g => g.Id == groupId);
        if (groupe is null)
        {
            return Result.Failure(Error.NotFound("food.option_group.not_found", "Groupe d'options introuvable."));
        }

        var result = groupe.SetOptionAvailability(optionId, availability);
        if (result.IsSuccess)
        {
            Touch();
        }

        return result;
    }

    /// <summary>
    /// Cet article est-il commandable aujourd'hui ?
    ///
    /// « DISPONIBLE » NE SUFFIT PAS. Un plat servable dont toutes les tailles
    /// sont épuisées n'est pas commandable : le client choisirait, verrait son
    /// panier refusé, et ne comprendrait pas — l'écran lui disait que le plat
    /// était là.
    /// </summary>
    public bool IsOrderableAt(DateTime nowUtc)
        => HasImage
        && Availability.IsAvailableAt(nowUtc)
        && _optionGroups.All(g => g.CanBeSatisfiedAt(nowUtc));

    /// <summary>
    /// L'article porte-t-il une photo ?
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA PHOTO EST OBLIGATOIRE POUR VENDRE, ET C'EST ICI QUE ÇA SE DÉCIDE.
    ///
    /// Elle pourrait être exigée dans `Create` : ce serait plus simple, et faux à
    /// deux titres.
    ///
    /// D'abord, cela invaliderait tous les articles DÉJÀ créés sans image — non pas
    /// en les bloquant, mais en rendant impossible toute modification ultérieure
    /// d'un plat que le restaurateur voit pourtant dans sa carte. Un domaine ne doit
    /// pas rendre inaccessible ce qu'il a lui-même accepté hier.
    ///
    /// Ensuite, cela ne garantirait rien : `SetImage(null)` retire la photo, et un
    /// article créé conforme redeviendrait vendable sans image la minute suivante.
    /// Une règle posée à la seule création est une règle qu'on peut défaire.
    ///
    /// Placée dans `IsOrderableAt`, l'obligation devient VRAIE en permanence :
    /// aucun article sans photo n'entre dans un panier, n'apparaît sur la vitrine,
    /// ni ne se facture. La vérification est refaite à chaque lecture et à chaque
    /// mise au panier, pas une fois pour toutes.
    ///
    /// `LegacyImageUrl` COMPTE COMME UNE PHOTO.
    ///
    /// Les articles importés du monolithe portent une URL et non un `mediaId`. Les
    /// exclure retirerait de la vente, du jour au lendemain, toute la carte des
    /// restaurateurs migrés — pour une raison technique qui ne les concerne pas.
    /// Ils resteront vendables jusqu'à ce que leur média soit repris.
    ///
    /// CE N'EST PAS UNE INDISPONIBILITÉ, ET L'ESPACE RESTAURATEUR DOIT LE DIRE.
    ///
    /// Un plat « épuisé aujourd'hui » se rétablit demain tout seul ; un plat sans
    /// photo attend un geste. Les afficher tous deux comme « indisponible » ferait
    /// attendre un restaurateur qui devrait agir. Voir `MenuItemView.IsOrderable`,
    /// que l'application doit désormais accompagner d'un motif.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public bool HasImage => ImageMediaId is not null
        || !string.IsNullOrWhiteSpace(LegacyImageUrl);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// VALIDE UNE SÉLECTION ET EN CALCULE LE PRIX.
    ///
    /// C'est la méthode que le panier appelle avant d'accepter une ligne, et que
    /// la commande rappelle avant de facturer. Tout se joue ici.
    ///
    /// TOUTES LES ERREURS D'UN COUP, PAS LA PREMIÈRE.
    ///
    /// Un client à qui l'on dit « choisissez une taille », puis, après correction,
    /// « choisissez une sauce », puis « le supplément fromage est épuisé »,
    /// abandonne. Le même raisonnement que la validation des attributs produit.
    ///
    /// LES OPTIONS D'UN AUTRE ARTICLE SONT REFUSÉES.
    ///
    /// Les identifiants viennent du client. Sans ce contrôle, on accepterait
    /// l'option « −2 000 F » d'un autre plat et le client paierait ce qu'il veut.
    /// C'est la raison d'être du parcours par groupe plutôt que d'une simple
    /// somme sur les identifiants reçus.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result<PricedSelection> PriceSelection(IReadOnlyCollection<Guid> selectedOptionIds, DateTime nowUtc)
    {
        if (!Availability.IsAvailableAt(nowUtc))
        {
            return Error.Conflict("food.item.unavailable", $"« {Name} » n'est pas disponible aujourd'hui.");
        }

        var erreurs = new List<string>();
        var retenues = new List<SelectedOption>();
        var reconnues = new HashSet<Guid>();

        foreach (var groupe in _optionGroups.OrderBy(g => g.DisplayOrder))
        {
            var choisies = groupe.Options.Where(o => selectedOptionIds.Contains(o.Id)).ToList();

            foreach (var option in choisies)
            {
                reconnues.Add(option.Id);
            }

            if (choisies.Count < groupe.MinSelections)
            {
                erreurs.Add(groupe.MinSelections == 1
                    ? $"« {groupe.Name} » : un choix est obligatoire."
                    : $"« {groupe.Name} » : au moins {groupe.MinSelections} choix sont obligatoires.");
                continue;
            }

            if (choisies.Count > groupe.MaxSelections)
            {
                erreurs.Add(groupe.MaxSelections == 1
                    ? $"« {groupe.Name} » : un seul choix est possible."
                    : $"« {groupe.Name} » : {groupe.MaxSelections} choix au maximum.");
                continue;
            }

            var epuisees = choisies.Where(o => !o.Availability.IsAvailableAt(nowUtc)).ToList();
            if (epuisees.Count > 0)
            {
                erreurs.Add($"« {groupe.Name} » : {string.Join(", ", epuisees.Select(o => o.Name))} — plus disponible aujourd'hui.");
                continue;
            }

            retenues.AddRange(choisies.Select(o => new SelectedOption(o.Id, groupe.Name, o.Name, o.PriceDelta)));
        }

        // LES IDENTIFIANTS INCONNUS SONT UNE ERREUR, PAS UN SILENCE.
        //
        // Les ignorer laisserait passer l'option d'un autre plat, ou une option
        // supprimée depuis que le client a ouvert son écran. Dans les deux cas il
        // paierait pour autre chose que ce qu'il croit avoir commandé.
        var inconnues = selectedOptionIds.Where(id => !reconnues.Contains(id)).ToList();
        if (inconnues.Count > 0)
        {
            erreurs.Add("Certaines options choisies n'existent plus sur cet article. Rechargez la carte.");
        }

        if (erreurs.Count > 0)
        {
            return Error.Validation("food.item.selection_invalid", string.Join(" ", erreurs));
        }

        var total = BasePrice.Amount + retenues.Sum(o => o.PriceDelta);

        if (total < 0m)
        {
            // Un cumul de remises ne rend pas un plat gratuit ni payant pour le
            // restaurant. Signe d'une carte mal saisie — on refuse plutôt que
            // d'encaisser un montant négatif.
            return Error.Conflict(
                "food.item.price_negative",
                $"Le prix de « {Name} » avec ces options tomberait sous zéro. Contactez le restaurant.");
        }

        return new PricedSelection(total, BasePrice.Currency, retenues);
    }

    private void Touch() => UpdatedOnUtc = DateTime.UtcNow;
}

/// <summary>Accès aux articles de la carte.</summary>
public interface IMenuItemRepository
{
    Task<MenuItem?> GetByIdAsync(MenuItemId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MenuItem>> ListByRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MenuItem>> ListByCategoryAsync(
        Guid menuCategoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Combien d'articles cette section contient-elle ENCORE ?
    ///
    /// Existe pour la suppression de section : le restaurateur doit savoir COMBIEN
    /// d'articles le retiennent, pas seulement qu'« il en reste ». Un compte, pas
    /// un booléen — « cette section contient encore 12 articles » se traite ; « la
    /// section n'est pas vide » se subit.
    /// </summary>
    Task<int> CountInCategoryAsync(Guid menuCategoryId, CancellationToken cancellationToken = default);

    Task AddAsync(MenuItem item, CancellationToken cancellationToken = default);

    void Remove(MenuItem item);
}
