using System.Collections.ObjectModel;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Les portefeuilles : celui de la plateforme, et celui d'un compte donné.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// LES QUATRE SOLDES DE LA PLATEFORME NE S'ADDITIONNENT PAS.
///
/// Commissions est un revenu ; frais opérateur, ce qui a été payé au
/// prestataire ; livraison, ce qui a été encaissé pour l'acheminement ;
/// remboursements, ce qui a été rendu. Les additionner mélangerait des entrées
/// et des sorties. L'écran les montre côte à côte et ne calcule pas de total —
/// un chiffre unique ici serait faux et personne ne pourrait le dire.
///
/// LA CONSULTATION D'UN COMPTE SE FAIT PAR IDENTIFIANT, FAUTE DE MIEUX.
///
/// Il n'existe aucune route « liste des portefeuilles » : ni pour les vendeurs,
/// ni pour les livreurs. Les deux routes disponibles sont adressées par GUID.
/// L'écran demande donc un identifiant, qui se copie depuis la page Vendeurs,
/// depuis Livreurs, ou depuis une ligne de reversement.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class PortefeuilleViewModel : ViewModelBase
{
    private readonly ClientApiAdmin _api;

    private PortefeuillePlateforme? _plateforme;
    private PortefeuilleVendeur? _vendeur;
    private PortefeuilleLivreur? _livreur;
    private string _identifiant = string.Empty;
    private bool _estVendeur = true;
    private int _combien = 50;
    private string? _erreurCompte;
    private string? _erreur;
    private bool _enCours;

    public PortefeuilleViewModel(ClientApiAdmin api)
    {
        _api = api;

        Rafraichir = new CommandeAsync(ChargerAsync);
        Consulter = new CommandeAsync(ConsulterAsync, () => !EnCours);
        ChoisirVendeur = new CommandeAsync(() => BasculerAsync(true), () => !EnCours);
        ChoisirLivreur = new CommandeAsync(() => BasculerAsync(false), () => !EnCours);

        Rafraichir.Execute(null);
    }

    public ObservableCollection<LigneEcriture> Ecritures { get; } = [];

    public ObservableCollection<LigneEcriture> EcrituresCompte { get; } = [];

    public ObservableCollection<int> Volumes { get; } = [50, 100, 200];

    /// <summary>Nombre d'écritures demandées — paramètre obligatoire du serveur.</summary>
    public int Combien
    {
        get => _combien;
        set
        {
            if (Definir(ref _combien, value))
            {
                Rafraichir.Execute(null);
            }
        }
    }

    public string Commissions => _plateforme is null
        ? "—"
        : Argent.Formater(_plateforme.CommissionBalance, _plateforme.Currency);

    public string FraisOperateur => _plateforme is null
        ? "—"
        : Argent.Formater(_plateforme.ProviderFeeBalance, _plateforme.Currency);

    public string Livraison => _plateforme is null
        ? "—"
        : Argent.Formater(_plateforme.ShippingBalance, _plateforme.Currency);

    public string Remboursements => _plateforme is null
        ? "—"
        : Argent.Formater(_plateforme.RefundsBalance, _plateforme.Currency);

    /// <summary>Identifiant du compte à consulter.</summary>
    public string Identifiant
    {
        get => _identifiant;
        set => Definir(ref _identifiant, value);
    }

    public bool EstVendeur
    {
        get => _estVendeur;
        private set
        {
            if (Definir(ref _estVendeur, value))
            {
                Notifier(nameof(EstLivreur));
                Notifier(nameof(LibelleCompte));
            }
        }
    }

    public bool EstLivreur => !_estVendeur;

    public string LibelleCompte => _estVendeur ? "Identifiant du vendeur" : "Identifiant du livreur";

    public PortefeuilleVendeur? Vendeur
    {
        get => _vendeur;
        private set
        {
            if (Definir(ref _vendeur, value))
            {
                Notifier(nameof(ADesVendeur));
                Notifier(nameof(VendeurDisponible));
                Notifier(nameof(VendeurAVenir));
                Notifier(nameof(VendeurRetenu));
            }
        }
    }

    public bool ADesVendeur => _vendeur is not null;

    public string VendeurDisponible => _vendeur is null
        ? string.Empty
        : Argent.Formater(_vendeur.AvailableBalance, _vendeur.Currency);

    public string VendeurAVenir => _vendeur is null
        ? string.Empty
        : Argent.Formater(_vendeur.PendingBalance, _vendeur.Currency);

    public string VendeurRetenu => _vendeur is null
        ? string.Empty
        : Argent.Formater(_vendeur.PendingWithdrawal, _vendeur.Currency);

    public PortefeuilleLivreur? Livreur
    {
        get => _livreur;
        private set
        {
            if (Definir(ref _livreur, value))
            {
                Notifier(nameof(ADesLivreur));
                Notifier(nameof(LivreurDisponible));
                Notifier(nameof(LivreurCumul));
            }
        }
    }

    public bool ADesLivreur => _livreur is not null;

    public string LivreurDisponible => _livreur is null
        ? string.Empty
        : Argent.Formater(_livreur.AvailableBalance, _livreur.Currency);

    public string LivreurCumul => _livreur is null
        ? string.Empty
        : Argent.Formater(_livreur.LifetimeEarned, _livreur.Currency);

    public string? ErreurCompte
    {
        get => _erreurCompte;
        private set { if (Definir(ref _erreurCompte, value)) Notifier(nameof(AErreurCompte)); }
    }

    public bool AErreurCompte => !string.IsNullOrEmpty(_erreurCompte);

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

    public string Position => Ecritures.Count == 0
        ? "Aucune écriture"
        : $"{Ecritures.Count} dernière(s) écriture(s)";

    public CommandeAsync Rafraichir { get; }

    public CommandeAsync Consulter { get; }

    public CommandeAsync ChoisirVendeur { get; }

    public CommandeAsync ChoisirLivreur { get; }

    private void Reevaluer()
    {
        Consulter.Reevaluer();
        ChoisirVendeur.Reevaluer();
        ChoisirLivreur.Reevaluer();
    }

    private async Task ChargerAsync()
    {
        EnCours = true;
        Erreur = null;

        try
        {
            var soldes = await _api.LirePortefeuillePlateformeAsync();
            var ecritures = await _api.ListerEcrituresPlateformeAsync(_combien);

            var soucis = new List<string>();

            if (soldes.Reussi && soldes.Valeur is not null)
            {
                _plateforme = soldes.Valeur;
                Notifier(nameof(Commissions));
                Notifier(nameof(FraisOperateur));
                Notifier(nameof(Livraison));
                Notifier(nameof(Remboursements));
            }
            else
            {
                soucis.Add(soldes.Message ?? "Soldes indisponibles.");
            }

            Ecritures.Clear();

            if (ecritures.Reussi && ecritures.Valeur is not null)
            {
                foreach (var ecriture in ecritures.Valeur)
                {
                    Ecritures.Add(new LigneEcriture(ecriture));
                }
            }
            else
            {
                soucis.Add(ecritures.Message ?? "Grand livre indisponible.");
            }

            Erreur = soucis.Count == 0 ? null : string.Join(" ", soucis);

            Notifier(nameof(Position));
        }
        finally
        {
            EnCours = false;
        }
    }

    private Task BasculerAsync(bool vendeur)
    {
        if (EstVendeur != vendeur)
        {
            // Changer de nature vide le résultat précédent : laisser à l'écran le
            // portefeuille d'un vendeur sous un libellé « livreur » serait pire
            // qu'un panneau vide.
            Vendeur = null;
            Livreur = null;
            EcrituresCompte.Clear();
            ErreurCompte = null;
            EstVendeur = vendeur;
        }

        return Task.CompletedTask;
    }

    private async Task ConsulterAsync()
    {
        ErreurCompte = null;

        if (!Guid.TryParse(_identifiant.Trim(), out var identifiant))
        {
            ErreurCompte = "Identifiant illisible. Il n'existe aucune route de liste : "
                           + "l'identifiant se copie depuis Vendeurs, Livreurs ou une ligne de reversement.";
            return;
        }

        Vendeur = null;
        Livreur = null;
        EcrituresCompte.Clear();
        EnCours = true;

        try
        {
            if (_estVendeur)
            {
                var portefeuille = await _api.LirePortefeuilleVendeurAsync(identifiant);

                if (!portefeuille.Reussi || portefeuille.Valeur is null)
                {
                    ErreurCompte = portefeuille.Message ?? "Portefeuille indisponible.";
                    return;
                }

                Vendeur = portefeuille.Valeur;
            }
            else
            {
                var portefeuille = await _api.LirePortefeuilleLivreurAsync(identifiant);

                if (!portefeuille.Reussi || portefeuille.Valeur is null)
                {
                    ErreurCompte = portefeuille.Message ?? "Portefeuille indisponible.";
                    return;
                }

                Livreur = portefeuille.Valeur;
            }

            // Les écritures sont un complément : leur absence n'annule pas les
            // soldes, qui sont l'information principale de ce panneau.
            var ecritures = await _api.ListerEcrituresDeCompteAsync(_estVendeur, identifiant, _combien);

            if (ecritures.Reussi && ecritures.Valeur is not null)
            {
                foreach (var ecriture in ecritures.Valeur)
                {
                    EcrituresCompte.Add(new LigneEcriture(ecriture));
                }
            }
            else
            {
                ErreurCompte = ecritures.Message ?? "Écritures indisponibles.";
            }
        }
        finally
        {
            EnCours = false;
        }
    }
}

/// <summary>Une écriture du grand livre, telle que la liste l'affiche.</summary>
public sealed class LigneEcriture
{
    public LigneEcriture(EcritureWallet ecriture)
    {
        Id = ecriture.Id;
        Compte = ecriture.Account;
        Motif = ecriture.Reason;
        Quand = ecriture.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

        // LE SENS EST PORTÉ PAR UN CHAMP, PAS PAR LE SIGNE DU MONTANT.
        //
        // `Amount` est toujours positif ; c'est `Direction` qui dit s'il entre ou
        // s'il sort. Afficher le montant sans le sens rendrait un relevé où tout
        // s'additionne.
        Credit = string.Equals(ecriture.Direction, "Credit", StringComparison.OrdinalIgnoreCase);

        Montant = (Credit ? "+ " : "− ") + Argent.Formater(ecriture.Amount, ecriture.Currency);

        Rattachement = ecriture.ReferenceType is { Length: > 0 } type
            ? $"{type} {ecriture.ReferenceId}"
            : "mouvement interne";
    }

    public Guid Id { get; }

    public string Compte { get; }

    public string Motif { get; }

    public string Montant { get; }

    public bool Credit { get; }

    public string Rattachement { get; }

    public string Quand { get; }
}
