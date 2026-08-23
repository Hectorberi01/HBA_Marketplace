using System.Collections.ObjectModel;
using System.Globalization;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Les factures de frais émises aux vendeurs.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// LE DÉTAIL D'UNE FACTURE N'EST EXPOSÉ PAR AUCUNE ROUTE.
///
/// `Invoice` possède ses `InvoiceLine`, le dépôt les charge, et
/// `InvoiceMapper.ToSummary` les laisse tomber — `GetInvoiceQuery` rend le même
/// résumé que la liste. On peut donc AJOUTER une ligne et ne jamais la relire :
/// seul le total bouge.
///
/// Cet écran le DIT au lieu d'afficher un détail vide, qui ferait croire à une
/// facture sans postes. Ce qu'il ne fait pas : corriger le contrat — les clients
/// vendeur consomment déjà `InvoiceSummary`, et l'élargir n'est pas un geste de
/// console.
///
/// LES TROIS ÉTATS SONT UNE SÉQUENCE, PAS DES ÉTIQUETTES.
///
/// `AddLine` n'accepte QUE `Draft` ; `Issue` refuse une facture vide ET une
/// facture déjà émise ; `MarkPaid` n'accepte QUE `Issued`. Les boutons suivent
/// donc l'état de la facture choisie, plutôt que d'envoyer un geste que le
/// domaine refusera en 409.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class FacturesViewModel : ViewModelBase
{
    private readonly ClientApiAdmin _api;
    private readonly IDemandeurDeSaisie _saisie;

    private LigneFacture? _selection;
    private string? _statut = "Issued";
    private string _vendeurFiltre = string.Empty;
    private int _page = 1;
    private long _total;
    private bool _suivante;

    private bool _creation;
    private string _vendeur = string.Empty;
    private string _debut = string.Empty;
    private string _fin = string.Empty;
    private string _devise = "XOF";

    private string _libelleLigne = string.Empty;
    private string _montantLigne = string.Empty;

    private string? _erreur;
    private string? _confirmation;
    private bool _enCours;

    private const int Taille = 25;

    /// <summary>Les trois statuts, dans l'ordre où une facture les traverse.</summary>
    private static readonly string[] Ordre = ["Draft", "Issued", "Paid"];

    public FacturesViewModel(ClientApiAdmin api, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _saisie = saisie;

        Rafraichir = new CommandeAsync(ChargerAsync);
        Precedente = new CommandeAsync(() => AllerAsync(_page - 1), () => _page > 1 && !EnCours);
        Suivante = new CommandeAsync(() => AllerAsync(_page + 1), () => _suivante && !EnCours);
        Filtrer = new CommandeAsync<string>(FiltrerAsync, _ => !EnCours);
        AppliquerVendeur = new CommandeAsync(AppliquerVendeurAsync, () => !EnCours);

        Nouvelle = new CommandeAsync(NouvelleAsync, () => !EnCours);
        Creer = new CommandeAsync(CreerAsync, () => _creation && !EnCours);
        Annuler = new CommandeAsync(AnnulerAsync, () => _creation && !EnCours);

        AjouterLigne = new CommandeAsync(AjouterLigneAsync, () => Modifiable && !EnCours);
        Emettre = new CommandeAsync(EmettreAsync, () => Emettable && !EnCours);
        MarquerPayee = new CommandeAsync(MarquerPayeeAsync, () => Payable && !EnCours);

        Rafraichir.Execute(null);
    }

    public ObservableCollection<LigneFacture> Factures { get; } = [];

    public ObservableCollection<LigneFacette> Facettes { get; } = [];

    public LigneFacture? Selection
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
                Notifier(nameof(EnCreation));
            }

            Notifier(nameof(ADesSelection));
            Notifier(nameof(Modifiable));
            Notifier(nameof(Emettable));
            Notifier(nameof(Payable));
            Notifier(nameof(EtatExplique));
            Reevaluer();
        }
    }

    public bool ADesSelection => _selection is not null;

    /// <summary>Une facture ne se compose qu'à l'état brouillon.</summary>
    public bool Modifiable => _selection is { Brouillon: true };

    public bool Emettable => _selection is { Brouillon: true };

    public bool Payable => _selection is { Emise: true };

    /// <summary>Ce que l'état de la facture choisie autorise, et ce qu'il ferme.</summary>
    public string EtatExplique => _selection switch
    {
        null => string.Empty,
        { Brouillon: true, TotalBrut: 0m } =>
            "Brouillon vide : l'émission sera refusée tant qu'aucune ligne n'y figure.",
        { Brouillon: true } =>
            "Brouillon : des lignes peuvent encore s'y ajouter. Une fois émise, elle est figée.",
        { Emise: true } =>
            "Émise : plus aucune ligne ne peut s'y ajouter. Il reste à la marquer payée.",
        _ => "Payée : la facture est soldée et ne bouge plus.",
    };

    public string? Statut => _statut;

    public string VendeurFiltre
    {
        get => _vendeurFiltre;
        set => Definir(ref _vendeurFiltre, value);
    }

    public string Position => _total == 0
        ? "Aucune facture"
        : $"{_total} facture(s)  ·  page {_page}";

    public bool EnCreation => _creation;

    public string Vendeur
    {
        get => _vendeur;
        set => Definir(ref _vendeur, value);
    }

    public string Debut
    {
        get => _debut;
        set => Definir(ref _debut, value);
    }

    public string Fin
    {
        get => _fin;
        set => Definir(ref _fin, value);
    }

    public string Devise
    {
        get => _devise;
        set => Definir(ref _devise, value);
    }

    public string LibelleLigne
    {
        get => _libelleLigne;
        set => Definir(ref _libelleLigne, value);
    }

    public string MontantLigne
    {
        get => _montantLigne;
        set => Definir(ref _montantLigne, value);
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

    public CommandeAsync Rafraichir { get; }

    public CommandeAsync Precedente { get; }

    public CommandeAsync Suivante { get; }

    public CommandeAsync<string> Filtrer { get; }

    public CommandeAsync AppliquerVendeur { get; }

    public CommandeAsync Nouvelle { get; }

    public CommandeAsync Creer { get; }

    public CommandeAsync Annuler { get; }

    public CommandeAsync AjouterLigne { get; }

    public CommandeAsync Emettre { get; }

    public CommandeAsync MarquerPayee { get; }

    private void Reevaluer()
    {
        Precedente.Reevaluer();
        Suivante.Reevaluer();
        Filtrer.Reevaluer();
        AppliquerVendeur.Reevaluer();
        Nouvelle.Reevaluer();
        Creer.Reevaluer();
        Annuler.Reevaluer();
        AjouterLigne.Reevaluer();
        Emettre.Reevaluer();
        MarquerPayee.Reevaluer();
    }

    private async Task ChargerAsync()
    {
        EnCours = true;
        Erreur = null;

        try
        {
            var choisie = _selection?.Id;

            Guid? vendeur = Guid.TryParse(_vendeurFiltre.Trim(), out var lu) ? lu : null;

            var page = await _api.ListerFacturesAsync(_page, Taille, _statut, vendeur);

            if (!page.Reussi || page.Valeur?.Data is null)
            {
                Erreur = page.Message ?? "Factures indisponibles.";
                return;
            }

            Selection = null;
            Factures.Clear();

            foreach (var facture in page.Valeur.Data)
            {
                Factures.Add(new LigneFacture(facture));
            }

            _total = page.Valeur.Meta?.Total ?? Factures.Count;
            _suivante = page.Valeur.Meta?.HasNext ?? false;

            RemplirFacettes(page.Valeur.Meta?.Facets);

            Selection = Factures.FirstOrDefault(f => f.Id == choisie);

            Notifier(nameof(Position));
        }
        finally
        {
            EnCours = false;
        }
    }

    /// <remarks>
    /// LES COMPTES SONT CALCULÉS AVANT LE FILTRE DE STATUT, ET APRÈS CELUI DE
    /// VENDEUR.
    ///
    /// Le dépôt groupe sur la requête déjà restreinte au vendeur, puis applique
    /// le statut : les trois onglets continuent donc d'afficher leurs comptes
    /// quand on filtre sur « Émises », mais ils portent sur le vendeur choisi,
    /// pas sur la plateforme. C'est ce que dit le libellé du filtre vendeur.
    /// </remarks>
    private void RemplirFacettes(IReadOnlyDictionary<string, int>? facettes)
    {
        Facettes.Clear();

        if (facettes is null)
        {
            return;
        }

        foreach (var cle in Ordre)
        {
            var present = facettes.TryGetValue(cle, out var nombre);

            if (present && nombre > 0 || cle == _statut)
            {
                Facettes.Add(new LigneFacette(cle, present ? nombre : 0, cle == _statut, Libelle(cle)));
            }
        }
    }

    private static string Libelle(string cle) => cle switch
    {
        "Draft" => "Brouillons",
        "Issued" => "Émises",
        "Paid" => "Payées",
        _ => cle,
    };

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

    private async Task AppliquerVendeurAsync()
    {
        Erreur = null;

        if (!string.IsNullOrWhiteSpace(_vendeurFiltre) && !Guid.TryParse(_vendeurFiltre.Trim(), out _))
        {
            Erreur = "Le filtre vendeur attend un identifiant complet ; il se copie depuis "
                     + "l'écran Vendeurs. Videz le champ pour revoir toute la plateforme.";
            return;
        }

        _page = 1;
        await ChargerAsync();
    }

    private Task NouvelleAsync()
    {
        Erreur = null;
        Confirmation = null;

        Selection = null;
        _creation = true;

        _vendeur = string.Empty;
        _devise = "XOF";

        // Le mois écoulé, qui est la période de facturation courante dans les
        // faits. Rien ne l'impose côté serveur : il refuse seulement une période
        // dont la fin ne dépasse pas le début.
        var aujourdhui = DateTime.UtcNow.Date;
        var premier = new DateTime(aujourdhui.Year, aujourdhui.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        _debut = premier.AddMonths(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _fin = premier.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        Notifier(nameof(EnCreation));
        Notifier(nameof(Vendeur));
        Notifier(nameof(Debut));
        Notifier(nameof(Fin));
        Notifier(nameof(Devise));
        Reevaluer();

        return Task.CompletedTask;
    }

    private Task AnnulerAsync()
    {
        _creation = false;
        Notifier(nameof(EnCreation));
        Reevaluer();

        return Task.CompletedTask;
    }

    private async Task CreerAsync()
    {
        Erreur = null;
        Confirmation = null;

        if (!Guid.TryParse(_vendeur.Trim(), out var vendeur))
        {
            Erreur = "L'identifiant du vendeur est obligatoire : il se copie depuis l'écran Vendeurs.";
            return;
        }

        if (!DateTime.TryParse(_debut.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var debut)
            || !DateTime.TryParse(_fin.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var fin))
        {
            Erreur = "Les deux dates suivent le format AAAA-MM-JJ, en UTC.";
            return;
        }

        // `Invoice.Create` refuse `periodEndUtc <= periodStartUtc`. Le vérifier
        // ici nomme le champ ; le 409 du serveur nomme la règle du domaine.
        if (fin <= debut)
        {
            Erreur = "La fin de période doit être postérieure à son début.";
            return;
        }

        var devise = _devise.Trim().ToUpperInvariant();

        if (devise.Length != 3)
        {
            Erreur = "La devise se note sur trois lettres — XOF.";
            return;
        }

        if (!await EleverAsync("Créer une facture"))
        {
            return;
        }

        EnCours = true;

        try
        {
            var resultat = await _api.CreerFactureAsync(vendeur, debut, fin, devise);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            _creation = false;
            Notifier(nameof(EnCreation));

            Confirmation = "Facture créée à l'état brouillon. Elle ne part chez le vendeur qu'à "
                           + "l'émission, et l'émission exige au moins une ligne.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    /// <remarks>
    /// L'AVERTISSEMENT PART AVANT L'ENVOI, PARCE QU'APRÈS IL SERAIT INUTILE.
    ///
    /// La ligne ajoutée ne sera jamais relue — aucune route ne rend le détail
    /// d'une facture. Composer à l'aveugle est acceptable quand on le sait ; le
    /// découvrir après coup ne l'est pas. Le total, lui, se met à jour et sert de
    /// seule vérification.
    /// </remarks>
    private async Task AjouterLigneAsync()
    {
        if (_selection is not { } facture)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        if (string.IsNullOrWhiteSpace(_libelleLigne))
        {
            Erreur = "Le libellé de la ligne est obligatoire : il est la seule trace lisible de "
                     + "ce poste, puisque le détail ne se relit pas.";
            return;
        }

        if (!decimal.TryParse(_montantLigne.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var montant)
            || montant == 0m)
        {
            Erreur = "Le montant de la ligne doit être un nombre non nul.";
            return;
        }

        if (!await EleverAsync($"Ajouter une ligne à la facture {facture.Numero}"))
        {
            return;
        }

        var avant = facture.TotalBrut;

        EnCours = true;

        try
        {
            var resultat = await _api.AjouterLigneFactureAsync(facture.Id, _libelleLigne.Trim(), montant);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            LibelleLigne = string.Empty;
            MontantLigne = string.Empty;

            Confirmation = $"Ligne ajoutée. Le total passe de {Argent.Formater(avant, facture.Devise)} "
                           + $"à {Argent.Formater(avant + montant, facture.Devise)} — c'est la seule "
                           + "confirmation disponible, le détail des lignes n'étant relu par aucune route.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    private async Task EmettreAsync()
    {
        if (_selection is not { } facture)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        if (facture.TotalBrut == 0m)
        {
            Erreur = "Une facture sans ligne ne s'émet pas : le domaine refuse une facture vide. "
                     + "Ajoutez au moins un poste.";
            return;
        }

        if (!await EleverAsync($"Émettre la facture {facture.Numero}"))
        {
            return;
        }

        EnCours = true;

        try
        {
            var resultat = await _api.AgirSurFactureAsync(facture.Id, "issue");

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = "Facture émise. Elle est désormais figée : aucune ligne ne peut plus s'y "
                           + "ajouter, et rien ne permet de l'annuler depuis cette console.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    /// <remarks>
    /// « MARQUER PAYÉE » N'ENCAISSE RIEN.
    ///
    /// `MarkPaid` change le statut, point : aucun prélèvement, aucun mouvement de
    /// portefeuille, aucun rapprochement avec un paiement. C'est une constatation
    /// faite à la main, et l'écran le dit — sans quoi on croit avoir encaissé.
    /// </remarks>
    private async Task MarquerPayeeAsync()
    {
        if (_selection is not { } facture)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        var reference = await _saisie.ReferenceAsync(
            $"Référence du paiement reçu pour la facture {facture.Numero}. "
            + "Ce geste ne fait que constater : il n'encaisse rien");

        if (string.IsNullOrWhiteSpace(reference))
        {
            return;
        }

        if (!await EleverAsync($"Marquer payée la facture {facture.Numero}"))
        {
            return;
        }

        EnCours = true;

        try
        {
            var resultat = await _api.AgirSurFactureAsync(facture.Id, "paid");

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            // La référence saisie ne part PAS : `MarkInvoicePaid` ne prend aucun
            // corps. Elle sert à faire réfléchir avant le clic, et à laisser une
            // trace dans le journal d'exploitation de l'administrateur. Le
            // prétendre enregistrée serait un mensonge d'écran.
            Confirmation = "Facture marquée payée. La référence saisie n'est PAS enregistrée : "
                           + "la route ne transporte aucun corps. Conservez-la de votre côté.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    private async Task<bool> EleverAsync(string geste)
    {
        if (_api.ElevationValide)
        {
            return true;
        }

        var mot = await _saisie.MotDePasseAsync(geste);

        if (string.IsNullOrWhiteSpace(mot))
        {
            return false;
        }

        var elevation = await _api.EleverAsync(mot);

        if (elevation.Reussi)
        {
            return true;
        }

        Erreur = elevation.Message;
        return false;
    }
}

/// <summary>Une facture, telle que la liste l'affiche.</summary>
public sealed class LigneFacture
{
    public LigneFacture(FactureAdmin facture)
    {
        Id = facture.Id;
        VendeurBrut = facture.SellerId;
        Devise = facture.Currency;
        TotalBrut = facture.TotalAmount;
        StatutBrut = facture.Status;

        Numero = facture.Id.ToString()[..8];
        Vendeur = facture.SellerId.ToString()[..8];
        VendeurComplet = facture.SellerId.ToString();

        Periode = $"{facture.PeriodStartUtc:dd/MM/yyyy} → {facture.PeriodEndUtc:dd/MM/yyyy}";

        Total = Argent.Formater(facture.TotalAmount, facture.Currency);

        Brouillon = facture.Status == "Draft";
        Emise = facture.Status == "Issued";
        Payee = facture.Status == "Paid";

        Etat = facture.Status switch
        {
            "Draft" => "brouillon",
            "Issued" => "émise",
            "Paid" => "payée",
            _ => facture.Status.ToLowerInvariant(),
        };

        // UN BROUILLON VIDE NE S'ÉMETTRA PAS, ET LA LISTE DOIT LE MONTRER.
        //
        // `Issue` refuse `_lines.Count == 0`. Un total à zéro sur un brouillon
        // est donc le signe qu'il manque un poste, pas une facture gratuite.
        Vide = Brouillon && facture.TotalAmount == 0m;
    }

    public Guid Id { get; }

    public Guid VendeurBrut { get; }

    public string Devise { get; }

    public decimal TotalBrut { get; }

    public string StatutBrut { get; }

    public string Numero { get; }

    public string Vendeur { get; }

    /// <summary>L'identifiant entier, celui qui se recopie dans un filtre.</summary>
    public string VendeurComplet { get; }

    public string Periode { get; }

    public string Total { get; }

    public bool Brouillon { get; }

    public bool Emise { get; }

    public bool Payee { get; }

    public bool Vide { get; }

    public string Etat { get; }
}
