using System.Collections.ObjectModel;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Les lots de reversement : ce qui est dû aux vendeurs, et ce qui est parti.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// ÉCRAN DE LECTURE, ET C'EST UNE DÉCISION D'ARCHITECTURE, PAS UN MANQUE.
///
/// La route `settlements` de la passerelle est `GET, HEAD, OPTIONS` seulement.
/// Ses métadonnées disent pourquoi : « le lancement d'un règlement vit sous
/// /api/financial/settlements dans un groupe MapAdminGroup voisin ; une route
/// sans restriction de méthode l'exposerait au proxy. Le service refuserait,
/// mais on ne compte pas là-dessus. »
///
/// Les quatre gestes — lancer, annuler, marquer payé, marquer échoué — restent
/// donc internes au réseau. L'écran les NOMME plutôt que d'afficher des boutons
/// qui rendraient 404 : un administrateur doit savoir où se fait le geste, pas
/// découvrir qu'il ne se fait pas ici.
///
/// L'UN D'EUX EST IRRÉVERSIBLE, ET CELA JUSTIFIE À SOI SEUL LA FRONTIÈRE.
///
/// Marquer un versement payé débite le vendeur de son solde. Le déclarer ensuite
/// échoué est refusé : du point de vue du système, l'argent est parti. Un clic
/// de trop dans une console de bureau n'a pas de retour arrière.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class ReglementViewModel : ViewModelBase
{
    private readonly ClientApiAdmin _api;

    private LigneLot? _lot;
    private LigneVersement? _versement;
    private ReleveVendeur? _releve;
    private string? _erreurReleve;
    private string? _erreur;
    private bool _enCours;

    public ReglementViewModel(ClientApiAdmin api)
    {
        _api = api;

        Rafraichir = new CommandeAsync(ChargerAsync);
        LireReleve = new CommandeAsync(LireReleveAsync, () => ADesVersement && !EnCours);

        Rafraichir.Execute(null);
    }

    public ObservableCollection<LigneLot> Lots { get; } = [];

    public ObservableCollection<LigneVersement> Versements { get; } = [];

    public LigneLot? Selection
    {
        get => _lot;
        set
        {
            if (!Definir(ref _lot, value))
            {
                return;
            }

            SelectionVersement = null;
            Versements.Clear();

            foreach (var versement in value?.Versements ?? [])
            {
                Versements.Add(versement);
            }

            Notifier(nameof(ADesSelection));
            Notifier(nameof(Repartition));
            Notifier(nameof(Ecart));
            Notifier(nameof(AEcart));
            Reevaluer();
        }
    }

    public LigneVersement? SelectionVersement
    {
        get => _versement;
        set
        {
            if (!Definir(ref _versement, value))
            {
                return;
            }

            Releve = null;
            ErreurReleve = null;

            Notifier(nameof(ADesVersement));
            Reevaluer();
        }
    }

    public bool ADesSelection => _lot is not null;

    public bool ADesVersement => _versement is not null;

    /// <summary>Le détail chiffré du lot sélectionné.</summary>
    /// <remarks>
    /// LE TOTAL DU LOT EST RECALCULÉ À CÔTÉ DE CELUI QUE LE SERVEUR ANNONCE.
    ///
    /// `TotalNet` est porté par le lot ; la somme des `NetAmount` de ses
    /// versements devrait le retrouver. Les deux viennent du MÊME agrégat, donc
    /// ils concordent — et c'est justement pour cela que les afficher ensemble a
    /// une valeur : le jour où ils divergent, c'est l'agrégat qui est abîmé, et
    /// rien d'autre ne le dirait.
    /// </remarks>
    public string Repartition
    {
        get
        {
            if (_lot is not { } lot)
            {
                return string.Empty;
            }

            var somme = lot.Versements.Sum(v => v.NetBrut);

            return $"{lot.Versements.Count} versement(s)  ·  "
                   + $"somme des nets {Argent.Formater(somme, lot.Devise)}  ·  "
                   + $"total annoncé {Argent.Formater(lot.TotalNetBrut, lot.Devise)}";
        }
    }

    public string? Ecart
    {
        get
        {
            if (_lot is not { } lot)
            {
                return null;
            }

            var somme = lot.Versements.Sum(v => v.NetBrut);

            return somme == lot.TotalNetBrut
                ? null
                : $"Le total du lot ({Argent.Formater(lot.TotalNetBrut, lot.Devise)}) ne retrouve pas "
                  + $"la somme de ses versements ({Argent.Formater(somme, lot.Devise)}). Les deux "
                  + "viennent du même agrégat : un écart signale une donnée abîmée, pas un décalage "
                  + "de période.";
        }
    }

    public bool AEcart => Ecart is not null;

    /// <summary>Le relevé du vendeur du versement sélectionné, sur la période du lot.</summary>
    public ReleveVendeur? Releve
    {
        get => _releve;
        private set
        {
            if (Definir(ref _releve, value))
            {
                Notifier(nameof(ADesReleve));
                Notifier(nameof(ReleveBrut));
                Notifier(nameof(ReleveCommissions));
                Notifier(nameof(ReleveFrais));
                Notifier(nameof(ReleveNet));
                Notifier(nameof(ReleveLignes));
            }
        }
    }

    public bool ADesReleve => _releve is not null;

    public string ReleveBrut => _releve is null
        ? string.Empty
        : Argent.Formater(_releve.GrossSales, _releve.Currency);

    public string ReleveCommissions => _releve is null
        ? string.Empty
        : Argent.Formater(_releve.Commissions, _releve.Currency);

    public string ReleveFrais => _releve is null
        ? string.Empty
        : Argent.Formater(_releve.ProviderFees, _releve.Currency);

    public string ReleveNet => _releve is null
        ? string.Empty
        : Argent.Formater(_releve.NetPayout, _releve.Currency);

    public string ReleveLignes => _releve is null
        ? string.Empty
        : $"{_releve.LineCount} gain(s) sur la période";

    public string? ErreurReleve
    {
        get => _erreurReleve;
        private set { if (Definir(ref _erreurReleve, value)) Notifier(nameof(AErreurReleve)); }
    }

    public bool AErreurReleve => !string.IsNullOrEmpty(_erreurReleve);

    public string? Erreur
    {
        get => _erreur;
        private set { if (Definir(ref _erreur, value)) Notifier(nameof(EnErreur)); }
    }

    public bool EnErreur => !string.IsNullOrEmpty(_erreur);

    public bool EnCours
    {
        get => _enCours;
        private set { if (Definir(ref _enCours, value)) Reevaluer(); }
    }

    public string Position => Lots.Count == 0
        ? "Aucun lot"
        : $"{Lots.Count} lot(s)  ·  {Lots.Count(l => l.EnAttente)} en cours";

    /// <summary>Ce que la console ne peut pas faire, et où cela se fait.</summary>
    public string Frontiere =>
        "Lancer un lot, l'annuler, marquer un versement payé ou échoué : ces quatre gestes "
        + "ne passent pas par la passerelle. Sa route settlements est GET seulement, "
        + "délibérément — le lancement d'un règlement vit dans un groupe admin voisin, et "
        + "une route sans restriction de méthode l'exposerait au proxy. Ils s'exécutent "
        + "depuis le réseau interne. Marquer un versement payé est sans retour : le vendeur "
        + "est débité, et le déclarer ensuite échoué est refusé.";

    public CommandeAsync Rafraichir { get; }

    public CommandeAsync LireReleve { get; }

    private void Reevaluer()
    {
        LireReleve.Reevaluer();
    }

    private async Task ChargerAsync()
    {
        EnCours = true;
        Erreur = null;

        try
        {
            var choisi = _lot?.Id;
            var resultat = await _api.ListerLotsReglementAsync();

            if (!resultat.Reussi || resultat.Valeur is null)
            {
                Erreur = resultat.Message ?? "Lots indisponibles.";
                return;
            }

            Selection = null;
            Lots.Clear();

            // Le plus récent en premier : c'est celui qu'on vient regarder. Le
            // service rend l'ordre du dépôt, sans tri garanti.
            foreach (var lot in resultat.Valeur.OrderByDescending(l => l.CreatedAtUtc))
            {
                Lots.Add(new LigneLot(lot));
            }

            Selection = Lots.FirstOrDefault(l => l.Id == choisi) ?? Lots.FirstOrDefault();

            Notifier(nameof(Position));
        }
        finally
        {
            EnCours = false;
        }
    }

    /// <remarks>
    /// LA PÉRIODE VIENT DU LOT, ET LES DEUX CHIFFRES NE SE COMPARENT PAS.
    ///
    /// Le relevé filtre les gains sur `CreatedAtUtc`, le lot sur `ReleasedAtUtc`
    /// et sur le statut `Released`. Un gain né avant la période mais libéré
    /// pendant est dans le lot et pas dans le relevé ; l'inverse existe aussi.
    /// L'écran affiche donc les deux en le disant, et ne calcule aucune
    /// différence — un tel chiffre enverrait chercher une erreur inexistante.
    /// </remarks>
    private async Task LireReleveAsync()
    {
        if (_versement is not { } versement || _lot is not { } lot)
        {
            return;
        }

        ErreurReleve = null;
        EnCours = true;

        try
        {
            var resultat = await _api.LireReleveVendeurAsync(
                versement.Vendeur, lot.DebutBrut, lot.FinBrute);

            if (!resultat.Reussi || resultat.Valeur is null)
            {
                ErreurReleve = resultat.Message ?? "Relevé indisponible.";
                return;
            }

            Releve = resultat.Valeur;
        }
        finally
        {
            EnCours = false;
        }
    }
}

/// <summary>Un lot de reversement, tel que la liste l'affiche.</summary>
public sealed class LigneLot
{
    public LigneLot(LotReglement lot)
    {
        Id = lot.Id;
        Devise = lot.Currency;
        TotalNetBrut = lot.TotalNet;
        DebutBrut = lot.PeriodStartUtc;
        FinBrute = lot.PeriodEndUtc;
        Statut = lot.Status;

        Periode = $"{lot.PeriodStartUtc:dd/MM/yyyy} → {lot.PeriodEndUtc:dd/MM/yyyy}";
        Cree = lot.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        Total = Argent.Formater(lot.TotalNet, lot.Currency);

        Versements = (lot.Payouts ?? [])
            .Select(p => new LigneVersement(p))
            .OrderByDescending(v => v.NetBrut)
            .ToList();

        // Un lot dont tous les versements sont payés est clos dans les faits,
        // quel que soit le libellé de son statut.
        Restants = Versements.Count(v => !v.Paye);
        EnAttente = Restants > 0;

        Etat = Restants == 0 && Versements.Count > 0
            ? "tous versés"
            : $"{Restants} en attente";
    }

    public Guid Id { get; }

    public string Devise { get; }

    public decimal TotalNetBrut { get; }

    public DateTime DebutBrut { get; }

    public DateTime FinBrute { get; }

    public string Statut { get; }

    public string Periode { get; }

    public string Cree { get; }

    public string Total { get; }

    public IReadOnlyList<LigneVersement> Versements { get; }

    public int Restants { get; }

    public bool EnAttente { get; }

    public string Etat { get; }
}

/// <summary>Un versement à un vendeur.</summary>
public sealed class LigneVersement
{
    public LigneVersement(VersementReglement versement)
    {
        Id = versement.Id;
        Vendeur = versement.SellerId;
        NetBrut = versement.NetAmount;
        Statut = versement.Status;

        Brut = Argent.Formater(versement.GrossAmount, versement.Currency);
        Commission = Argent.Formater(versement.CommissionAmount, versement.Currency);
        Net = Argent.Formater(versement.NetAmount, versement.Currency);
        Detail = $"brut {Brut}  ·  commission {Commission}";

        Paye = versement.PaidAtUtc is not null;

        // LA RÉFÉRENCE DE L'OPÉRATEUR EST LA SEULE PREUVE QUE L'ARGENT EST
        //    PARTI, ET SON ABSENCE SUR UN VERSEMENT PAYÉ EST UN SIGNAL.
        //
        // `MarkPayoutPaidAsync` prend un `ProviderReference` : un versement marqué
        // payé sans référence a été déclaré à la main, sans trace côté opérateur.
        Reference = string.IsNullOrWhiteSpace(versement.ProviderRef)
            ? (Paye ? "payé SANS référence opérateur" : "—")
            : versement.ProviderRef;

        SansPreuve = Paye && string.IsNullOrWhiteSpace(versement.ProviderRef);

        Quand = versement.PaidAtUtc is { } date
            ? date.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
            : "non versé";
    }

    public Guid Id { get; }

    public Guid Vendeur { get; }

    /// <summary>
    /// L'identifiant du vendeur, faute de son nom.
    /// </summary>
    /// <remarks>
    /// `PayoutSummary` ne porte que `SellerId`. Le nom de la boutique est dans
    /// merchant-service, et aucune route ne joint les deux. Le GUID est laid mais
    /// exact ; un libellé inventé le serait moins.
    /// </remarks>
    public string VendeurAffiche => Vendeur.ToString();

    public decimal NetBrut { get; }

    public string Statut { get; }

    public string Brut { get; }

    public string Commission { get; }

    public string Net { get; }

    /// <summary>Brut et commission sur une ligne, composés ici et non en XAML.</summary>
    public string Detail { get; }

    public bool Paye { get; }

    public bool SansPreuve { get; }

    public string Reference { get; }

    public string Quand { get; }
}
