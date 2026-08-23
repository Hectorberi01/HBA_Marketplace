using System.Collections.ObjectModel;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Les marques : le référentiel, et la file des demandes des vendeurs.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// DEUX LISTES SUR LE MÊME ÉCRAN, PARCE QUE LE GESTE UTILE LES RELIE.
///
/// Séparer « demandes » et « référentiel » en deux pages obligerait, pour
/// rattacher « samsumg » à « Samsung », à mémoriser un GUID en changeant de
/// page. Le rattachement est pourtant le cas FRÉQUENT — le domaine le dit
/// lui-même. Les deux listes cohabitent donc, et l'on approuve une demande EN
/// DÉSIGNANT la marque déjà sélectionnée à côté.
///
/// CE QUE CET ÉCRAN NE MONTRE PAS : l'historique des demandes tranchées. La
/// route ne rend que les demandes en attente, et il n'existe pas de route
/// acceptant un statut. Ce n'est pas un filtre oublié côté client.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class MarquesViewModel : ViewModelBase
{
    private readonly ClientApiAdmin _api;
    private readonly IDemandeurDeSaisie _saisie;
    private readonly List<LigneMarque> _referentiel = [];

    private LigneDemandeMarque? _demande;
    private LigneMarque? _marque;
    private string _recherche = string.Empty;
    private string _nom = string.Empty;
    private string _logo = string.Empty;
    private string _description = string.Empty;
    private string? _erreur;
    private string? _confirmation;
    private bool _enCours;

    public MarquesViewModel(ClientApiAdmin api, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _saisie = saisie;

        Rafraichir = new CommandeAsync(ChargerAsync);
        Creer = new CommandeAsync(CreerAsync, () => !EnCours);
        Enregistrer = new CommandeAsync(EnregistrerAsync, () => Modifiable && !EnCours);
        Publier = new CommandeAsync(() => BasculerAsync(true), () => Publiable && !EnCours);
        Depublier = new CommandeAsync(() => BasculerAsync(false), () => Depubliable && !EnCours);
        Supprimer = new CommandeAsync(SupprimerAsync, () => ADesMarque && !EnCours);
        Approuver = new CommandeAsync(() => TrancherAsync(null), () => ADesDemande && !EnCours);
        Rattacher = new CommandeAsync(() => TrancherAsync(_marque?.Id), () => PeutRattacher && !EnCours);
        Refuser = new CommandeAsync(RefuserAsync, () => ADesDemande && !EnCours);

        Rafraichir.Execute(null);
    }

    public ObservableCollection<LigneDemandeMarque> Demandes { get; } = [];

    public ObservableCollection<LigneMarque> Marques { get; } = [];

    /// <summary>La demande en cours d'examen.</summary>
    public LigneDemandeMarque? SelectionDemande
    {
        get => _demande;
        set
        {
            if (!Definir(ref _demande, value))
            {
                return;
            }

            Notifier(nameof(ADesDemande));
            Notifier(nameof(DemandeANote));
            Notifier(nameof(PeutRattacher));
            Notifier(nameof(LibelleRattachement));
            Reevaluer();
        }
    }

    /// <summary>La marque du référentiel en cours d'édition — et cible du rattachement.</summary>
    public LigneMarque? SelectionMarque
    {
        get => _marque;
        set
        {
            if (!Definir(ref _marque, value))
            {
                return;
            }

            // Les champs suivent la sélection, faute de quoi on éditerait la
            // marque précédente en croyant modifier celle qu'on vient de cliquer.
            Nom = value?.Nom ?? string.Empty;
            Logo = value?.Logo ?? string.Empty;
            Description = value?.Description ?? string.Empty;

            Notifier(nameof(ADesMarque));
            Notifier(nameof(Modifiable));
            Notifier(nameof(Publiable));
            Notifier(nameof(Depubliable));
            Notifier(nameof(Archivee));
            Notifier(nameof(PeutRattacher));
            Notifier(nameof(LibelleRattachement));
            Reevaluer();
        }
    }

    /// <summary>Filtre local sur le nom et le slug.</summary>
    /// <remarks>
    /// LOCAL PARCE QUE LA ROUTE N'ACCEPTE AUCUN PARAMÈTRE.
    ///
    /// `ListBrandsQuery` est un enregistrement vide : le service rend tout le
    /// référentiel, depuis un cache de donnée de référence. Filtrer ici ne peut
    /// donc pas manquer une marque restée côté serveur — ce qui ne serait PAS
    /// vrai sur une liste paginée comme celle des produits.
    /// </remarks>
    public string Recherche
    {
        get => _recherche;
        set { if (Definir(ref _recherche, value)) Filtrer(); }
    }

    public bool ADesDemande => _demande is not null;

    /// <summary>La demande porte-t-elle une note du vendeur ?</summary>
    /// <remarks>
    /// UNE PROPRIÉTÉ PLUTÔT QU'UN CHEMIN `SelectionDemande.ANote` DANS LA VUE.
    ///
    /// Sans sélection, le chemin rend `null` ; `IsVisible` retombe alors sur sa
    /// valeur par défaut, qui est VRAIE. Le bloc s'afficherait vide au lieu de
    /// disparaître.
    /// </remarks>
    public bool DemandeANote => _demande?.ANote ?? false;

    /// <summary>Vrai quand la file est vide — et non « quand elle est indisponible ».</summary>
    public bool AucuneDemande => Demandes.Count == 0;

    public bool ADesMarque => _marque is not null;

    public bool Modifiable => _marque is not null && !string.IsNullOrWhiteSpace(_nom);

    /// <summary>Une marque `Pending` se publie ; une marque archivée, non.</summary>
    /// <remarks>
    /// `Brand.Publish()` refuse explicitement l'état `Archived`, et `Unpublish()`
    /// aussi. Griser les deux boutons évite un aller-retour dont le message
    /// serveur serait la seule explication.
    /// </remarks>
    public bool Publiable => _marque is { Statut: "Pending" };

    public bool Depubliable => _marque is { Statut: "Active" };

    public bool Archivee => _marque is { Statut: "Archived" };

    public bool PeutRattacher => _demande is not null && _marque is not null;

    public string LibelleRattachement => _marque is null
        ? "Rattacher à la marque sélectionnée"
        : $"Rattacher à « {_marque.Nom} »";

    public string Nom
    {
        get => _nom;
        set { if (Definir(ref _nom, value)) { Notifier(nameof(Modifiable)); Reevaluer(); } }
    }

    /// <summary>URL du logo, telle que le serveur la stocke.</summary>
    /// <remarks>
    /// AUCUN TÉLÉVERSEMENT ICI : LE SERVICE N'ATTEND QU'UNE URL.
    ///
    /// `BrandRequest(Name, LogoUrl, Description)` porte une chaîne. Le dépôt a
    /// bien un service de médias, mais aucune route de catalogue ne le relie aux
    /// marques. Un bouton « Parcourir » ne pourrait donc rien envoyer.
    /// </remarks>
    public string Logo
    {
        get => _logo;
        set => Definir(ref _logo, value);
    }

    public string Description
    {
        get => _description;
        set => Definir(ref _description, value);
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
        private set { if (Definir(ref _enCours, value)) Reevaluer(); }
    }

    public string PositionDemandes => Demandes.Count == 0
        ? "Aucune demande en attente"
        : $"{Demandes.Count} demande(s) en attente";

    public string PositionMarques => _referentiel.Count == Marques.Count
        ? $"{_referentiel.Count} marque(s)"
        : $"{Marques.Count} sur {_referentiel.Count} marque(s)";

    public CommandeAsync Rafraichir { get; }

    public CommandeAsync Creer { get; }

    public CommandeAsync Enregistrer { get; }

    public CommandeAsync Publier { get; }

    public CommandeAsync Depublier { get; }

    public CommandeAsync Supprimer { get; }

    public CommandeAsync Approuver { get; }

    public CommandeAsync Rattacher { get; }

    public CommandeAsync Refuser { get; }

    private void Reevaluer()
    {
        Creer.Reevaluer();
        Enregistrer.Reevaluer();
        Publier.Reevaluer();
        Depublier.Reevaluer();
        Supprimer.Reevaluer();
        Approuver.Reevaluer();
        Rattacher.Reevaluer();
        Refuser.Reevaluer();
    }

    /// <remarks>
    /// LES DEUX LISTES SONT CHARGÉES ENSEMBLE, ET UN ÉCHEC N'EN CACHE PAS L'AUTRE.
    ///
    /// La file des demandes passe par le groupe admin, le référentiel par la
    /// route publique : ce sont deux autorisations différentes sur deux groupes
    /// différents. Si l'une échoue, l'autre s'affiche tout de même — sinon un
    /// droit manquant sur la file rendrait le référentiel invisible, et l'on
    /// chercherait la panne du mauvais côté.
    /// </remarks>
    private async Task ChargerAsync()
    {
        EnCours = true;
        Erreur = null;

        try
        {
            var choisieMarque = _marque?.Id;
            var choisieDemande = _demande?.Id;

            var marques = await _api.ListerMarquesAsync();
            var demandes = await _api.ListerDemandesMarqueAsync();

            var soucis = new List<string>();

            SelectionMarque = null;
            _referentiel.Clear();

            if (marques.Reussi && marques.Valeur is not null)
            {
                foreach (var marque in marques.Valeur.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    _referentiel.Add(new LigneMarque(marque));
                }
            }
            else
            {
                soucis.Add(marques.Message ?? "Référentiel indisponible.");
            }

            Filtrer();

            SelectionDemande = null;
            Demandes.Clear();

            if (demandes.Reussi && demandes.Valeur is not null)
            {
                foreach (var demande in demandes.Valeur)
                {
                    Demandes.Add(new LigneDemandeMarque(demande));
                }
            }
            else
            {
                soucis.Add(demandes.Message ?? "File des demandes indisponible.");
            }

            // On retrouve les sélections après rechargement : sans cela, chaque
            // geste ramènerait les panneaux à vide et il faudrait recliquer pour
            // poser le suivant.
            SelectionMarque = Marques.FirstOrDefault(m => m.Id == choisieMarque);
            SelectionDemande = Demandes.FirstOrDefault(d => d.Id == choisieDemande);

            Erreur = soucis.Count == 0 ? null : string.Join(" ", soucis);

            Notifier(nameof(PositionDemandes));
            Notifier(nameof(PositionMarques));
            Notifier(nameof(AucuneDemande));
        }
        finally
        {
            EnCours = false;
        }
    }

    private void Filtrer()
    {
        var terme = _recherche.Trim();
        var choisie = _marque?.Id;

        Marques.Clear();

        foreach (var ligne in _referentiel)
        {
            if (terme.Length == 0 || ligne.Correspond(terme))
            {
                Marques.Add(ligne);
            }
        }

        // La sélection ne survit pas à un filtre qui l'exclut : la garder
        // afficherait un panneau d'édition portant sur une marque absente de la
        // liste, et le bouton « Rattacher » désignerait une cible invisible.
        if (choisie is not null && Marques.All(m => m.Id != choisie))
        {
            SelectionMarque = null;
        }

        Notifier(nameof(PositionMarques));
    }

    private async Task CreerAsync()
    {
        Erreur = null;
        Confirmation = null;

        var nom = await _saisie.MotifAsync("Nom de la nouvelle marque");

        if (string.IsNullOrWhiteSpace(nom))
        {
            return;
        }

        EnCours = true;

        try
        {
            // Créée sans logo ni description : ils se posent ensuite dans le
            // panneau, où on les voit. Une boîte d'une ligne ne peut pas les
            // recueillir tous les trois.
            var resultat = await _api.CreerMarqueAsync(nom.Trim(), null, null);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = $"Marque « {nom.Trim()} » créée en attente de publication.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    private async Task EnregistrerAsync()
    {
        if (_marque is not { } marque || string.IsNullOrWhiteSpace(_nom))
        {
            return;
        }

        Erreur = null;
        Confirmation = null;
        EnCours = true;

        try
        {
            var resultat = await _api.ModifierMarqueAsync(
                marque.Id, _nom.Trim(),
                string.IsNullOrWhiteSpace(_logo) ? null : _logo.Trim(),
                string.IsNullOrWhiteSpace(_description) ? null : _description.Trim());

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = marque.Nom == _nom.Trim()
                ? "Marque enregistrée."
                : $"Marque enregistrée. Le slug reste « {marque.Slug} » : il ne suit pas le nom.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    private async Task BasculerAsync(bool publier)
    {
        if (_marque is not { } marque)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;
        EnCours = true;

        try
        {
            var resultat = await _api.PublierMarqueAsync(marque.Id, publier);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = publier
                ? $"« {marque.Nom} » est publiée."
                : $"« {marque.Nom} » repasse en attente — elle n'est pas archivée.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    /// <remarks>
    /// MOT DE PASSE ET MOTIF AVANT UNE SUPPRESSION QUI N'EST PAS RATTRAPABLE.
    ///
    /// Le serveur supprime la ligne sans vérifier qu'aucun produit ne la
    /// référence, et sans clé étrangère pour l'en empêcher. Le motif n'est
    /// envoyé nulle part — la route ne prend pas de corps : il n'a d'autre rôle
    /// que d'imposer une seconde d'arrêt sur un geste définitif.
    /// </remarks>
    private async Task SupprimerAsync()
    {
        if (_marque is not { } marque)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        var motif = await _saisie.MotifAsync($"Motif de la suppression de « {marque.Nom} »");

        if (string.IsNullOrWhiteSpace(motif))
        {
            return;
        }

        var mot = await _saisie.MotDePasseAsync($"Supprimer la marque {marque.Nom}");

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

        EnCours = true;

        try
        {
            var resultat = await _api.SupprimerMarqueAsync(marque.Id);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = $"Marque « {marque.Nom} » supprimée. Les fiches qui la citaient "
                           + "conservent son identifiant, qui ne désigne plus rien.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    /// <summary>Approuve la demande : création d'une marque, ou rattachement.</summary>
    private async Task TrancherAsync(Guid? marqueExistante)
    {
        if (_demande is not { } demande)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        var cible = marqueExistante is null ? null : _marque;

        // Retenue par le serveur : la marque créée, ou celle du rattachement.
        Guid? retenue = null;

        EnCours = true;

        try
        {
            var resultat = await _api.ApprouverDemandeMarqueAsync(demande.Id, marqueExistante);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = cible is null
                ? $"Demande « {demande.Nom} » approuvée : la marque est créée, en attente de publication."
                : $"Demande « {demande.Nom} » rattachée à « {cible.Nom} ». Aucune marque n'a été créée.";

            // La marque retenue est sélectionnée après rechargement, pour que le
            // geste suivant — publier, corriger le nom — porte sur elle sans
            // avoir à la retrouver dans la liste.
            retenue = resultat.Valeur == Guid.Empty ? null : resultat.Valeur;
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();

        if (retenue is { } marque)
        {
            // Une marque tout juste créée démarre en `Pending` : elle est bien
            // dans le référentiel complet, et la recherche en cours pourrait
            // néanmoins l'exclure. On efface donc le filtre plutôt que de
            // sélectionner une ligne qui n'est pas affichée.
            if (_referentiel.Any(m => m.Id == marque) && Marques.All(m => m.Id != marque))
            {
                Recherche = string.Empty;
            }

            SelectionMarque = Marques.FirstOrDefault(m => m.Id == marque);
        }
    }

    private async Task RefuserAsync()
    {
        if (_demande is not { } demande)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        var motif = await _saisie.MotifAsync($"Motif du refus de « {demande.Nom} »");

        if (string.IsNullOrWhiteSpace(motif))
        {
            return;
        }

        var mot = _api.ElevationValide
            ? null
            : await _saisie.MotDePasseAsync($"Refuser la demande {demande.Nom}");

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
            var resultat = await _api.RefuserDemandeMarqueAsync(demande.Id, motif.Trim());

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = $"Demande « {demande.Nom} » refusée. Le vendeur peut la resoumettre corrigée.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }
}

/// <summary>Une marque du référentiel, telle que la liste l'affiche.</summary>
public sealed class LigneMarque
{
    public LigneMarque(MarqueAdmin marque)
    {
        Id = marque.Id;
        Nom = marque.Name;
        Slug = marque.Slug;
        Statut = marque.Status;
        Logo = marque.LogoUrl ?? string.Empty;
        Description = marque.Description ?? string.Empty;

        Etat = marque.Status switch
        {
            "Active" => "publiée",
            "Pending" => "en attente",
            "Archived" => "archivée",
            _ => marque.Status.ToLowerInvariant(),
        };
    }

    public Guid Id { get; }

    public string Nom { get; }

    public string Slug { get; }

    public string Statut { get; }

    public string Etat { get; }

    public string Logo { get; }

    public string Description { get; }

    /// <summary>Une marque publiée se distingue d'un coup d'œil dans la liste.</summary>
    public bool EstPubliee => Statut == "Active";

    public bool Correspond(string terme)
        => Nom.Contains(terme, StringComparison.CurrentCultureIgnoreCase)
           || Slug.Contains(terme, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Une demande de marque en attente, telle que la file l'affiche.</summary>
public sealed class LigneDemandeMarque
{
    public LigneDemandeMarque(DemandeMarqueAdmin demande)
    {
        Id = demande.Id;
        Nom = demande.Name;
        Note = demande.Note ?? string.Empty;
        Vendeur = demande.SellerId.ToString();
        Depuis = demande.RequestedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        ANote = !string.IsNullOrWhiteSpace(demande.Note);
    }

    public Guid Id { get; }

    public string Nom { get; }

    public string Note { get; }

    /// <summary>
    /// L'identifiant du vendeur, faute de son nom.
    /// </summary>
    /// <remarks>
    /// `BrandRequestSummary` ne porte que `SellerId` : le nom de la boutique est
    /// dans merchant-service, et rien ne les joint côté serveur. Afficher le
    /// GUID est laid mais exact ; inventer un libellé le serait moins.
    /// </remarks>
    public string Vendeur { get; }

    public string Depuis { get; }

    public bool ANote { get; }
}
