using System.Collections.ObjectModel;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>La file de modération des avis.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// CET ÉCRAN COMPLÈTE « MODÉRATION », QUI NE COUVRAIT QUE LES RESTAURANTS.
///
/// L'audit le notait : « les restaurants ont leur file ; les avis n'en ont pas.
/// Aucune route ne rend les avis signalés, alors que flag, reject et restore
/// existent. » La route manquante a été ouverte
/// (`GET /api/engagement/reviews/moderation`) et cet écran la consomme.
///
/// TROIS GESTES QUI RÉÉCRIVENT UNE RÉPUTATION.
///
/// La note d'un produit et celle d'un vendeur ne comptent que les avis
/// `Published`. Rejeter retire de la moyenne, restaurer y remet. Ce n'est pas de
/// la mise en forme : c'est le classement d'une boutique dans la vitrine.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class ModerationAvisViewModel : ViewModelBase
{
    private readonly ClientApiAdmin _api;
    private readonly IDemandeurDeSaisie _saisie;

    private LigneAvis? _selection;
    private string? _statut = "Flagged";
    private int _page = 1;
    private long _total;
    private bool _suivante;
    private string? _erreur;
    private string? _confirmation;
    private bool _enCours;

    private const int Taille = 25;

    public ModerationAvisViewModel(ClientApiAdmin api, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _saisie = saisie;

        Rafraichir = new CommandeAsync(ChargerAsync);
        Precedente = new CommandeAsync(() => AllerAsync(_page - 1), () => _page > 1 && !EnCours);
        Suivante = new CommandeAsync(() => AllerAsync(_page + 1), () => _suivante && !EnCours);
        Filtrer = new CommandeAsync<string>(FiltrerAsync, _ => !EnCours);
        Signaler = new CommandeAsync(() => AgirAsync("flag"), () => Signalable && !EnCours);
        Rejeter = new CommandeAsync(() => AgirAsync("reject"), () => Rejetable && !EnCours);
        Restaurer = new CommandeAsync(() => AgirAsync("restore"), () => Restaurable && !EnCours);

        Rafraichir.Execute(null);
    }

    public ObservableCollection<LigneAvis> Avis { get; } = [];

    public ObservableCollection<LigneFacette> Facettes { get; } = [];

    public LigneAvis? Selection
    {
        get => _selection;
        set
        {
            if (!Definir(ref _selection, value))
            {
                return;
            }

            Notifier(nameof(ADesSelection));
            Notifier(nameof(Signalable));
            Notifier(nameof(Rejetable));
            Notifier(nameof(Restaurable));
            Reevaluer();
        }
    }

    public bool ADesSelection => _selection is not null;

    /// <summary>Signaler n'a de sens que sur un avis encore publié.</summary>
    public bool Signalable => _selection is { Statut: "Published" };

    /// <summary>Rejeter s'applique au publié comme au signalé, pas au déjà rejeté.</summary>
    public bool Rejetable => _selection is { Statut: not "Rejected" };

    /// <summary>Restaurer ne concerne que ce qui a été retiré de la vitrine.</summary>
    public bool Restaurable => _selection is { Statut: "Rejected" or "Flagged" };

    public string Position => _total == 0
        ? "Aucun avis"
        : $"{_total} avis  ·  page {_page}";

    /// <summary>Ce que la file ne dit pas, et qu'il vaut mieux savoir.</summary>
    public string Limite =>
        "Rien n'enregistre QUI a signalé un avis ni pourquoi : le domaine ne porte "
        + "qu'un statut. Un avis en « signalé » attend donc une relecture humaine sans "
        + "motif joint — c'est le texte lui-même qu'il faut juger.";

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

    public CommandeAsync Signaler { get; }

    public CommandeAsync Rejeter { get; }

    public CommandeAsync Restaurer { get; }

    private void Reevaluer()
    {
        Precedente.Reevaluer();
        Suivante.Reevaluer();
        Filtrer.Reevaluer();
        Signaler.Reevaluer();
        Rejeter.Reevaluer();
        Restaurer.Reevaluer();
    }

    private async Task ChargerAsync()
    {
        EnCours = true;
        Erreur = null;

        try
        {
            var choisi = _selection?.Id;
            var page = await _api.ListerAvisAsync(_page, Taille, _statut);

            if (!page.Reussi || page.Valeur?.Data is null)
            {
                Erreur = page.Message ?? "File de modération indisponible.";
                return;
            }

            Selection = null;
            Avis.Clear();

            foreach (var avis in page.Valeur.Data)
            {
                Avis.Add(new LigneAvis(avis));
            }

            _total = page.Valeur.Meta?.Total ?? Avis.Count;
            _suivante = page.Valeur.Meta?.HasNext ?? false;

            RemplirFacettes(page.Valeur.Meta?.Facets);

            Selection = Avis.FirstOrDefault(a => a.Id == choisi);

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

        // Trois statuts seulement, et c'est le nombre de « signalé » qui dit s'il
        // y a du travail : l'ordre le met en premier.
        foreach (var (cle, libelle) in new[]
                 {
                     ("Flagged", "Signalés"),
                     ("Published", "Publiés"),
                     ("Rejected", "Rejetés"),
                 })
        {
            var present = facettes.TryGetValue(cle, out var nombre);

            if (present || cle == _statut)
            {
                Facettes.Add(new LigneFacette(cle, present ? nombre : 0, cle == _statut, libelle));
            }
        }
    }

    private async Task AllerAsync(int page)
    {
        _page = Math.Max(1, page);
        await ChargerAsync();
    }

    private async Task FiltrerAsync(string statut)
    {
        _statut = string.Equals(_statut, statut, StringComparison.Ordinal) ? null : statut;
        _page = 1;
        await ChargerAsync();
    }

    /// <remarks>
    /// LE MOT DE PASSE EST EXIGÉ POUR LE REJET ET LA RESTAURATION, PAS POUR LE
    ///    SIGNALEMENT.
    ///
    /// Signaler ne retire rien de la vitrine — c'est une mise de côté, réversible
    /// et sans effet sur la note. Rejeter et restaurer, eux, changent la moyenne
    /// affichée sur une fiche produit et sur une boutique.
    /// </remarks>
    private async Task AgirAsync(string geste)
    {
        if (_selection is not { } avis)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        if (geste is "reject" or "restore")
        {
            var mot = _api.ElevationValide
                ? null
                : await _saisie.MotDePasseAsync(geste == "reject"
                    ? $"Rejeter l'avis « {avis.Titre} »"
                    : $"Restaurer l'avis « {avis.Titre} »");

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
        }

        EnCours = true;

        try
        {
            var resultat = await _api.ModererAvisAsync(avis.Id, geste);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = geste switch
            {
                "flag" => "Avis signalé. Il reste dans la moyenne tant qu'il n'est pas rejeté.",
                "reject" => "Avis rejeté. Il sort de la note du produit et de celle du vendeur.",
                _ => "Avis restauré. Il revient dans les deux moyennes.",
            };
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }
}

/// <summary>Un avis, tel que la file l'affiche.</summary>
public sealed class LigneAvis
{
    public LigneAvis(AvisAdmin avis)
    {
        Id = avis.Id;
        Statut = avis.Status;
        Titre = string.IsNullOrWhiteSpace(avis.Title) ? "(sans titre)" : avis.Title;
        Corps = avis.Body;
        Note = new string('★', Math.Clamp(avis.Rating, 0, 5)).PadRight(5, '☆');
        Achat = avis.IsVerifiedPurchase;
        Produit = avis.ProductId.ToString();
        Vendeur = avis.SellerId.ToString();
        Depose = avis.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy");

        Etat = avis.Status switch
        {
            "Published" => "publié",
            "Flagged" => "signalé",
            "Rejected" => "rejeté",
            _ => avis.Status.ToLowerInvariant(),
        };

        Reponse = avis.SellerReply ?? string.Empty;
        ARepondu = !string.IsNullOrWhiteSpace(avis.SellerReply);
    }

    public Guid Id { get; }

    public string Statut { get; }

    public string Etat { get; }

    public string Titre { get; }

    public string Corps { get; }

    /// <summary>La note en étoiles, pleines puis vides.</summary>
    public string Note { get; }

    /// <summary>
    /// L'avis vient d'un achat vérifié.
    /// </summary>
    /// <remarks>
    /// C'est le premier critère de modération : un avis sans achat vérifié est
    /// celui qu'on relit d'abord, parce que c'est là que se logent les faux.
    /// </remarks>
    public bool Achat { get; }

    public bool SansAchat => !Achat;

    public string Produit { get; }

    public string Vendeur { get; }

    public string Depose { get; }

    public string Reponse { get; }

    public bool ARepondu { get; }

    public bool EstPublie => Statut == "Published";
}
