using System.Collections.ObjectModel;
using System.Globalization;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Ce que la plateforme met en avant.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// ÉCRIRE UNE RECOMMANDATION, C'EST ÉCRIRE LA PAGE D'ACCUEIL.
///
/// C'est le commentaire du service, et il vaut pour cet écran. La route
/// d'écriture acceptait autrefois la commande brute de n'importe quel inscrit :
/// on choisissait les produits mis en avant sur la fiche d'un concurrent. Elle
/// est passée sur le groupe admin ; cet écran est la première façon de VOIR ce
/// qui a été écrit.
///
/// L'ENREGISTREMENT REMPLACE, IL N'AJOUTE PAS.
///
/// La clé fonctionnelle est (type, contexte) : par utilisateur pour
/// `Personalized`, par produit pour les deux autres. Réécrire une clé appelle
/// `Refresh`, qui remplace la liste entière et le score. Cet écran charge donc
/// la liste existante dans le formulaire quand on sélectionne une ligne, pour
/// qu'un remplacement ne se fasse jamais depuis un champ vide.
///
/// CE QUE CET ÉCRAN NE FAIT PAS : nommer les produits. Une recommandation ne
/// porte que des identifiants, et le service n'a aucun accès au catalogue. Ils
/// se copient depuis l'écran Produits.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class RecommandationsViewModel : ViewModelBase
{
    private readonly ClientApiAdmin _api;
    private readonly IDemandeurDeSaisie _saisie;

    private LigneRecommandation? _selection;
    private string? _type;
    private int _page = 1;
    private long _total;
    private bool _suivante;

    private bool _creation;
    private string _typeSaisi = "Similar";
    private string _contexte = string.Empty;
    private string _produits = string.Empty;
    private string _score = "1";

    private string? _erreur;
    private string? _confirmation;
    private bool _enCours;

    private const int Taille = 25;

    /// <summary>Les trois types, orthographiés comme l'énumération du domaine.</summary>
    /// <remarks>
    /// `Enum.TryParse` est appelé avec `ignoreCase: true` des deux côtés — au
    /// filtre comme à l'écriture — mais on envoie la forme exacte : un type mal
    /// orthographié au filtre est IGNORÉ en silence (la page complète revient),
    /// alors qu'à l'écriture il rend une erreur de validation. Deux traitements
    /// pour la même faute de frappe, d'où la liste fermée.
    /// </remarks>
    public IReadOnlyList<string> Types { get; } = ["Similar", "FrequentlyBoughtTogether", "Personalized"];

    public RecommandationsViewModel(ClientApiAdmin api, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _saisie = saisie;

        Rafraichir = new CommandeAsync(ChargerAsync);
        Precedente = new CommandeAsync(() => AllerAsync(_page - 1), () => _page > 1 && !EnCours);
        Suivante = new CommandeAsync(() => AllerAsync(_page + 1), () => _suivante && !EnCours);
        Filtrer = new CommandeAsync<string>(FiltrerAsync, _ => !EnCours);

        Nouvelle = new CommandeAsync(NouvelleAsync, () => !EnCours);
        Enregistrer = new CommandeAsync(EnregistrerAsync, () => EnEdition && !EnCours);
        Abandonner = new CommandeAsync(AbandonnerAsync, () => EnEdition && !EnCours);

        Rafraichir.Execute(null);
    }

    public ObservableCollection<LigneRecommandation> Recommandations { get; } = [];

    public ObservableCollection<LigneFacette> Facettes { get; } = [];

    public LigneRecommandation? Selection
    {
        get => _selection;
        set
        {
            if (!Definir(ref _selection, value))
            {
                return;
            }

            if (value is not null)
            {
                _creation = false;
                Remplir(value);
            }

            Notifier(nameof(ADesSelection));
            Notifier(nameof(EnEdition));
            Notifier(nameof(CleModifiable));
            Notifier(nameof(Titre));
            Notifier(nameof(Consigne));
            Reevaluer();
        }
    }

    public bool ADesSelection => _selection is not null;

    public bool EnEdition => _selection is not null || _creation;

    /// <summary>Le type et le contexte ne se choisissent qu'à la création.</summary>
    /// <remarks>
    /// Ce n'est pas une contrainte du serveur — la commande accepte n'importe
    /// quelle clé. C'est la conséquence de l'upsert : changer le type ou le
    /// contexte en gardant les produits sous les yeux n'édite pas la ligne
    /// affichée, cela en écrit une AUTRE, et laisse la première intacte. Deux
    /// recommandations là où l'on croyait en corriger une.
    /// </remarks>
    public bool CleModifiable => _creation;

    public string Titre => _creation
        ? "Nouvelle recommandation"
        : _selection is null ? "Recommandation" : _selection.Resume;

    /// <summary>Ce que l'enregistrement va faire, dit avant le clic.</summary>
    public string Consigne => _creation
        ? "Si cette clé (type + contexte) existe déjà, l'enregistrement REMPLACERA sa liste "
          + "et son score, sans avertissement. Vérifiez d'abord la liste de gauche."
        : _selection is null
            ? string.Empty
            : "L'enregistrement remplace la liste entière de cette clé — il n'ajoute pas. "
              + "Les identifiants ci-dessous sont ceux déjà enregistrés : retirez ou ajoutez "
              + "à partir d'eux.";

    public string TypeSaisi
    {
        get => _typeSaisi;
        set { if (Definir(ref _typeSaisi, value)) Notifier(nameof(LibelleContexte)); }
    }

    /// <summary>Le contexte change de nature selon le type, et le champ le dit.</summary>
    public string LibelleContexte => _typeSaisi == "Personalized"
        ? "Utilisateur destinataire"
        : "Produit dont c'est la fiche";

    public string Contexte
    {
        get => _contexte;
        set => Definir(ref _contexte, value);
    }

    public string Produits
    {
        get => _produits;
        set { if (Definir(ref _produits, value)) Notifier(nameof(CompteSaisi)); }
    }

    public string CompteSaisi
    {
        get
        {
            var lus = Decouper(_produits);

            if (lus.Count == 0)
            {
                return string.Empty;
            }

            var distincts = lus.Distinct().Count();

            // LE DOMAINE DÉDOUBLONNE EN SILENCE, ET LE COMPTE LE DIRA APRÈS COUP.
            //
            // `Create` comme `Refresh` appliquent `.Distinct()` : un doublon
            // disparaît sans erreur. Le dire ici évite de relire la ligne
            // enregistrée en croyant à une perte.
            return distincts == lus.Count
                ? $"{lus.Count} produit(s)"
                : $"{lus.Count} saisi(s), {distincts} retenu(s) — les doublons sont écartés par le domaine";
        }
    }

    public string Score
    {
        get => _score;
        set => Definir(ref _score, value);
    }

    public string? Type => _type;

    public string Position => _total == 0
        ? "Aucune recommandation enregistrée"
        : $"{_total} recommandation(s)  ·  page {_page}";

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
        private set { if (Definir(ref _enCours, value)) Reevaluer(); }
    }

    public CommandeAsync Rafraichir { get; }

    public CommandeAsync Precedente { get; }

    public CommandeAsync Suivante { get; }

    public CommandeAsync<string> Filtrer { get; }

    public CommandeAsync Nouvelle { get; }

    public CommandeAsync Enregistrer { get; }

    public CommandeAsync Abandonner { get; }

    private void Reevaluer()
    {
        Precedente.Reevaluer();
        Suivante.Reevaluer();
        Filtrer.Reevaluer();
        Nouvelle.Reevaluer();
        Enregistrer.Reevaluer();
        Abandonner.Reevaluer();
    }

    private async Task ChargerAsync()
    {
        EnCours = true;
        Erreur = null;

        try
        {
            var choisie = _selection?.Id;
            var page = await _api.ListerRecommandationsAsync(_page, Taille, _type);

            if (!page.Reussi || page.Valeur?.Data is null)
            {
                Erreur = page.Message ?? "Recommandations indisponibles.";
                return;
            }

            Selection = null;
            Recommandations.Clear();

            foreach (var recommandation in page.Valeur.Data)
            {
                Recommandations.Add(new LigneRecommandation(recommandation));
            }

            _total = page.Valeur.Meta?.Total ?? Recommandations.Count;
            _suivante = page.Valeur.Meta?.HasNext ?? false;

            RemplirFacettes(page.Valeur.Meta?.Facets);

            Selection = Recommandations.FirstOrDefault(r => r.Id == choisie);

            Notifier(nameof(Position));
        }
        finally
        {
            EnCours = false;
        }
    }

    private void RemplirFacettes(IReadOnlyDictionary<string, int>? facettes)
    {
        Facettes.Clear();

        if (facettes is null)
        {
            return;
        }

        foreach (var cle in Types)
        {
            var present = facettes.TryGetValue(cle, out var nombre);

            if (present && nombre > 0 || cle == _type)
            {
                Facettes.Add(new LigneFacette(cle, present ? nombre : 0, cle == _type, Libelle(cle)));
            }
        }
    }

    private static string Libelle(string cle) => cle switch
    {
        "Similar" => "Produits similaires",
        "FrequentlyBoughtTogether" => "Achetés ensemble",
        "Personalized" => "Personnalisées",
        _ => cle,
    };

    private async Task AllerAsync(int page)
    {
        _page = Math.Max(1, page);
        await ChargerAsync();
    }

    private async Task FiltrerAsync(string type)
    {
        _type = string.Equals(_type, type, StringComparison.Ordinal) ? null : type;
        _page = 1;
        await ChargerAsync();
    }

    private void Remplir(LigneRecommandation ligne)
    {
        _typeSaisi = ligne.Type;
        _contexte = ligne.CleBrute?.ToString() ?? string.Empty;
        _produits = string.Join("\n", ligne.ProduitsBruts);
        _score = ligne.ScoreBrut.ToString(CultureInfo.InvariantCulture);

        Notifier(nameof(TypeSaisi));
        Notifier(nameof(LibelleContexte));
        Notifier(nameof(Contexte));
        Notifier(nameof(Produits));
        Notifier(nameof(CompteSaisi));
        Notifier(nameof(Score));
    }

    private Task NouvelleAsync()
    {
        Erreur = null;
        Confirmation = null;

        Selection = null;
        _creation = true;

        _typeSaisi = "Similar";
        _contexte = string.Empty;
        _produits = string.Empty;
        _score = "1";

        Notifier(nameof(TypeSaisi));
        Notifier(nameof(LibelleContexte));
        Notifier(nameof(Contexte));
        Notifier(nameof(Produits));
        Notifier(nameof(CompteSaisi));
        Notifier(nameof(Score));
        Notifier(nameof(EnEdition));
        Notifier(nameof(CleModifiable));
        Notifier(nameof(Titre));
        Notifier(nameof(Consigne));
        Reevaluer();

        return Task.CompletedTask;
    }

    private Task AbandonnerAsync()
    {
        _creation = false;
        Selection = null;

        Notifier(nameof(EnEdition));
        Notifier(nameof(CleModifiable));
        Notifier(nameof(Titre));
        Notifier(nameof(Consigne));
        Reevaluer();

        return Task.CompletedTask;
    }

    private async Task EnregistrerAsync()
    {
        Erreur = null;
        Confirmation = null;

        if (!Lire(out var valeurs, out var probleme))
        {
            Erreur = probleme;
            return;
        }

        // LE REMPLACEMENT SILENCIEUX EST CE QUI JUSTIFIE LA CONFIRMATION.
        //
        // Rien côté serveur ne distingue une création d'un écrasement : la même
        // route rend 201 dans les deux cas. Sur une clé déjà connue de la liste,
        // on demande donc un motif — il ne part nulle part, il force à regarder
        // ce qu'on remplace.
        var existante = Recommandations.FirstOrDefault(
            r => r.Type == valeurs.Type && r.CleBrute == valeurs.Cle);

        if (existante is not null)
        {
            var motif = await _saisie.MotifAsync(
                $"Cette clé porte déjà {existante.NombreProduits} produit(s). L'enregistrement "
                + "REMPLACE la liste entière. Motif du remplacement");

            if (string.IsNullOrWhiteSpace(motif))
            {
                return;
            }
        }

        var mot = _api.ElevationValide
            ? null
            : await _saisie.MotDePasseAsync("Écrire une recommandation de la plateforme");

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
            var resultat = await _api.EnregistrerRecommandationAsync(
                valeurs.Type, valeurs.Produit, valeurs.Utilisateur, valeurs.Produits, valeurs.Score);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            _creation = false;

            Confirmation = existante is null
                ? $"Recommandation enregistrée — {valeurs.Produits.Count} produit(s) mis en avant."
                : $"Liste remplacée : {existante.NombreProduits} produit(s) écartés, "
                  + $"{valeurs.Produits.Count} enregistrés.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    /// <remarks>
    /// LES REFUS ICI SONT CEUX DU DOMAINE, RECOPIÉS POUR NOMMER LE CHAMP.
    ///
    /// `UpsertRecommendationCommandHandler` refuse un type inconnu ; il n'exige
    /// EN REVANCHE aucun contexte. Une commande sans produit ni utilisateur passe
    /// la validation, `existing` reste nul, et une recommandation orpheline est
    /// créée — qu'aucune des trois lectures ne retrouvera jamais, puisque toutes
    /// sont adressées. L'écran l'interdit ; le serveur, non.
    /// </remarks>
    private bool Lire(out ValeursRecommandation valeurs, out string? probleme)
    {
        valeurs = default;
        probleme = null;

        if (!Types.Contains(_typeSaisi))
        {
            probleme = "Choisissez l'un des trois types.";
            return false;
        }

        if (!Guid.TryParse(_contexte.Trim(), out var cle))
        {
            probleme = _typeSaisi == "Personalized"
                ? "Une recommandation personnalisée vise un utilisateur : son identifiant se "
                  + "copie depuis l'écran Utilisateurs."
                : "Une recommandation de produit vise la fiche d'un produit : son identifiant "
                  + "se copie depuis l'écran Produits.";
            return false;
        }

        var produits = Decouper(_produits).Distinct().ToList();

        if (produits.Count == 0)
        {
            probleme = "Il faut au moins un identifiant de produit à mettre en avant, un par ligne.";
            return false;
        }

        if (!double.TryParse(_score.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
        {
            probleme = "Le score doit être un nombre. Le domaine ne le borne pas et aucune "
                       + "lecture ne trie dessus : c'est une métadonnée du moteur de calcul.";
            return false;
        }

        var personnalisee = _typeSaisi == "Personalized";

        valeurs = new ValeursRecommandation(
            _typeSaisi,
            cle,
            personnalisee ? null : cle,
            personnalisee ? cle : null,
            produits,
            score);

        return true;
    }

    /// <summary>Un identifiant par ligne, virgules et espaces tolérés.</summary>
    private static List<Guid> Decouper(string saisie)
    {
        var lus = new List<Guid>();

        if (string.IsNullOrWhiteSpace(saisie))
        {
            return lus;
        }

        foreach (var morceau in saisie.Split(['\n', '\r', ',', ';', ' ', '\t'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(morceau, out var identifiant) && identifiant != Guid.Empty)
            {
                lus.Add(identifiant);
            }
        }

        return lus;
    }

    private readonly record struct ValeursRecommandation(
        string Type, Guid Cle, Guid? Produit, Guid? Utilisateur, IReadOnlyList<Guid> Produits, double Score);
}

/// <summary>Une recommandation, telle que la liste l'affiche.</summary>
public sealed class LigneRecommandation
{
    public LigneRecommandation(RecommandationAdmin recommandation)
    {
        Id = recommandation.Id;
        Type = recommandation.Type;
        ProduitsBruts = recommandation.RecommendedProductIds;
        ScoreBrut = recommandation.Score;

        // La clé fonctionnelle : l'un des deux est nul, jamais les deux — sauf
        // sur une ligne orpheline, que la commande accepte de créer et
        // qu'aucune lecture adressée ne retrouve. La liste, elle, la montre.
        CleBrute = recommandation.ContextProductId ?? recommandation.UserId;

        Orpheline = recommandation.ContextProductId is null && recommandation.UserId is null;

        var nature = recommandation.Type switch
        {
            "Similar" => "similaires à",
            "FrequentlyBoughtTogether" => "achetés avec",
            "Personalized" => "pour",
            _ => recommandation.Type,
        };

        Resume = Orpheline
            ? "sans contexte"
            : $"{nature} {CleBrute!.Value.ToString()[..8]}";

        NombreProduits = recommandation.RecommendedProductIds.Count;

        Produits = NombreProduits == 0
            ? "aucun produit"
            : $"{NombreProduits} produit(s) mis en avant";

        Calcule = recommandation.GeneratedAtUtc.ToLocalTime().ToString(
            "dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);

        Score = recommandation.Score.ToString("0.##", CultureInfo.CurrentCulture);
    }

    public Guid Id { get; }

    public string Type { get; }

    public Guid? CleBrute { get; }

    public IReadOnlyList<Guid> ProduitsBruts { get; }

    public double ScoreBrut { get; }

    public int NombreProduits { get; }

    /// <summary>Ni produit ni utilisateur : introuvable par les lectures adressées.</summary>
    public bool Orpheline { get; }

    public string Resume { get; }

    public string Produits { get; }

    public string Calcule { get; }

    public string Score { get; }
}
