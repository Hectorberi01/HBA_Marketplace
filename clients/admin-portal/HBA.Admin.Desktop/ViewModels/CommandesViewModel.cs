using System.Collections.ObjectModel;
using System.Globalization;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Les commandes, et la trappe de sortie des commandes bloquées.</summary>
public sealed class CommandesViewModel : ViewModelBase
{
    private const int Taille = 25;
    private const string Tous = "Tous";

    private readonly ClientApiAdmin _api;
    private readonly IDemandeurDeSaisie _saisie;

    private LigneCommande? _selection;
    private string _recherche = string.Empty;
    private string? _filtreStatut = "UnderReview";
    private string? _erreur;
    private string? _confirmation;
    private bool _enCours;
    private int _page = 1;
    private int _total;

    public CommandesViewModel(ClientApiAdmin api, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _saisie = saisie;

        Chercher = new CommandeAsync(() => ChargerAsync(1));
        Precedente = new CommandeAsync(() => ChargerAsync(_page - 1), () => _page > 1 && !EnCours);
        Suivante = new CommandeAsync(() => ChargerAsync(_page + 1), () => APageSuivante && !EnCours);
        Agir = new CommandeAsync<GesteCommande>(AgirAsync, Applicable);

        Chercher.Execute(null);
    }

    public ObservableCollection<LigneCommande> Commandes { get; } = [];

    /// <summary>Les huit valeurs de `OrderStatus`, dans l'ordre du cycle.</summary>
    /// <remarks>
    /// LE FILTRE S'OUVRE SUR `UnderReview`, ET C'EST DÉLIBÉRÉ.
    ///
    /// C'est le seul état sur lequel cet écran peut agir. Ouvrir sur « Tous »
    /// afficherait des milliers de commandes dont aucune n'attend quoi que ce
    /// soit, et il faudrait filtrer avant de commencer — tous les jours.
    /// </remarks>
    public IReadOnlyList<string> Statuts { get; } =
    [
        Tous, "Pending", "AwaitingPayment", "Paid", "Confirmed",
        "UnderReview", "Delivered", "Cancelled", "Failed",
    ];

    public IReadOnlyList<GesteCommande> Gestes { get; } = GesteCommande.Tous;

    public LigneCommande? Selection
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

    public string Recherche
    {
        get => _recherche;
        set => Definir(ref _recherche, value);
    }

    public string? FiltreStatut
    {
        get => _filtreStatut;
        set => Definir(ref _filtreStatut, value);
    }

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
        private set
        {
            if (Definir(ref _enCours, value))
            {
                Precedente.Reevaluer();
                Suivante.Reevaluer();
                Agir.Reevaluer();
            }
        }
    }

    public string Position => _total == 0
        ? "Aucune commande"
        : $"Page {_page} · {_total:N0} commande(s)";

    private bool APageSuivante => (long)_page * Taille < _total;

    public CommandeAsync Chercher { get; }

    public CommandeAsync Precedente { get; }

    public CommandeAsync Suivante { get; }

    public CommandeAsync<GesteCommande> Agir { get; }

    private bool Applicable(GesteCommande geste)
        => _selection is not null && !EnCours && geste.ApplicableA(_selection.Statut);

    private async Task ChargerAsync(int page)
    {
        if (page < 1)
        {
            return;
        }

        EnCours = true;
        Erreur = null;
        Confirmation = null;

        try
        {
            var filtre = _filtreStatut == Tous ? null : _filtreStatut;
            var resultat = await _api.ListerCommandesAsync(page, Taille, _recherche, filtre);

            if (!resultat.Reussi || resultat.Valeur?.Items is null)
            {
                Erreur = resultat.Message ?? "Liste indisponible.";
                return;
            }

            Selection = null;
            Commandes.Clear();

            foreach (var commande in resultat.Valeur.Items)
            {
                Commandes.Add(new LigneCommande(commande));
            }

            _page = resultat.Valeur.Page <= 0 ? page : resultat.Valeur.Page;
            _total = resultat.Valeur.Total;

            Notifier(nameof(Position));
        }
        finally
        {
            EnCours = false;
        }
    }

    private async Task AgirAsync(GesteCommande geste)
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
            motif = await _saisie.MotifAsync($"{geste.Libelle} · {ligne.MontantAffiche}");

            if (string.IsNullOrWhiteSpace(motif))
            {
                return;
            }
        }

        if (geste.Destructeur && !_api.ElevationValide)
        {
            var mot = await _saisie.MotDePasseAsync($"{geste.Libelle} · {ligne.MontantAffiche}");

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
            var resultat = await _api.AgirSurCommandeAsync(ligne.Id, geste, motif);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = $"{geste.Libelle} — {ligne.MontantAffiche}.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync(_page);
    }
}

/// <summary>Une commande, telle que la liste l'affiche.</summary>
public sealed class LigneCommande
{
    public LigneCommande(CommandeAdmin commande)
    {
        Id = commande.Id;
        Statut = commande.Status;
        MontantAffiche = Argent.Formater(commande.GrandTotal, commande.Currency);
        Acheteur = commande.BuyerId.ToString("N")[..8];
        Reference = commande.Id.ToString("N")[..8];
        EnArbitrage = commande.Status == "UnderReview";

        CreeeLe = commande.CreatedAtUtc.ToLocalTime()
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
    }

    public Guid Id { get; }

    public string Reference { get; }

    public string MontantAffiche { get; }

    public string Statut { get; }

    public string Acheteur { get; }

    public string CreeeLe { get; }

    /// <summary>Pilote la mise en évidence : c'est la seule ligne actionnable.</summary>
    public bool EnArbitrage { get; }
}
