using System.Collections.ObjectModel;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>La modération du catalogue : valider, rejeter, suspendre.</summary>
public sealed class ProduitsViewModel : ViewModelBase
{
    private const int Taille = 25;
    private const string Tous = "Tous";

    private readonly ClientApiAdmin _api;
    private readonly IDemandeurDeSaisie _saisie;

    private LigneProduit? _selection;
    private string _recherche = string.Empty;
    private string? _filtreStatut;
    private string? _erreur;
    private string? _confirmation;
    private bool _enCours;
    private bool _fileDeValidation = true;
    private int _page = 1;
    private long _total;
    private bool _pageSuivante;

    public ProduitsViewModel(ClientApiAdmin api, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _saisie = saisie;

        Chercher = new CommandeAsync(() => ChargerAsync(1));
        Precedente = new CommandeAsync(() => ChargerAsync(_page - 1), () => _page > 1 && !EnCours);
        Suivante = new CommandeAsync(() => ChargerAsync(_page + 1), () => _pageSuivante && !EnCours);

        Basculer = new CommandeAsync<string>(cle =>
        {
            FileDeValidation = cle == "file";
            return ChargerAsync(1);
        });

        Agir = new CommandeAsync<GesteProduit>(AgirAsync, Applicable);

        Chercher.Execute(null);
    }

    public ObservableCollection<LigneProduit> Produits { get; } = [];

    /// <summary>Les huit valeurs EXACTES de `ProductStatus`, dans l'ordre du cycle.</summary>
    /// <remarks>
    /// L'ORDRE EST CELUI DU CYCLE DE VIE, PAS L'ALPHABÉTIQUE.
    ///
    /// Brouillon, soumis, validé, refusé, publié, dépublié, suspendu, archivé :
    /// une liste déroulante qui suit le cycle se parcourt sans réfléchir. Triée
    /// par ordre alphabétique, elle mettrait « Approved » avant « Draft » et
    /// obligerait à chercher.
    /// </remarks>
    public IReadOnlyList<string> Statuts { get; } =
    [
        Tous, "Draft", "PendingReview", "Approved", "Rejected",
        "Published", "Unpublished", "Suspended", "Archived",
    ];

    public IReadOnlyList<GesteProduit> Gestes { get; } = GesteProduit.Tous;

    /// <summary>Affiche-t-on la file de validation, ou tout le catalogue ?</summary>
    public bool FileDeValidation
    {
        get => _fileDeValidation;
        private set
        {
            if (Definir(ref _fileDeValidation, value))
            {
                Notifier(nameof(SurCatalogue));
                Notifier(nameof(FiltresActifs));
                Notifier(nameof(Explication));
            }
        }
    }

    public bool SurCatalogue => !_fileDeValidation;

    /// <summary>La file n'accepte ni recherche ni statut : les champs sont masqués.</summary>
    public bool FiltresActifs => !_fileDeValidation;

    public string Explication => _fileDeValidation
        ? "Fiches soumises et en attente de validation. Le vendeur ne peut plus les modifier tant qu'elles sont ici."
        : "Tout le catalogue, tous statuts confondus.";

    public LigneProduit? Selection
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
        ? "Aucune fiche"
        : $"Page {_page} · {_total:N0} fiche(s)";

    public CommandeAsync Chercher { get; }

    public CommandeAsync Precedente { get; }

    public CommandeAsync Suivante { get; }

    public CommandeAsync<string> Basculer { get; }

    public CommandeAsync<GesteProduit> Agir { get; }

    private bool Applicable(GesteProduit geste)
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

            var resultat = await _api.ListerProduitsAsync(
                _fileDeValidation, page, Taille, _recherche, filtre);

            if (!resultat.Reussi || resultat.Valeur?.Data is null)
            {
                Erreur = resultat.Message ?? "Liste indisponible.";
                return;
            }

            Selection = null;
            Produits.Clear();

            foreach (var produit in resultat.Valeur.Data)
            {
                Produits.Add(new LigneProduit(produit));
            }

            _page = resultat.Valeur.Meta?.Page ?? page;
            _total = resultat.Valeur.Meta?.Total ?? Produits.Count;
            _pageSuivante = resultat.Valeur.Meta?.HasNext ?? false;

            Notifier(nameof(Position));
        }
        finally
        {
            EnCours = false;
        }
    }

    private async Task AgirAsync(GesteProduit geste)
    {
        if (_selection is not { } ligne)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        string? saisie = null;

        if (geste.Saisie == SaisieRequise.Motif)
        {
            saisie = await _saisie.MotifAsync($"{geste.Libelle} · {ligne.Nom}");

            if (string.IsNullOrWhiteSpace(saisie))
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
            var resultat = await _api.AgirSurProduitAsync(ligne.Id, geste, saisie);

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

        await ChargerAsync(_page);
    }
}

/// <summary>Une fiche produit, telle que la liste l'affiche.</summary>
public sealed class LigneProduit
{
    public LigneProduit(ProduitAdmin produit)
    {
        Id = produit.Id;
        Nom = produit.Name;
        Statut = produit.Status;
        Slug = produit.Slug;
        Vendeur = produit.SellerId.ToString("N")[..8];
        Gtin = produit.Gtin ?? string.Empty;
        AGtin = !string.IsNullOrEmpty(produit.Gtin);

        // SANS MARQUE N'EST PAS UNE ANOMALIE : `BrandId` est nullable côté
        // domaine, et beaucoup de fiches locales n'en portent pas. L'écrire
        // explicitement évite qu'une colonne vide passe pour un défaut de
        // chargement.
        SansMarque = produit.BrandId is null;
    }

    public Guid Id { get; }

    public string Nom { get; }

    public string Statut { get; }

    public string Slug { get; }

    public string Vendeur { get; }

    public string Gtin { get; }

    public bool AGtin { get; }

    public bool SansMarque { get; }
}
