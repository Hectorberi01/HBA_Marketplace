using System.Collections.ObjectModel;
using System.Globalization;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Les paiements, et les trois gestes de rattrapage.</summary>
public sealed class PaiementsViewModel : ViewModelBase
{
    private const int Taille = 25;

    private readonly ClientApiAdmin _api;
    private readonly IDemandeurDeSaisie _saisie;

    private LignePaiement? _selection;
    private string _recherche = string.Empty;
    private string? _filtreStatut;
    private string? _erreur;
    private string? _confirmation;
    private bool _enCours;
    private int _page = 1;
    private int _total;

    public PaiementsViewModel(ClientApiAdmin api, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _saisie = saisie;

        Chercher = new CommandeAsync(() => ChargerAsync(1));
        Precedente = new CommandeAsync(() => ChargerAsync(_page - 1), () => _page > 1 && !EnCours);
        Suivante = new CommandeAsync(() => ChargerAsync(_page + 1), () => APageSuivante && !EnCours);
        Agir = new CommandeAsync<GestePaiement>(AgirAsync, Applicable);

        Chercher.Execute(null);
    }

    public ObservableCollection<LignePaiement> Paiements { get; } = [];

    /// <summary>Les valeurs EXACTES de `PaymentStatus`.</summary>
    /// <remarks>
    /// LES CINQ, DANS L'ORDRE DU CYCLE DE VIE, ET PAS UNE DE PLUS.
    ///
    /// `Pending`, `Authorized`, `Captured`, `Failed`, `Refunded`. Une valeur
    /// inventée ne viderait pas la liste : comme ailleurs dans ce dépôt, un
    /// filtre illisible est ignoré, et l'écran afficherait TOUS les paiements en
    /// donnant l'apparence d'un filtre actif.
    /// </remarks>
    public IReadOnlyList<string> Statuts { get; } =
        [Tous, "Pending", "Authorized", "Captured", "Failed", "Refunded"];

    private const string Tous = "Tous";

    public IReadOnlyList<GestePaiement> Gestes { get; } = GestePaiement.Tous;

    public LignePaiement? Selection
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

    /// <summary>Les quatre chiffres de l'en-tête.</summary>
    public string Capture { get; private set; } = "—";

    public string EnAttente { get; private set; } = "—";

    public string Echoues { get; private set; } = "—";

    public string Rembourse { get; private set; } = "—";

    public string Position => _total == 0
        ? "Aucun paiement"
        : $"Page {_page} · {_total:N0} paiement(s)";

    private bool APageSuivante => (long)_page * Taille < _total;

    public CommandeAsync Chercher { get; }

    public CommandeAsync Precedente { get; }

    public CommandeAsync Suivante { get; }

    public CommandeAsync<GestePaiement> Agir { get; }

    /// <summary>
    /// Le geste s'applique-t-il au paiement sélectionné ?
    /// </summary>
    private bool Applicable(GestePaiement geste)
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

            // Les deux appels partent ENSEMBLE : l'en-tête et la liste décrivent
            // le même filtre, et l'écran ne montre jamais l'un sans l'autre.
            var listeTache = _api.ListerPaiementsAsync(page, Taille, _recherche, filtre);
            var statsTache = _api.LireStatsPaiementsAsync(_recherche);

            await Task.WhenAll(listeTache, statsTache);

            var liste = await listeTache;

            if (!liste.Reussi || liste.Valeur?.Items is null)
            {
                Erreur = liste.Message ?? "Liste indisponible.";
                return;
            }

            Selection = null;
            Paiements.Clear();

            foreach (var paiement in liste.Valeur.Items)
            {
                Paiements.Add(new LignePaiement(paiement));
            }

            _page = liste.Valeur.Page <= 0 ? page : liste.Valeur.Page;
            _total = liste.Valeur.Total;

            AppliquerStats(await statsTache);

            Notifier(nameof(Position));
        }
        finally
        {
            EnCours = false;
        }
    }

    /// <remarks>
    /// LE RÉSUMÉ EST FACULTATIF, LA LISTE NE L'EST PAS.
    ///
    /// `stats` est un second appel : s'il échoue quand la liste a réussi, l'écran
    /// reste utile. Les quatre chiffres retombent alors sur « — », qui ne se
    /// confond pas avec un zéro — même règle que les tuiles de l'accueil.
    /// </remarks>
    private void AppliquerStats(Resultat<StatsPaiements> resultat)
    {
        if (!resultat.Reussi || resultat.Valeur is not { } stats)
        {
            Capture = EnAttente = Echoues = Rembourse = "—";
        }
        else
        {
            Capture = $"{stats.CapturedCount:N0}  ·  {Argent.Formater(stats.CapturedAmount, "XOF")}";
            EnAttente = stats.PendingCount.ToString("N0", CultureInfo.CurrentCulture);
            Echoues = stats.FailedCount.ToString("N0", CultureInfo.CurrentCulture);
            Rembourse = $"{stats.RefundedCount:N0}  ·  {Argent.Formater(stats.RefundedAmount, "XOF")}";
        }

        Notifier(nameof(Capture));
        Notifier(nameof(EnAttente));
        Notifier(nameof(Echoues));
        Notifier(nameof(Rembourse));
    }

    private async Task AgirAsync(GestePaiement geste)
    {
        if (_selection is not { } ligne)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        string? saisie = null;

        if (geste.Saisie != SaisieRequise.Aucune)
        {
            var invite = $"{geste.Libelle} · {ligne.MontantAffiche}";

            saisie = geste.Saisie == SaisieRequise.Reference
                ? await _saisie.ReferenceAsync(invite)
                : await _saisie.MotifAsync(invite);

            if (string.IsNullOrWhiteSpace(saisie))
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
            var resultat = await _api.AgirSurPaiementAsync(ligne.Id, geste, saisie);

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

/// <summary>Un paiement, tel que la liste l'affiche.</summary>
public sealed class LignePaiement
{
    public LignePaiement(PaiementAdmin paiement)
    {
        Id = paiement.Id;
        Statut = paiement.Status;
        MontantAffiche = Argent.Formater(paiement.Amount, paiement.Currency);
        Commande = paiement.OrderId.ToString("N")[..8];
        Prestataire = $"{paiement.Provider} · {paiement.Method}";
        Reference = paiement.ProviderReference ?? string.Empty;
        AReference = !string.IsNullOrEmpty(paiement.ProviderReference);

        CreeLe = paiement.CreatedAtUtc.ToLocalTime()
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
    }

    public Guid Id { get; }

    public string MontantAffiche { get; }

    public string Statut { get; }

    public string Commande { get; }

    public string Prestataire { get; }

    /// <summary>La référence chez le prestataire — vide tant qu'il n'a pas répondu.</summary>
    public string Reference { get; }

    public bool AReference { get; }

    public string CreeLe { get; }
}
