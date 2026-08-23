using System.Globalization;
using HBA.Food.Application.Abstractions;
using HBA.Food.Domain.Menus;
using HBA.Food.Domain.Restaurants;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Application.Menus;

// ═══════════════════════════════════════════════════════════════════════════════
// DEUX NIVEAUX, DEUX JEUX DE COMMANDES (cahier des charges §5).
//
//   • la CARTE — « Menu du midi », « Carte d'été » — porte les créneaux ;
//   • la SECTION — « Entrées », « Plats » — porte les articles.
//
// Ce qui s'appelait « Menu » ici désignait une section. Le renommage n'est pas
// cosmétique : tant qu'un seul mot servait aux deux, il n'existait aucun endroit
// évident où poser les horaires de service d'une carte.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Les créneaux, en entrée. Dates au format « yyyy-MM-dd », heures « HH:mm ».
///
/// Tout est FACULTATIF : quatre champs vides décrivent la carte permanente, qui
/// est de loin le cas le plus fréquent et ne doit rien coûter à saisir.
/// </summary>
public sealed record ServingWindowInput(
    string? AvailableFrom, string? AvailableUntil, string? StartTime, string? EndTime);

// ── Cartes ──────────────────────────────────────────────────────────────────

public sealed record CreateMenuCommand(
    Guid RestaurantId, string Name, int DisplayOrder, ServingWindowInput? Window = null) : ICommand<Guid>;

public sealed record RenameMenuCommand(
    Guid RestaurantId, Guid MenuId, string Name, string? Description) : ICommand;

/// <summary>
/// Fixe QUAND la carte est servie.
///
/// C'est la commande qui justifie tout le second niveau : sans elle, un maquis
/// proposant un menu ouvrier à midi et une carte complète le soir devait masquer
/// et démasquer des sections à la main, deux fois par jour, tous les jours.
/// </summary>
public sealed record SetMenuWindowCommand(
    Guid RestaurantId, Guid MenuId, ServingWindowInput Window) : ICommand;

public sealed record SetMenuVisibilityCommand(Guid RestaurantId, Guid MenuId, bool Active) : ICommand;

public sealed record ReorderMenuCommand(Guid RestaurantId, Guid MenuId, int DisplayOrder) : ICommand;

/// <summary>
/// Supprime une carte.
///
/// REFUSÉE TANT QU'ELLE CONTIENT DES SECTIONS — même raison, un cran plus
/// haut, que le refus de supprimer une section garnie : les sections référencent
/// la carte sans lui appartenir. La supprimer ne les supprime pas, elle les
/// ORPHELINE — et la projection de la carte parcourt les CARTES puis rattache les
/// sections. Une section sans carte disparaît des deux vues, celle du client
/// comme celle du restaurateur, avec tous ses articles.
/// </summary>
public sealed record DeleteMenuCommand(Guid RestaurantId, Guid MenuId) : ICommand;

// ── Sections ────────────────────────────────────────────────────────────────

public sealed record CreateCategoryCommand(
    Guid RestaurantId, Guid MenuId, string Name, int DisplayOrder) : ICommand<Guid>;

public sealed record RenameCategoryCommand(
    Guid RestaurantId, Guid CategoryId, string Name, string? Description) : ICommand;

public sealed record SetCategoryVisibilityCommand(
    Guid RestaurantId, Guid CategoryId, bool Active) : ICommand;

public sealed record ReorderCategoryCommand(
    Guid RestaurantId, Guid CategoryId, int DisplayOrder) : ICommand;

/// <summary>
/// Déplace une section vers une autre carte.
///
/// EMPORTE TOUS SES ARTICLES SANS EN TOUCHER UN SEUL : ils référencent la
/// section, pas la carte. C'est le geste qui rend la bascule à deux niveaux
/// utilisable — faire passer « Grillades » du midi au soir se fait en un appel,
/// pas en quinze déplacements d'articles.
/// </summary>
public sealed record MoveCategoryCommand(Guid RestaurantId, Guid CategoryId, Guid MenuId) : ICommand;

public sealed record DeleteCategoryCommand(Guid RestaurantId, Guid CategoryId) : ICommand;

// ── Articles ────────────────────────────────────────────────────────────────

public sealed record CreateMenuItemCommand(
    Guid RestaurantId, Guid CategoryId, string Name, decimal BasePrice) : ICommand<Guid>;

/// <param name="DisplayOrder">
/// `null` LAISSE LE RANG INCHANGÉ. Voir `MenuItem.UpdateDetails` : le paramètre
/// était non-nullable, et toute mise à jour de libellé remettait le plat en tête
/// de sa section.
/// </param>
public sealed record UpdateMenuItemCommand(
    Guid RestaurantId, Guid ItemId, string Name, string? Description, int? DisplayOrder = null)
    : ICommand;

/// <summary>Rattache la photo du plat (§6), par identifiant de média.</summary>
public sealed record SetMenuItemImageCommand(
    Guid RestaurantId, Guid ItemId, Guid? ImageMediaId, string? ImagePublicUrl) : ICommand;

public sealed record ChangeMenuItemPriceCommand(Guid RestaurantId, Guid ItemId, decimal BasePrice) : ICommand;

/// <summary>
/// Épuisé POUR AUJOURD'HUI : l'article revient au service suivant, sans que
/// personne ait à y penser.
///
/// L'ÉCHÉANCE EST CALCULÉE ICI, à partir des horaires du restaurant. La
/// demander à l'appelant reviendrait à demander une date absolue à un cuisinier
/// en plein service, sur un téléphone.
/// </summary>
public sealed record MarkItemSoldOutTodayCommand(Guid RestaurantId, Guid ItemId) : ICommand;

/// <summary>Retiré de la carte jusqu'à nouvel ordre. NE revient PAS seul.</summary>
public sealed record MarkItemUnavailableCommand(Guid RestaurantId, Guid ItemId) : ICommand;

public sealed record MarkItemAvailableCommand(Guid RestaurantId, Guid ItemId) : ICommand;

public sealed record MoveMenuItemCommand(Guid RestaurantId, Guid ItemId, Guid CategoryId) : ICommand;

/// <summary>
/// Supprime définitivement un article.
///
/// CE N'EST PAS « ARRÊTER DE VENDRE CE PLAT ». Pour cela il y a
/// <c>MarkItemUnavailableCommand</c>, qui le retire de la vitrine en le gardant
/// en base, prêt à revenir. La suppression sert aux ERREURS DE SAISIE : le
/// doublon, la faute de frappe, le plat créé dans la mauvaise carte.
///
/// CE QUE LE RACCORDEMENT AUX COMMANDES DEVRA RESPECTER
///
/// Aujourd'hui rien ne référence un article hors de ce module. Le jour où le
/// panier et la commande porteront un <c>MenuItemId</c>, une ligne de commande
/// devra FIGER le libellé et le prix — le cahier (§13) l'exige explicitement, et
/// <c>SelectedOption</c> le fait déjà pour les options.
/// </summary>
public sealed record DeleteMenuItemCommand(Guid RestaurantId, Guid ItemId) : ICommand;

// ── Options ─────────────────────────────────────────────────────────────────

public sealed record AddOptionGroupCommand(
    Guid RestaurantId, Guid ItemId, string Name, int MinSelections, int MaxSelections, int DisplayOrder) : ICommand<Guid>;

public sealed record RemoveOptionGroupCommand(Guid RestaurantId, Guid ItemId, Guid GroupId) : ICommand;

public sealed record AddOptionCommand(
    Guid RestaurantId, Guid ItemId, Guid GroupId, string Name, decimal PriceDelta) : ICommand<Guid>;

public sealed record RemoveOptionCommand(Guid RestaurantId, Guid ItemId, Guid GroupId, Guid OptionId) : ICommand;

public sealed record SetOptionSoldOutTodayCommand(
    Guid RestaurantId, Guid ItemId, Guid GroupId, Guid OptionId) : ICommand;

public sealed record SetOptionAvailableCommand(
    Guid RestaurantId, Guid ItemId, Guid GroupId, Guid OptionId) : ICommand;

internal sealed class MenuCommandHandler
    : ICommandHandler<CreateMenuCommand, Guid>,
      ICommandHandler<RenameMenuCommand>,
      ICommandHandler<SetMenuWindowCommand>,
      ICommandHandler<SetMenuVisibilityCommand>,
      ICommandHandler<ReorderMenuCommand>,
      ICommandHandler<DeleteMenuCommand>,
      ICommandHandler<CreateCategoryCommand, Guid>,
      ICommandHandler<RenameCategoryCommand>,
      ICommandHandler<SetCategoryVisibilityCommand>,
      ICommandHandler<ReorderCategoryCommand>,
      ICommandHandler<MoveCategoryCommand>,
      ICommandHandler<DeleteCategoryCommand>,
      ICommandHandler<CreateMenuItemCommand, Guid>,
      ICommandHandler<UpdateMenuItemCommand>,
      ICommandHandler<SetMenuItemImageCommand>,
      ICommandHandler<ChangeMenuItemPriceCommand>,
      ICommandHandler<MarkItemSoldOutTodayCommand>,
      ICommandHandler<MarkItemUnavailableCommand>,
      ICommandHandler<MarkItemAvailableCommand>,
      ICommandHandler<MoveMenuItemCommand>,
      ICommandHandler<DeleteMenuItemCommand>,
      ICommandHandler<AddOptionGroupCommand, Guid>,
      ICommandHandler<RemoveOptionGroupCommand>,
      ICommandHandler<AddOptionCommand, Guid>,
      ICommandHandler<RemoveOptionCommand>,
      ICommandHandler<SetOptionSoldOutTodayCommand>,
      ICommandHandler<SetOptionAvailableCommand>
{
    private readonly IRestaurantRepository _restaurants;
    private readonly IMenuRepository _menus;
    private readonly IMenuCategoryRepository _categories;
    private readonly IMenuItemRepository _items;
    private readonly IFoodUnitOfWork _unitOfWork;

    public MenuCommandHandler(
        IRestaurantRepository restaurants,
        IMenuRepository menus,
        IMenuCategoryRepository categories,
        IMenuItemRepository items,
        IFoodUnitOfWork unitOfWork)
    {
        _restaurants = restaurants;
        _menus = menus;
        _categories = categories;
        _items = items;
        _unitOfWork = unitOfWork;
    }

    // ── Cartes ──────────────────────────────────────────────────────────────

    public async Task<Result<Guid>> Handle(CreateMenuCommand command, CancellationToken cancellationToken)
    {
        var creneau = ParseWindow(command.Window);
        if (creneau.IsFailure)
        {
            return Result.Failure<Guid>(creneau.Error);
        }

        var carte = Menu.Create(command.RestaurantId, command.Name, creneau.Value, command.DisplayOrder);
        if (carte.IsFailure)
        {
            return Result.Failure<Guid>(carte.Error);
        }

        await _menus.AddAsync(carte.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return carte.Value.Id.Value;
    }

    public Task<Result> Handle(RenameMenuCommand command, CancellationToken cancellationToken)
        => OnMenuAsync(command.MenuId, command.RestaurantId, cancellationToken,
            m => m.Rename(command.Name, command.Description));

    public async Task<Result> Handle(SetMenuWindowCommand command, CancellationToken cancellationToken)
    {
        var creneau = ParseWindow(command.Window);
        if (creneau.IsFailure)
        {
            return Result.Failure(creneau.Error);
        }

        return await OnMenuAsync(command.MenuId, command.RestaurantId, cancellationToken,
            m => m.SetWindow(creneau.Value));
    }

    public Task<Result> Handle(SetMenuVisibilityCommand command, CancellationToken cancellationToken)
        => OnMenuAsync(command.MenuId, command.RestaurantId, cancellationToken,
            m => command.Active ? m.Activate() : m.Deactivate());

    public Task<Result> Handle(ReorderMenuCommand command, CancellationToken cancellationToken)
        => OnMenuAsync(command.MenuId, command.RestaurantId, cancellationToken,
            m => m.Reorder(command.DisplayOrder));

    public async Task<Result> Handle(DeleteMenuCommand command, CancellationToken cancellationToken)
    {
        var carte = await LoadMenuAsync(command.MenuId, command.RestaurantId, cancellationToken);
        if (carte is null)
        {
            return Result.Failure(MenuIntrouvable);
        }

        // LA GARDE, UN CRAN AU-DESSUS DE CELLE DES SECTIONS.
        //
        // Supprimer une carte garnie orphelinerait ses sections — et donc, en
        // cascade silencieuse, tous les articles qu'elles portent. Le nombre est
        // dans le message : « il reste 3 sections » se traite, « la carte n'est
        // pas vide » se subit.
        var restantes = await _categories.CountInMenuAsync(command.MenuId, cancellationToken);
        if (restantes > 0)
        {
            return Result.Failure(Error.Conflict(
                "food.menu.not_empty",
                restantes == 1
                    ? "Cette carte contient encore 1 section. Déplacez-la ou supprimez-la avant de supprimer la carte."
                    : $"Cette carte contient encore {restantes} sections. Déplacez-les ou supprimez-les avant de supprimer la carte."));
        }

        _menus.Remove(carte);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    // ── Sections ────────────────────────────────────────────────────────────

    public async Task<Result<Guid>> Handle(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        // LA CARTE DOIT APPARTENIR À CE RESTAURANT.
        //
        // Le MenuId vient du client. Sans ce contrôle, un restaurateur rangerait
        // ses sections dans la carte d'un concurrent — dont la vitrine afficherait
        // des plats qu'il n'a jamais saisis, à des prix qu'il ne fixe pas.
        var carte = await LoadMenuAsync(command.MenuId, command.RestaurantId, cancellationToken);
        if (carte is null)
        {
            return Result.Failure<Guid>(MenuIntrouvable);
        }

        var section = MenuCategory.Create(
            command.RestaurantId, command.MenuId, command.Name, command.DisplayOrder);

        if (section.IsFailure)
        {
            return Result.Failure<Guid>(section.Error);
        }

        await _categories.AddAsync(section.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return section.Value.Id.Value;
    }

    public Task<Result> Handle(RenameCategoryCommand command, CancellationToken cancellationToken)
        => OnCategoryAsync(command.CategoryId, command.RestaurantId, cancellationToken,
            c => c.Rename(command.Name, command.Description));

    public Task<Result> Handle(SetCategoryVisibilityCommand command, CancellationToken cancellationToken)
        => OnCategoryAsync(command.CategoryId, command.RestaurantId, cancellationToken,
            c => command.Active ? c.Activate() : c.Deactivate());

    public Task<Result> Handle(ReorderCategoryCommand command, CancellationToken cancellationToken)
        => OnCategoryAsync(command.CategoryId, command.RestaurantId, cancellationToken,
            c => c.Reorder(command.DisplayOrder));

    public async Task<Result> Handle(MoveCategoryCommand command, CancellationToken cancellationToken)
    {
        // La carte de destination est vérifiée AVANT de charger la section :
        // inutile de mettre la main sur l'agrégat pour découvrir ensuite qu'on n'a
        // nulle part où le mettre.
        var destination = await LoadMenuAsync(command.MenuId, command.RestaurantId, cancellationToken);
        if (destination is null)
        {
            return Result.Failure(MenuIntrouvable);
        }

        return await OnCategoryAsync(command.CategoryId, command.RestaurantId, cancellationToken,
            c => c.MoveToMenu(command.MenuId));
    }

    public async Task<Result> Handle(DeleteCategoryCommand command, CancellationToken cancellationToken)
    {
        var section = await LoadCategoryAsync(command.CategoryId, command.RestaurantId, cancellationToken);
        if (section is null)
        {
            return Result.Failure(SectionIntrouvable);
        }

        var restants = await _items.CountInCategoryAsync(command.CategoryId, cancellationToken);
        if (restants > 0)
        {
            return Result.Failure(Error.Conflict(
                "food.category.not_empty",
                restants == 1
                    ? "Cette section contient encore 1 article. Déplacez-le ou supprimez-le avant de supprimer la section."
                    : $"Cette section contient encore {restants} articles. Déplacez-les ou supprimez-les avant de supprimer la section."));
        }

        _categories.Remove(section);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    // ── Articles ────────────────────────────────────────────────────────────

    public async Task<Result<Guid>> Handle(CreateMenuItemCommand command, CancellationToken cancellationToken)
    {
        var section = await LoadCategoryAsync(command.CategoryId, command.RestaurantId, cancellationToken);
        if (section is null)
        {
            return Result.Failure<Guid>(SectionIntrouvable);
        }

        var item = MenuItem.Create(command.RestaurantId, command.CategoryId, command.Name, command.BasePrice);
        if (item.IsFailure)
        {
            return Result.Failure<Guid>(item.Error);
        }

        await _items.AddAsync(item.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return item.Value.Id.Value;
    }

    public Task<Result> Handle(UpdateMenuItemCommand command, CancellationToken cancellationToken)
        => OnItemAsync(command.ItemId, command.RestaurantId, cancellationToken,
            i => i.UpdateDetails(command.Name, command.Description, command.DisplayOrder));

    public Task<Result> Handle(SetMenuItemImageCommand command, CancellationToken cancellationToken)
        => OnItemAsync(command.ItemId, command.RestaurantId, cancellationToken,
            i => i.SetImage(command.ImageMediaId, command.ImagePublicUrl));

    public Task<Result> Handle(ChangeMenuItemPriceCommand command, CancellationToken cancellationToken)
        => OnItemAsync(command.ItemId, command.RestaurantId, cancellationToken,
            i => i.ChangePrice(command.BasePrice));

    public async Task<Result> Handle(MarkItemSoldOutTodayCommand command, CancellationToken cancellationToken)
    {
        // L'échéance vient des horaires du restaurant : c'est la seule chose que
        // l'article ne peut pas savoir seul.
        var restaurant = await _restaurants.GetByIdAsync(new RestaurantId(command.RestaurantId), cancellationToken);
        if (restaurant is null)
        {
            return Result.Failure(Error.NotFound("food.restaurant.not_found", "Établissement introuvable."));
        }

        var maintenant = DateTime.UtcNow;
        var retour = restaurant.EndOfServiceDayUtc(maintenant);

        return await OnItemAsync(command.ItemId, command.RestaurantId, cancellationToken,
            i => i.MarkUnavailableUntil(retour, maintenant));
    }

    public Task<Result> Handle(MarkItemUnavailableCommand command, CancellationToken cancellationToken)
        => OnItemAsync(command.ItemId, command.RestaurantId, cancellationToken, i => i.MarkUnavailableIndefinitely());

    public Task<Result> Handle(MarkItemAvailableCommand command, CancellationToken cancellationToken)
        => OnItemAsync(command.ItemId, command.RestaurantId, cancellationToken, i => i.MarkAvailable());

    public async Task<Result> Handle(MoveMenuItemCommand command, CancellationToken cancellationToken)
    {
        var destination = await LoadCategoryAsync(command.CategoryId, command.RestaurantId, cancellationToken);
        if (destination is null)
        {
            return Result.Failure(SectionIntrouvable);
        }

        return await OnItemAsync(command.ItemId, command.RestaurantId, cancellationToken,
            i => i.MoveToCategory(command.CategoryId));
    }

    public async Task<Result> Handle(DeleteMenuItemCommand command, CancellationToken cancellationToken)
    {
        var item = await LoadItemAsync(command.ItemId, command.RestaurantId, cancellationToken);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("food.item.not_found", "Article introuvable."));
        }

        // Les groupes d'options sont des types « owned » : EF les supprime avec
        // la racine. Rien à faire de plus, et surtout rien à oublier.
        _items.Remove(item);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    // ── Options ─────────────────────────────────────────────────────────────

    public async Task<Result<Guid>> Handle(AddOptionGroupCommand command, CancellationToken cancellationToken)
    {
        var item = await LoadItemAsync(command.ItemId, command.RestaurantId, cancellationToken);
        if (item is null)
        {
            return Result.Failure<Guid>(Error.NotFound("food.item.not_found", "Article introuvable."));
        }

        var groupe = item.AddOptionGroup(
            command.Name, command.MinSelections, command.MaxSelections, command.DisplayOrder);

        if (groupe.IsFailure)
        {
            return Result.Failure<Guid>(groupe.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return groupe.Value;
    }

    public Task<Result> Handle(RemoveOptionGroupCommand command, CancellationToken cancellationToken)
        => OnItemAsync(command.ItemId, command.RestaurantId, cancellationToken,
            i => i.RemoveOptionGroup(command.GroupId));

    public async Task<Result<Guid>> Handle(AddOptionCommand command, CancellationToken cancellationToken)
    {
        var item = await LoadItemAsync(command.ItemId, command.RestaurantId, cancellationToken);
        if (item is null)
        {
            return Result.Failure<Guid>(Error.NotFound("food.item.not_found", "Article introuvable."));
        }

        var option = item.AddOption(command.GroupId, command.Name, command.PriceDelta);
        if (option.IsFailure)
        {
            return Result.Failure<Guid>(option.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return option.Value;
    }

    public Task<Result> Handle(RemoveOptionCommand command, CancellationToken cancellationToken)
        => OnItemAsync(command.ItemId, command.RestaurantId, cancellationToken,
            i => i.RemoveOption(command.GroupId, command.OptionId));

    public async Task<Result> Handle(SetOptionSoldOutTodayCommand command, CancellationToken cancellationToken)
    {
        var restaurant = await _restaurants.GetByIdAsync(new RestaurantId(command.RestaurantId), cancellationToken);
        if (restaurant is null)
        {
            return Result.Failure(Error.NotFound("food.restaurant.not_found", "Établissement introuvable."));
        }

        var maintenant = DateTime.UtcNow;
        var etat = ItemAvailability.UntilUtc(restaurant.EndOfServiceDayUtc(maintenant), maintenant);
        if (etat.IsFailure)
        {
            return Result.Failure(etat.Error);
        }

        return await OnItemAsync(command.ItemId, command.RestaurantId, cancellationToken,
            i => i.SetOptionAvailability(command.GroupId, command.OptionId, etat.Value));
    }

    public Task<Result> Handle(SetOptionAvailableCommand command, CancellationToken cancellationToken)
        => OnItemAsync(command.ItemId, command.RestaurantId, cancellationToken,
            i => i.SetOptionAvailability(command.GroupId, command.OptionId, ItemAvailability.Available()));

    // ── Lecture des créneaux ────────────────────────────────────────────────

    /// <summary>
    /// Lit les quatre champs du cahier, en culture INVARIANTE.
    ///
    /// Le projet tourne en <c>InvariantGlobalization</c>, et faire dépendre la
    /// lecture d'un horaire d'un réglage serveur ferait cesser « 14:30 » d'être lu
    /// un jour, sans qu'aucune ligne de code ait changé. Même raisonnement que
    /// pour les horaires de service.
    /// </summary>
    private static Result<MenuServingWindow> ParseWindow(ServingWindowInput? entree)
    {
        if (entree is null)
        {
            return MenuServingWindow.Always;
        }

        var debut = ParseDate(entree.AvailableFrom);
        if (debut.IsFailure)
        {
            return debut.Error;
        }

        var fin = ParseDate(entree.AvailableUntil);
        if (fin.IsFailure)
        {
            return fin.Error;
        }

        var ouverture = ParseTime(entree.StartTime);
        if (ouverture.IsFailure)
        {
            return ouverture.Error;
        }

        var fermeture = ParseTime(entree.EndTime);
        if (fermeture.IsFailure)
        {
            return fermeture.Error;
        }

        return MenuServingWindow.Create(debut.Value, fin.Value, ouverture.Value, fermeture.Value);
    }

    private static Result<DateOnly?> ParseDate(string? valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur))
        {
            return Result.Success<DateOnly?>(null);
        }

        return DateOnly.TryParse(valeur, CultureInfo.InvariantCulture, out var date)
            ? Result.Success<DateOnly?>(date)
            : Error.Validation("food.menu.date_invalid", $"Date attendue au format « aaaa-mm-jj » : « {valeur} ».");
    }

    private static Result<TimeOnly?> ParseTime(string? valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur))
        {
            return Result.Success<TimeOnly?>(null);
        }

        return TimeOnly.TryParse(valeur, CultureInfo.InvariantCulture, out var heure)
            ? Result.Success<TimeOnly?>(heure)
            : Error.Validation("food.menu.time_invalid", $"Heure attendue au format « HH:mm » : « {valeur} ».");
    }

    // ── Chargement scopé ────────────────────────────────────────────────────

    private static readonly Error MenuIntrouvable =
        Error.NotFound("food.menu.not_found", "Carte introuvable.");

    private static readonly Error SectionIntrouvable =
        Error.NotFound("food.category.not_found", "Section introuvable.");

    /// <summary>
    /// LE RestaurantId N'EST PAS UN PARAMÈTRE DE CONFORT : C'EST LA CLÔTURE.
    ///
    /// Toutes ces commandes désignent une carte, une section ou un article par un
    /// GUID venu du client. Sans la comparaison au restaurant de l'appelant —
    /// lui-même résolu DEPUIS LE JETON par la route —, un restaurateur modifierait
    /// le prix d'un plat concurrent, ou le déclarerait épuisé en pleine heure de
    /// pointe.
    ///
    /// On répond « introuvable » et non « interdit » : distinguer les deux dirait
    /// à qui teste des identifiants lesquels existent.
    /// </summary>
    private async Task<Menu?> LoadMenuAsync(Guid menuId, Guid restaurantId, CancellationToken cancellationToken)
    {
        var carte = await _menus.GetByIdAsync(new MenuId(menuId), cancellationToken);
        return carte is null || carte.RestaurantId != restaurantId ? null : carte;
    }

    private async Task<MenuCategory?> LoadCategoryAsync(
        Guid categoryId, Guid restaurantId, CancellationToken cancellationToken)
    {
        var section = await _categories.GetByIdAsync(new MenuCategoryId(categoryId), cancellationToken);
        return section is null || section.RestaurantId != restaurantId ? null : section;
    }

    private async Task<MenuItem?> LoadItemAsync(Guid itemId, Guid restaurantId, CancellationToken cancellationToken)
    {
        var item = await _items.GetByIdAsync(new MenuItemId(itemId), cancellationToken);
        return item is null || item.RestaurantId != restaurantId ? null : item;
    }

    private async Task<Result> OnMenuAsync(
        Guid menuId, Guid restaurantId, CancellationToken cancellationToken, Func<Menu, Result> action)
    {
        var carte = await LoadMenuAsync(menuId, restaurantId, cancellationToken);
        return carte is null ? Result.Failure(MenuIntrouvable) : await CommitAsync(action(carte), cancellationToken);
    }

    private async Task<Result> OnCategoryAsync(
        Guid categoryId, Guid restaurantId, CancellationToken cancellationToken, Func<MenuCategory, Result> action)
    {
        var section = await LoadCategoryAsync(categoryId, restaurantId, cancellationToken);
        return section is null
            ? Result.Failure(SectionIntrouvable)
            : await CommitAsync(action(section), cancellationToken);
    }

    private async Task<Result> OnItemAsync(
        Guid itemId, Guid restaurantId, CancellationToken cancellationToken, Func<MenuItem, Result> action)
    {
        var item = await LoadItemAsync(itemId, restaurantId, cancellationToken);
        return item is null
            ? Result.Failure(Error.NotFound("food.item.not_found", "Article introuvable."))
            : await CommitAsync(action(item), cancellationToken);
    }

    private async Task<Result> CommitAsync(Result result, CancellationToken cancellationToken)
    {
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
