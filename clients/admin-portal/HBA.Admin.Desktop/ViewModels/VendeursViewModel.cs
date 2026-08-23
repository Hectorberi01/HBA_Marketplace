using System.Collections.ObjectModel;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Gouvernance des vendeurs : chercher, lire, décider.</summary>
public sealed class VendeursViewModel : ViewModelBase
{
    private const int Taille = 20;

    private readonly ClientApiAdmin _api;
    private readonly IDemandeurDeSaisie _saisie;

    private VendeurAdmin? _selection;
    private string _recherche = string.Empty;
    private string? _filtreKyb;
    private string? _filtreStatut;
    private string? _erreur;
    private string? _confirmation;
    private bool _enCours;
    private int _page = 1;
    private long _total;
    private bool _pageSuivante;
    private LigneBoutique? _boutique;
    private string? _erreurBoutiques;

    public VendeursViewModel(ClientApiAdmin api, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _saisie = saisie;

        Chercher = new CommandeAsync(() => ChargerAsync(1));
        Precedente = new CommandeAsync(() => ChargerAsync(_page - 1), () => _page > 1 && !EnCours);
        Suivante = new CommandeAsync(() => ChargerAsync(_page + 1), () => _pageSuivante && !EnCours);
        Agir = new CommandeAsync<GesteVendeur>(AgirAsync, _ => _selection is not null && !EnCours);

        ChargerBoutiques = new CommandeAsync(ChargerBoutiquesAsync);
        Suspendre = new CommandeAsync(() => BasculerBoutiqueAsync(true), () => Suspendable && !EnCours);
        Lever = new CommandeAsync(() => BasculerBoutiqueAsync(false), () => Levable && !EnCours);

        Chercher.Execute(null);
    }

    public ObservableCollection<VendeurAdmin> Vendeurs { get; } = [];

    /// <summary>
    /// Les valeurs EXACTES de `KybStatus` côté merchant-service.
    /// </summary>
    /// <remarks>
    /// ELLES NE SONT PAS INTERCHANGEABLES AVEC CELLES DE `Statuts`, ET UNE VALEUR
    /// INCONNUE NE PRODUIT AUCUNE ERREUR.
    ///
    /// `ListSellersQueryHandler` documente son choix : « un filtre illisible est
    /// IGNORÉ, pas refusé ». Écrire `Pending` ici — qui existe dans `SellerStatus`
    /// mais PAS dans `KybStatus` — ne viderait donc pas la liste : elle
    /// afficherait TOUS les vendeurs, en donnant l'apparence d'un filtre actif.
    /// C'est exactement l'erreur qui s'était glissée dans le compteur de l'écran
    /// d'accueil.
    ///
    /// Ces listes sont fixes, et c'est voulu : une liste déroulante alimentée par
    /// le serveur exigerait une route qui n'existe pas, pour une énumération qui
    /// change une fois par an.
    /// </remarks>
    public IReadOnlyList<string> StatutsKyb { get; } =
        [Tous, "NotStarted", "InReview", "Verified", "Rejected"];

    /// <summary>Les valeurs EXACTES de `SellerStatus`.</summary>
    public IReadOnlyList<string> Statuts { get; } =
        [Tous, "Pending", "Active", "Suspended", "Closed", "PendingReactivation"];

    /// <summary>
    /// L'entrée « pas de filtre » des deux listes.
    ///
    /// Une chaîne VIDE aurait fait la même chose côté serveur — `Vide` la
    /// convertit en `null` — mais aurait affiché une ligne blanche en tête de
    /// liste déroulante, que l'on prend pour un défaut de chargement.
    /// </summary>
    private const string Tous = "Tous";

    /// <summary>Les six gestes, tels que la table les déclare.</summary>
    public IReadOnlyList<GesteVendeur> Gestes { get; } = GesteVendeur.Tous;

    public VendeurAdmin? Selection
    {
        get => _selection;
        set
        {
            if (!Definir(ref _selection, value))
            {
                return;
            }

            Notifier(nameof(ADesSelection));
            Agir.Reevaluer();

            // Les boutiques appartiennent au vendeur sélectionné : on vide avant
            // de recharger, sinon celles du précédent restent affichées sous le
            // nom du nouveau — le temps d'un aller-retour réseau, ce qui suffit
            // pour suspendre la mauvaise.
            SelectionBoutique = null;
            Boutiques.Clear();
            ErreurBoutiques = null;
            Notifier(nameof(PositionBoutiques));

            if (value is not null)
            {
                ChargerBoutiques.Execute(null);
            }
        }
    }

    public bool ADesSelection => _selection is not null;

    public string Recherche
    {
        get => _recherche;
        set => Definir(ref _recherche, value);
    }

    public string? FiltreKyb
    {
        get => _filtreKyb;
        set => Definir(ref _filtreKyb, value);
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

    /// <summary>Message de succès d'un geste, effacé au chargement suivant.</summary>
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
        ? "Aucun vendeur"
        : $"Page {_page} · {_total:N0} vendeur(s)";

    // ═════════════════════════════════════════════════════════════════════════
    // LES BOUTIQUES DU VENDEUR SÉLECTIONNÉ.
    //
    // UN PANNEAU ICI, ET NON UNE ENTRÉE DE MENU.
    //
    // La liste dépend d'un vendeur : une section autonome exigerait de recoller
    // un sélecteur de vendeur déjà présent à côté, et de le tenir synchronisé
    // avec celui-ci. Le panneau vit là où la sélection existe.
    //
    // CE QU'IL AJOUTE : un vendeur peut tenir plusieurs boutiques. Jusqu'ici la
    // console ne savait sanctionner que le vendeur ENTIER — c'est-à-dire fermer
    // trois boutiques pour ce qu'une seule a fait.
    // ═════════════════════════════════════════════════════════════════════════

    public ObservableCollection<LigneBoutique> Boutiques { get; } = [];

    public LigneBoutique? SelectionBoutique
    {
        get => _boutique;
        set
        {
            if (!Definir(ref _boutique, value))
            {
                return;
            }

            Notifier(nameof(ADesBoutique));
            Notifier(nameof(Suspendable));
            Notifier(nameof(Levable));
            Suspendre.Reevaluer();
            Lever.Reevaluer();
        }
    }

    public bool ADesBoutique => _boutique is not null;

    /// <summary>Toute boutique non déjà suspendue peut l'être.</summary>
    public bool Suspendable => _boutique is { Suspendue: false };

    /// <summary>Seule une boutique suspendue par la plateforme se lève.</summary>
    /// <remarks>
    /// NE PAS OFFRIR « LEVER » SUR UNE BOUTIQUE `Closed`.
    ///
    /// `Closed` est la fermeture décidée par le VENDEUR — congés, travaux — et
    /// elle lui appartient : « réversible d'un geste, et c'est ce qui la
    /// distingue de la suspension ». Un bouton « lever » dessus laisserait croire
    /// que la plateforme peut rouvrir une boutique que son gérant a fermée.
    /// </remarks>
    public bool Levable => _boutique is { Suspendue: true };

    public string PositionBoutiques => Boutiques.Count == 0
        ? "Aucune boutique"
        : $"{Boutiques.Count} boutique(s), dont {Boutiques.Count(b => b.EnVente)} en vente";

    public string? ErreurBoutiques
    {
        get => _erreurBoutiques;
        private set { if (Definir(ref _erreurBoutiques, value)) Notifier(nameof(AErreurBoutiques)); }
    }

    public bool AErreurBoutiques => !string.IsNullOrEmpty(_erreurBoutiques);

    public CommandeAsync ChargerBoutiques { get; }

    public CommandeAsync Suspendre { get; }

    public CommandeAsync Lever { get; }

    public CommandeAsync Chercher { get; }

    public CommandeAsync Precedente { get; }

    public CommandeAsync Suivante { get; }

    public CommandeAsync<GesteVendeur> Agir { get; }

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
            var resultat = await _api.ListerVendeursAsync(
                page, Taille, _recherche, Vide(_filtreKyb), Vide(_filtreStatut));

            if (!resultat.Reussi || resultat.Valeur?.Data is null)
            {
                Erreur = resultat.Message ?? "Liste indisponible.";
                return;
            }

            // La sélection est retirée AVANT de vider la liste : conservée, elle
            // désignerait un vendeur absent de la page affichée, et le panneau de
            // droite proposerait des gestes sur une fiche qu'on ne voit plus.
            Selection = null;
            Vendeurs.Clear();

            foreach (var vendeur in resultat.Valeur.Data)
            {
                Vendeurs.Add(vendeur);
            }

            _page = resultat.Valeur.Meta?.Page ?? page;
            _total = resultat.Valeur.Meta?.Total ?? Vendeurs.Count;
            _pageSuivante = resultat.Valeur.Meta?.HasNext ?? false;

            Notifier(nameof(Position));
        }
        finally
        {
            EnCours = false;
        }
    }

    /// <summary>Applique un geste au vendeur sélectionné.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// L'ORDRE DES TROIS ÉTAPES EST CE QUI REND CET ÉCRAN SÛR.
    ///
    ///   1. LE MOTIF, quand le geste l'exige. merchant-service refuse en 400 sans
    ///      lui — mais surtout, le motif part au vendeur : c'est ce qu'il lira.
    ///   2. L'ÉLÉVATION, quand le geste est destructeur. On ne la demande QUE si
    ///      la session n'est plus assez fraîche : redemander le mot de passe à
    ///      chaque clic conduit à le taper machinalement, ce qui retire à cette
    ///      demande tout ce qui la rend utile.
    ///   3. LE GESTE.
    ///
    /// Renoncer à n'importe laquelle des deux premières annule tout. Il n'y a pas
    /// d'état intermédiaire à nettoyer : rien n'a encore été envoyé.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private async Task AgirAsync(GesteVendeur geste)
    {
        if (_selection is not { } vendeur)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        string? motif = null;

        if (geste.MotifExige)
        {
            motif = await _saisie.MotifAsync($"{geste.Libelle} · {vendeur.ShopName}");

            if (string.IsNullOrWhiteSpace(motif))
            {
                return;
            }
        }

        if (geste.Destructeur && !_api.ElevationValide)
        {
            var mot = await _saisie.MotDePasseAsync($"{geste.Libelle} · {vendeur.ShopName}");

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
            var resultat = await _api.AgirSurVendeurAsync(vendeur.Id, geste, motif);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = $"{geste.Libelle} — {vendeur.ShopName}.";
        }
        finally
        {
            EnCours = false;
        }

        // La liste est rechargée APRÈS le geste : le statut affiché vient du
        // serveur, jamais d'une supposition locale. Un geste refusé par une règle
        // métier que le client ignore laisserait sinon une ligne mise à jour à
        // l'écran et inchangée en base.
        await ChargerAsync(_page);
    }

    private static string? Vide(string? valeur)
        => string.IsNullOrWhiteSpace(valeur) || valeur == Tous ? null : valeur;

    /// <remarks>
    /// L'ÉCHEC DE CE CHARGEMENT NE TOUCHE PAS `Erreur`.
    ///
    /// Le panneau des boutiques est un complément : un 403 dessus — le cas d'un
    /// modérateur, dont le rôle ne court-circuite PAS la garde de
    /// merchant-service — ne doit pas effacer le message d'un geste de
    /// gouvernance qui vient d'aboutir.
    /// </remarks>
    private async Task ChargerBoutiquesAsync()
    {
        if (_selection is not { } vendeur)
        {
            return;
        }

        ErreurBoutiques = null;

        var resultat = await _api.ListerBoutiquesAsync(vendeur.Id);

        if (!resultat.Reussi || resultat.Valeur is null)
        {
            ErreurBoutiques = resultat.Message ?? "Boutiques indisponibles.";
            return;
        }

        var choisie = _boutique?.Id;

        SelectionBoutique = null;
        Boutiques.Clear();

        foreach (var boutique in resultat.Valeur.OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Boutiques.Add(new LigneBoutique(boutique));
        }

        SelectionBoutique = Boutiques.FirstOrDefault(b => b.Id == choisie);

        Notifier(nameof(PositionBoutiques));
    }

    /// <remarks>
    /// LE MOTIF EST EXIGÉ ICI ALORS QUE LE SERVEUR L'ACCEPTE VIDE.
    ///
    /// `ReasonRequest(string? Reason)` est nullable. Mais le motif atterrit dans
    /// `StatusReason`, que la vitrine publique n'expose pas, et c'est la SEULE
    /// trace de la raison d'une sanction. Une suspension sans motif se retrouve
    /// un mois plus tard sans que personne ne sache pourquoi elle a été posée —
    /// ni si elle peut être levée.
    /// </remarks>
    private async Task BasculerBoutiqueAsync(bool suspendre)
    {
        if (_selection is not { } vendeur || _boutique is not { } boutique)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;
        ErreurBoutiques = null;

        string? motif = null;

        if (suspendre)
        {
            motif = await _saisie.MotifAsync($"Motif de la suspension de « {boutique.Nom} »");

            if (string.IsNullOrWhiteSpace(motif))
            {
                return;
            }
        }

        var mot = _api.ElevationValide
            ? null
            : await _saisie.MotDePasseAsync(suspendre
                ? $"Suspendre la boutique {boutique.Nom}"
                : $"Lever la suspension de {boutique.Nom}");

        if (!_api.ElevationValide)
        {
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
            var resultat = await _api.BasculerBoutiqueAsync(
                vendeur.Id, boutique.Id, suspendre, motif?.Trim());

            if (!resultat.Reussi)
            {
                ErreurBoutiques = resultat.Message;
                return;
            }

            Confirmation = suspendre
                ? $"Boutique « {boutique.Nom} » suspendue. Le vendeur ne peut pas la rouvrir lui-même."
                : $"Suspension de « {boutique.Nom} » levée. Elle revient à l'état où le vendeur l'avait laissée.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerBoutiquesAsync();
    }
}

/// <summary>Une boutique, telle que le panneau l'affiche.</summary>
public sealed class LigneBoutique
{
    public LigneBoutique(BoutiqueAdmin boutique)
    {
        Id = boutique.Id;
        Nom = boutique.Name;
        Statut = boutique.Status;
        EnVente = boutique.IsSelling;
        Suspendue = boutique.Status == "Suspended";
        Motif = boutique.StatusReason ?? string.Empty;
        AMotif = !string.IsNullOrWhiteSpace(boutique.StatusReason);
        Contact = boutique.ContactPhone;

        Etat = boutique.Status switch
        {
            "Open" => "ouverte",
            "Closed" => "fermée par le vendeur",
            "Suspended" => "suspendue par la plateforme",
            "Draft" => "jamais ouverte",
            _ => boutique.Status.ToLowerInvariant(),
        };

        // « OUVERTE » ET « EN VENTE » NE SONT PAS LA MÊME CHOSE.
        //
        // `IsSelling` répond « ses offres sont-elles achetables EN CE MOMENT » —
        // une boutique ouverte hors de ses horaires ne vend pas. Afficher le seul
        // statut ferait chercher une panne là où il n'y a qu'un jeudi soir.
        Vente = boutique.IsSelling ? "en vente" : "pas de vente en cours";

        SansLieu = boutique.FulfillmentLocationId is null;
    }

    public Guid Id { get; }

    public string Nom { get; }

    public string Statut { get; }

    public string Etat { get; }

    public bool EnVente { get; }

    public string Vente { get; }

    public bool Suspendue { get; }

    public string Motif { get; }

    public bool AMotif { get; }

    public string Contact { get; }

    /// <summary>
    /// Aucun lieu d'expédition rattaché.
    /// </summary>
    /// <remarks>
    /// Le lieu vit dans Inventory et n'est ici qu'un identifiant. Son absence est
    /// un signal : une boutique sans lieu ne peut rien faire enlever, et c'est le
    /// genre de dossier incomplet qu'on ne voit pas depuis la fiche vendeur.
    /// </remarks>
    public bool SansLieu { get; }
}
