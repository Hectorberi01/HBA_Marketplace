using System.Collections.ObjectModel;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Les alertes de stock, et les lieux où le stock se trouve.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// LES DEUX ROUTES SE COMPLÈTENT, ET C'EST TOUT L'INTÉRÊT DE LES CHARGER
///    ENSEMBLE.
///
/// `low-stock` rend des articles qui portent un `LocationId` et rien d'autre sur
/// le lieu ; `locations` rend les lieux avec leur commune, leur quartier et leur
/// téléphone. Une alerte réduite à un GUID d'entrepôt ne se traite pas : il faut
/// savoir OÙ aller. L'écran fait donc la jointure localement.
///
/// CE N'EST PAS UN INVENTAIRE, ET LE SERVEUR S'EN ASSURE.
///
/// `ListLowStockQueryHandler` plafonne à 200 quoi qu'on demande, avec ce motif :
/// « deux cents lignes sous seuil, c'est déjà plus que ce qu'un gestionnaire
/// traite dans sa journée ». Il n'existe aucune route qui rende tout le stock de
/// la plateforme, et c'est délibéré.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class StockViewModel : ViewModelBase
{
    private readonly ClientApiAdmin _api;

    private readonly List<LigneLieu> _lieux = [];
    private readonly List<LigneArticle> _articles = [];

    private string _recherche = string.Empty;
    private int _combien = 50;
    private string? _erreur;
    private bool _enCours;

    public StockViewModel(ClientApiAdmin api)
    {
        _api = api;

        Rafraichir = new CommandeAsync(ChargerAsync);

        Rafraichir.Execute(null);
    }

    public ObservableCollection<LigneArticle> Articles { get; } = [];

    public ObservableCollection<LigneLieu> Lieux { get; } = [];

    /// <summary>Volumes proposés — au-delà de 200 le serveur ne rendrait pas plus.</summary>
    public ObservableCollection<int> Volumes { get; } = [50, 100, 200];

    public int Combien
    {
        get => _combien;
        set { if (Definir(ref _combien, value)) Rafraichir.Execute(null); }
    }

    /// <summary>Filtre local sur le SKU et sur le lieu.</summary>
    public string Recherche
    {
        get => _recherche;
        set { if (Definir(ref _recherche, value)) Filtrer(); }
    }

    public string? Erreur
    {
        get => _erreur;
        private set { if (Definir(ref _erreur, value)) Notifier(nameof(EnErreur)); }
    }

    public bool EnErreur => !string.IsNullOrEmpty(_erreur);

    public bool EnCours
    {
        get => _enCours;
        private set => Definir(ref _enCours, value);
    }

    public string Position => Articles.Count == 0
        ? "Aucune alerte"
        : $"{Articles.Count} article(s) sous seuil, dont {Articles.Count(a => a.EnRupture)} sans stock vendable";

    public string PositionLieux => $"{_lieux.Count} lieu(x)";

    /// <summary>Le plafond serveur, dit à l'écran plutôt que découvert.</summary>
    public string Plafond =>
        "Cette liste est une alerte, pas un inventaire : le service la plafonne à 200 lignes, "
        + "et aucune route ne rend le stock complet de la plateforme.";

    public CommandeAsync Rafraichir { get; }

    private async Task ChargerAsync()
    {
        EnCours = true;
        Erreur = null;

        try
        {
            var lieux = await _api.ListerLieuxStockAsync();
            var articles = await _api.ListerStockBasAsync(_combien);

            var soucis = new List<string>();

            _lieux.Clear();
            Lieux.Clear();

            if (lieux.Reussi && lieux.Valeur is not null)
            {
                foreach (var lieu in lieux.Valeur.OrderBy(l => l.CommuneName, StringComparer.CurrentCultureIgnoreCase))
                {
                    var ligne = new LigneLieu(lieu);
                    _lieux.Add(ligne);
                    Lieux.Add(ligne);
                }
            }
            else
            {
                soucis.Add(lieux.Message ?? "Lieux indisponibles.");
            }

            // La jointure se fait sur ce qu'on a pu charger. Si les lieux
            // manquent, les alertes restent affichées avec leur GUID : une alerte
            // mal étiquetée vaut mieux qu'une alerte cachée.
            var parIdentifiant = _lieux.ToDictionary(l => l.Id);

            _articles.Clear();

            if (articles.Reussi && articles.Valeur is not null)
            {
                foreach (var article in articles.Valeur.OrderBy(a => a.Available).ThenBy(a => a.Sku, StringComparer.Ordinal))
                {
                    parIdentifiant.TryGetValue(article.LocationId, out var lieu);
                    _articles.Add(new LigneArticle(article, lieu));
                }
            }
            else
            {
                soucis.Add(articles.Message ?? "Alertes indisponibles.");
            }

            Filtrer();

            Erreur = soucis.Count == 0 ? null : string.Join(" ", soucis);

            Notifier(nameof(PositionLieux));
        }
        finally
        {
            EnCours = false;
        }
    }

    private void Filtrer()
    {
        var terme = _recherche.Trim();

        Articles.Clear();

        foreach (var article in _articles)
        {
            if (terme.Length == 0 || article.Correspond(terme))
            {
                Articles.Add(article);
            }
        }

        Notifier(nameof(Position));
    }
}

/// <summary>Un article sous seuil, enrichi de son lieu quand il est connu.</summary>
public sealed class LigneArticle
{
    public LigneArticle(ArticleStock article, LigneLieu? lieu)
    {
        Sku = article.Sku;
        Disponible = article.Available;
        Seuil = article.ReorderThreshold;

        // « EN RUPTURE » PORTE SUR LE DISPONIBLE, PAS SUR LE PHYSIQUE.
        //
        // Un article peut avoir des cartons en entrepôt et zéro vendable : la
        // différence est ce que des commandes en cours ont déjà réservé. Les deux
        // situations se règlent autrement, et l'écran les distingue.
        EnRupture = article.Available <= 0;
        SousReserve = article.Reserved > 0 && article.OnHand > article.Available;

        Detail = $"disponible {article.Available}  ·  physique {article.OnHand}"
                 + (article.Reserved > 0 ? $"  ·  réservé {article.Reserved}" : string.Empty)
                 + $"  ·  seuil {article.ReorderThreshold}";

        Lieu = lieu?.Libelle ?? article.LocationId.ToString();
        Contact = lieu?.Contact ?? string.Empty;
        AContact = !string.IsNullOrEmpty(Contact);
    }

    public string Sku { get; }

    public int Disponible { get; }

    public int Seuil { get; }

    public bool EnRupture { get; }

    /// <summary>Du stock physique existe, mais il est réservé.</summary>
    public bool SousReserve { get; }

    public string Detail { get; }

    public string Lieu { get; }

    public string Contact { get; }

    public bool AContact { get; }

    public bool Correspond(string terme)
        => Sku.Contains(terme, StringComparison.OrdinalIgnoreCase)
           || Lieu.Contains(terme, StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>Un lieu d'expédition.</summary>
public sealed class LigneLieu
{
    public LigneLieu(LieuStock lieu)
    {
        Id = lieu.Id;
        Type = lieu.Type;

        // Un lieu sans propriétaire est un entrepôt de la plateforme ; avec, c'est
        // le point d'enlèvement d'un vendeur. La liste mélange les deux, et rien
        // d'autre que ce champ ne les sépare.
        DeLaPlateforme = lieu.OwnerId is null;

        var morceaux = new List<string> { lieu.CommuneName };

        if (!string.IsNullOrWhiteSpace(lieu.Quartier))
        {
            morceaux.Add(lieu.Quartier);
        }

        if (!string.IsNullOrWhiteSpace(lieu.Landmark))
        {
            morceaux.Add(lieu.Landmark);
        }

        Libelle = string.Join(" · ", morceaux);
        Adresse = lieu.Line ?? string.Empty;
        Contact = lieu.ContactPhone ?? string.Empty;
        AContact = !string.IsNullOrEmpty(Contact);
        Nature = DeLaPlateforme ? "plateforme" : "vendeur";
    }

    public Guid Id { get; }

    public string Type { get; }

    public bool DeLaPlateforme { get; }

    public string Libelle { get; }

    public string Adresse { get; }

    public string Contact { get; }

    public bool AContact { get; }

    public string Nature { get; }
}
