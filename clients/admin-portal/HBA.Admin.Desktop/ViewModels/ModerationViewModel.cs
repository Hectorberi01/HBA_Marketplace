using System.Collections.ObjectModel;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>La modération : restaurants à ouvrir.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// LA MOITIÉ DE CET ÉCRAN N'EXISTE PAS, ET C'EST ÉCRIT À L'ÉCRAN.
///
/// La modération recouvre deux choses : les restaurants et les avis. Les
/// restaurants ont leur file (`/api/food/admin/restaurants/pending`) ; les avis
/// n'en ont pas. `flag`, `reject` et `restore` existent bien sur
/// engagement-service, mais on ne peut LIRE un avis que par produit ou par
/// vendeur — aucune route ne rend les avis SIGNALÉS.
///
/// Un écran ne peut donc pas montrer ce qui attend une modération d'avis. Le
/// taire donnerait à croire qu'il n'y a rien à modérer.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class ModerationViewModel : ViewModelBase
{
    private const int Plafond = 200;

    private readonly ClientApiAdmin _api;
    private readonly IDemandeurDeSaisie _saisie;

    private LigneRestaurant? _selection;
    private string? _erreur;
    private string? _confirmation;
    private bool _enCours;

    public ModerationViewModel(ClientApiAdmin api, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _saisie = saisie;

        Rafraichir = new CommandeAsync(ChargerAsync);
        Agir = new CommandeAsync<GesteRestaurant>(AgirAsync, _ => _selection is not null && !EnCours);

        Rafraichir.Execute(null);
    }

    public ObservableCollection<LigneRestaurant> Restaurants { get; } = [];

    public IReadOnlyList<GesteRestaurant> Gestes { get; } = GesteRestaurant.Tous;

    /// <summary>Ce que cet écran ne sait pas faire, dit à l'administrateur.</summary>
    public string Manque =>
        "Les AVIS ne sont pas modérables ici : engagement-service permet de les "
        + "signaler, rejeter et rétablir, mais aucune route ne rend la liste des "
        + "avis signalés. Il n'y a donc rien à afficher tant que cette route "
        + "n'existe pas.";

    public LigneRestaurant? Selection
    {
        get => _selection;
        set
        {
            if (Definir(ref _selection, value))
            {
                Notifier(nameof(ADesSelection));
                Agir.Reevaluer();
            }
        }
    }

    public bool ADesSelection => _selection is not null;

    public string? Erreur
    {
        get => _erreur;
        private set { if (Definir(ref _erreur, value)) Notifier(nameof(EnErreur)); }
    }

    public bool EnErreur => !string.IsNullOrEmpty(_erreur);

    public string? Confirmation
    {
        get => _confirmation;
        private set { if (Definir(ref _confirmation, value)) Notifier(nameof(AConfirmation)); }
    }

    public bool AConfirmation => !string.IsNullOrEmpty(_confirmation);

    public bool EnCours
    {
        get => _enCours;
        private set { if (Definir(ref _enCours, value)) Agir.Reevaluer(); }
    }

    public string Position => Restaurants.Count == 0
        ? "Aucun restaurant en attente"
        : $"{Restaurants.Count} restaurant(s) en attente";

    public CommandeAsync Rafraichir { get; }

    public CommandeAsync<GesteRestaurant> Agir { get; }

    private async Task ChargerAsync()
    {
        EnCours = true;
        Erreur = null;
        Confirmation = null;

        try
        {
            var resultat = await _api.ListerRestaurantsAsync(Plafond);

            if (!resultat.Reussi || resultat.Valeur is null)
            {
                Erreur = resultat.Message ?? "File indisponible.";
                return;
            }

            Selection = null;
            Restaurants.Clear();

            foreach (var restaurant in resultat.Valeur)
            {
                Restaurants.Add(new LigneRestaurant(restaurant));
            }

            Notifier(nameof(Position));
        }
        finally
        {
            EnCours = false;
        }
    }

    private async Task AgirAsync(GesteRestaurant geste)
    {
        if (_selection is not { } ligne)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        string? motif = null;

        if (geste.Saisie == SaisieRequise.Motif)
        {
            motif = await _saisie.MotifAsync($"{geste.Libelle} · {ligne.Nom}");

            if (string.IsNullOrWhiteSpace(motif))
            {
                return;
            }
        }

        if (geste.Destructeur && !_api.ElevationValide)
        {
            var mot = await _saisie.MotDePasseAsync($"{geste.Libelle} · {ligne.Nom}");

            if (string.IsNullOrWhiteSpace(mot))
            {
                return;
            }

            var elevation = await _api.EleverAsync(mot);

            if (!elevation.Reussi)
            {
                Erreur = elevation.Message;
                return;
            }
        }

        EnCours = true;

        try
        {
            var resultat = await _api.AgirSurRestaurantAsync(ligne.Id, geste, motif);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = $"{geste.Libelle} — {ligne.Nom}.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }
}

/// <summary>Un restaurant, tel que la file de modération l'affiche.</summary>
public sealed class LigneRestaurant
{
    public LigneRestaurant(RestaurantAdmin restaurant)
    {
        Id = restaurant.Id;
        Nom = restaurant.Name;
        Telephone = restaurant.Phone;
        Statut = restaurant.Status;
        AccepteMaintenant = restaurant.AcceptsOrdersNow;
        Preparation = $"{restaurant.PreparationMinutes} min";
        Blocage = restaurant.BlockedReason ?? string.Empty;

        // `BlockedReason` est une chaîne NON nullable côté contrat, mais elle vaut
        // couramment la chaîne vide. Tester la nullité seule laisserait un
        // encadré vide à l'écran.
        ABlocage = !string.IsNullOrWhiteSpace(restaurant.BlockedReason);
    }

    public Guid Id { get; }

    public string Nom { get; }

    public string Telephone { get; }

    public string Statut { get; }

    public bool AccepteMaintenant { get; }

    public string Preparation { get; }

    public string Blocage { get; }

    public bool ABlocage { get; }
}
