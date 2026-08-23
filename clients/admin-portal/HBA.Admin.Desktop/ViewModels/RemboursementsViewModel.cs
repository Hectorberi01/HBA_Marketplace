using System.Collections.ObjectModel;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Les dossiers de retour : ce qui attend un arbitrage.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// PAS DE « FILE DES LITIGES » : SEIZE ÉTATS, ET C'EST L'ÉCRAN QUI PRIORISE.
///
/// Décider dans le serveur lesquels des seize `ReturnStatus` forment un litige
/// y figerait un jugement d'exploitation. Selon le jour, ce qui presse est
/// `ManualReview`, ou `RefundPending` qui traîne, ou `InspectionPending`. La
/// route rend donc tout, filtrable, avec le compte par statut — et cet écran met
/// `ManualReview` en tête, parce que c'est le seul état qui ATTEND explicitement
/// un humain.
///
/// LE FILTRE PART EN NOM, LE STATUT REVIENT EN NUMÉRO.
///
/// `ReturnRequestDto` porte les énumérations telles quelles et rien n'enregistre
/// de `JsonStringEnumConverter` : elles arrivent en entiers. La table de
/// correspondance ci-dessous est recopiée de `ReturnEnums.cs` — si l'énumération
/// change côté serveur, elle ment jusqu'à ce qu'on la reprenne.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class RemboursementsViewModel : ViewModelBase
{
    private readonly ClientApiAdmin _api;
    private readonly IDemandeurDeSaisie _saisie;

    private LigneDossier? _selection;
    private string? _statut = "ManualReview";
    private int _page = 1;
    private long _total;
    private bool _suivante;
    private string? _erreur;
    private string? _confirmation;
    private bool _enCours;

    private const int Taille = 25;

    public RemboursementsViewModel(ClientApiAdmin api, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _saisie = saisie;

        Rafraichir = new CommandeAsync(ChargerAsync);
        Precedente = new CommandeAsync(() => AllerAsync(_page - 1), () => _page > 1 && !EnCours);
        Suivante = new CommandeAsync(() => AllerAsync(_page + 1), () => _suivante && !EnCours);
        Filtrer = new CommandeAsync<string>(FiltrerAsync, _ => !EnCours);
        Rejeter = new CommandeAsync(RejeterAsync, () => ADesSelection && !EnCours);
        Clore = new CommandeAsync(CloreAsync, () => ADesSelection && !EnCours);

        Rafraichir.Execute(null);
    }

    public ObservableCollection<LigneDossier> Dossiers { get; } = [];

    public ObservableCollection<LigneFacette> Facettes { get; } = [];

    public LigneDossier? Selection
    {
        get => _selection;
        set
        {
            if (Definir(ref _selection, value))
            {
                Notifier(nameof(ADesSelection));
                Reevaluer();
            }
        }
    }

    public bool ADesSelection => _selection is not null;

    public string? Statut => _statut;

    public string Position => _total == 0
        ? "Aucun dossier"
        : $"{_total} dossier(s)  ·  page {_page}";

    public string LibelleFiltre => _statut is null
        ? "Tous les statuts"
        : $"Filtré sur « {EtatsRetour.Libelle(_statut)} »";

    /// <summary>Ce que « rejeter » fait vraiment, dit avant le clic.</summary>
    public string Avertissement =>
        "La route s'appelle « override » et la commande qu'elle envoie s'appelle "
        + "RejectReturnCommand : ce geste REJETTE le dossier, il ne le débloque pas. "
        + "Le motif saisi est obligatoire côté serveur.";

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

    public CommandeAsync Rejeter { get; }

    public CommandeAsync Clore { get; }

    private void Reevaluer()
    {
        Precedente.Reevaluer();
        Suivante.Reevaluer();
        Filtrer.Reevaluer();
        Rejeter.Reevaluer();
        Clore.Reevaluer();
    }

    private async Task ChargerAsync()
    {
        EnCours = true;
        Erreur = null;

        try
        {
            var choisi = _selection?.Id;
            var page = await _api.ListerRetoursAsync(_page, Taille, _statut);

            if (!page.Reussi || page.Valeur?.Data is null)
            {
                Erreur = page.Message ?? "Dossiers indisponibles.";
                return;
            }

            Selection = null;
            Dossiers.Clear();

            foreach (var dossier in page.Valeur.Data)
            {
                Dossiers.Add(new LigneDossier(dossier));
            }

            _total = page.Valeur.Meta?.Total ?? Dossiers.Count;
            _suivante = page.Valeur.Meta?.HasNext ?? false;

            RemplirFacettes(page.Valeur.Meta?.Facets);

            Selection = Dossiers.FirstOrDefault(d => d.Id == choisi);

            Notifier(nameof(Position));
            Notifier(nameof(LibelleFiltre));
        }
        finally
        {
            EnCours = false;
        }
    }

    /// <remarks>
    /// LES ONGLETS SONT CEUX QUI ONT DES DOSSIERS, PLUS CELUI QUI EST ACTIF.
    ///
    /// Afficher les seize états ferait une barre illisible dont quatorze entrées
    /// diraient « 0 ». Mais l'onglet actif doit rester visible même quand il se
    /// vide — sinon le filtre disparaît sous la main de celui qui vient de traiter
    /// le dernier dossier, et l'écran semble avoir oublié ce qu'il montrait.
    /// </remarks>
    private void RemplirFacettes(IReadOnlyDictionary<string, int>? facettes)
    {
        Facettes.Clear();

        if (facettes is null)
        {
            return;
        }

        foreach (var cle in EtatsRetour.Ordre)
        {
            var present = facettes.TryGetValue(cle, out var nombre);

            if (present && nombre > 0 || cle == _statut)
            {
                Facettes.Add(new LigneFacette(
                    cle, present ? nombre : 0, cle == _statut, EtatsRetour.Libelle(cle)));
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

    private async Task RejeterAsync()
    {
        if (_selection is not { } dossier)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        var motif = await _saisie.MotifAsync(
            $"Motif du REJET du dossier {dossier.Numero}. Ce texte part vers le client");

        if (string.IsNullOrWhiteSpace(motif))
        {
            return;
        }

        var mot = _api.ElevationValide
            ? null
            : await _saisie.MotDePasseAsync($"Rejeter le dossier {dossier.Numero}");

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
            var resultat = await _api.RejeterRetourAsync(dossier.Id, motif.Trim());

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = $"Dossier {dossier.Numero} rejeté.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    private async Task CloreAsync()
    {
        if (_selection is not { } dossier)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        var mot = _api.ElevationValide
            ? null
            : await _saisie.MotDePasseAsync($"Clore le dossier {dossier.Numero}");

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
            var resultat = await _api.CloreRetourAsync(dossier.Id);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = $"Dossier {dossier.Numero} clos.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }
}

/// <summary>
/// Les seize états d'un dossier, recopiés de `ReturnEnums.cs`.
/// </summary>
/// <remarks>
/// RECOPIE ASSUMÉE, ET DATÉE PAR CE COMMENTAIRE.
///
/// Les énumérations arrivent en entiers : il n'y a pas d'autre façon de les
/// nommer côté client. Si `ReturnStatus` change côté serveur, cette table ment
/// jusqu'à ce qu'on la reprenne — et un état inconnu s'affichera « état N »
/// plutôt que de se taire.
/// </remarks>
public static class EtatsRetour
{
    private static readonly string[] Noms =
    [
        "Requested", "EligibilityCheck", "AwaitingApproval", "Approved", "AwaitingReturn",
        "InReturnTransit", "Received", "InspectionPending", "RefundPending", "Refunded",
        "Closed", "Rejected", "RejectedAfterInspection", "Cancelled", "Expired", "ManualReview",
    ];

    private static readonly Dictionary<string, string> Libelles = new()
    {
        ["Requested"] = "demandé",
        ["EligibilityCheck"] = "éligibilité",
        ["AwaitingApproval"] = "attente vendeur",
        ["Approved"] = "approuvé",
        ["AwaitingReturn"] = "attente renvoi",
        ["InReturnTransit"] = "en transit",
        ["Received"] = "reçu",
        ["InspectionPending"] = "à inspecter",
        ["RefundPending"] = "à rembourser",
        ["Refunded"] = "remboursé",
        ["Closed"] = "clos",
        ["Rejected"] = "rejeté",
        ["RejectedAfterInspection"] = "rejeté après inspection",
        ["Cancelled"] = "annulé",
        ["Expired"] = "expiré",
        ["ManualReview"] = "ARBITRAGE",
    };

    /// <summary>
    /// L'ordre d'affichage : ce qui attend un humain d'abord, le classé ensuite.
    /// </summary>
    public static readonly string[] Ordre =
    [
        "ManualReview", "RefundPending", "InspectionPending", "AwaitingApproval",
        "AwaitingReturn", "InReturnTransit", "Received", "Approved", "EligibilityCheck",
        "Requested", "Refunded", "Rejected", "RejectedAfterInspection", "Cancelled",
        "Expired", "Closed",
    ];

    public static string Nom(int valeur)
        => valeur >= 0 && valeur < Noms.Length ? Noms[valeur] : $"état {valeur}";

    public static string Libelle(string nom)
        => Libelles.TryGetValue(nom, out var libelle) ? libelle : nom;

    public static string LibelleDe(int valeur) => Libelle(Nom(valeur));
}

/// <summary>Un dossier de retour, tel que la liste l'affiche.</summary>
public sealed class LigneDossier
{
    public LigneDossier(DossierRetour dossier)
    {
        Id = dossier.Id;
        Numero = dossier.ReturnNumber;
        Statut = EtatsRetour.Nom(dossier.Status);
        Etat = EtatsRetour.LibelleDe(dossier.Status);
        Arbitrage = Statut == "ManualReview";

        Client = dossier.CustomerId.ToString();
        Vendeur = dossier.SellerId.ToString();
        Commande = dossier.OrderId.ToString();

        Estime = dossier.EstimatedRefund is { } estime
            ? Argent.Formater(estime.Amount, estime.Currency)
            : "—";

        Accorde = dossier.ApprovedRefund is { } accorde
            ? Argent.Formater(accorde.Amount, accorde.Currency)
            : "non décidé";

        Ouvert = dossier.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy");

        // L'EXPIRATION EST UNE ÉCHÉANCE, PAS UNE DÉCORATION.
        //
        // `ListExpirableAsync` fait basculer en `Expired` les dossiers dépassés
        // qui attendent encore — mais seulement depuis `AwaitingApproval` et
        // `AwaitingReturn`. Ailleurs, la date passe sans que rien n'arrive.
        Echeance = dossier.ExpiresAtUtc.ToLocalTime().ToString("dd/MM/yyyy");
        Depasse = dossier.ResolvedAtUtc is null && dossier.ExpiresAtUtc < DateTime.UtcNow;

        var lignes = dossier.Items ?? [];

        Contenu = lignes.Count == 0
            ? "aucune ligne"
            : string.Join(", ", lignes.Select(l => $"{l.RequestedQuantity}× {l.NameSnapshot}"));

        Transport = string.IsNullOrWhiteSpace(dossier.ReturnShippingPayer)
            ? "renvoi : non précisé"
            : $"renvoi payé par {dossier.ReturnShippingPayer.ToLowerInvariant()}";
    }

    public Guid Id { get; }

    public string Numero { get; }

    public string Statut { get; }

    public string Etat { get; }

    /// <summary>Le seul état qui attend explicitement un humain.</summary>
    public bool Arbitrage { get; }

    public string Client { get; }

    public string Vendeur { get; }

    public string Commande { get; }

    public string Estime { get; }

    public string Accorde { get; }

    public string Ouvert { get; }

    public string Echeance { get; }

    public bool Depasse { get; }

    public string Contenu { get; }

    public string Transport { get; }
}
