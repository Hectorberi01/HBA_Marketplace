using System.Collections.ObjectModel;
using System.Globalization;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>La grille de commission : ce que la plateforme prélève.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// L'ORDRE D'AFFICHAGE EST L'ORDRE D'APPLICATION.
///
/// `CommissionResolver` retient la règle applicable la plus SPÉCIFIQUE —
/// `Priority => (int)Scope`, donc Seller (2) devant Category (1) devant
/// Global (0) — et départage les ex æquo par `EffectiveFromUtc` décroissante.
/// La liste est triée ainsi, et pas par date : une grille lue dans un autre
/// ordre que celui du moteur se comprend de travers.
///
/// ET SI RIEN NE CORRESPOND, LE MOTEUR NE PRÉLÈVE PAS ZÉRO.
///
/// Il applique un taux par défaut. Un gestionnaire antérieur recopiait le
/// résolveur et rendait `0` dans ce cas : « l'écran d'administration annonçait
/// commission : 0 pendant que la comptabilisation prélevait 10 % ». C'est
/// pourquoi l'aperçu de cet écran passe par la route `/compute`, qui délègue au
/// vrai moteur, et pourquoi il DIT quand aucune règle ne s'est appliquée.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class CommissionsViewModel : ViewModelBase
{
    private readonly ClientApiAdmin _api;
    private readonly IDemandeurDeSaisie _saisie;

    private LigneCommission? _selection;
    private bool _creation;
    private string _portee = "Global";
    private string _cible = string.Empty;
    private string _taux = "0.10";
    private string _fixe = "0";
    private string _devise = "XOF";
    private string _minimum = string.Empty;
    private string _maximum = string.Empty;
    private string _effet = string.Empty;
    private bool _dateModifiee;

    private string _vendeurEssai = string.Empty;
    private string _categorieEssai = string.Empty;
    private string _montantEssai = "10000";
    private ApercuCommission? _apercu;
    private string? _erreurApercu;

    private string? _erreur;
    private string? _confirmation;
    private bool _enCours;

    public CommissionsViewModel(ClientApiAdmin api, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _saisie = saisie;

        Rafraichir = new CommandeAsync(ChargerAsync);
        Nouvelle = new CommandeAsync(NouvelleAsync, () => !EnCours);
        Enregistrer = new CommandeAsync(EnregistrerAsync, () => EnEdition && !EnCours);
        Activer = new CommandeAsync(() => AgirAsync("reactivate"), () => Activable && !EnCours);
        Desactiver = new CommandeAsync(() => AgirAsync("deactivate"), () => Desactivable && !EnCours);
        Supprimer = new CommandeAsync(() => AgirAsync("supprimer"), () => ADesSelection && !EnCours);
        Calculer = new CommandeAsync(CalculerAsync, () => !EnCours);

        Rafraichir.Execute(null);
    }

    public ObservableCollection<LigneCommission> Regles { get; } = [];

    /// <summary>Les trois portées, telles que le validateur les accepte.</summary>
    /// <remarks>
    /// `RuleFor(c => c.Scope).Must(v => v is "Global" or "Category" or "Seller")` :
    /// la casse compte au validateur, même si le handler reparse ensuite en
    /// ignorant la casse. On envoie donc la forme exacte.
    /// </remarks>
    public IReadOnlyList<string> Portees { get; } = ["Global", "Category", "Seller"];

    public LigneCommission? Selection
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
            Notifier(nameof(Activable));
            Notifier(nameof(Desactivable));
            Notifier(nameof(PorteeModifiable));
            Notifier(nameof(Titre));
            Reevaluer();
        }
    }

    public bool ADesSelection => _selection is not null;

    public bool EnEdition => _selection is not null || _creation;

    public bool Activable => _selection is { Active: false };

    public bool Desactivable => _selection is { Active: true };

    /// <summary>Le périmètre ne se modifie qu'à la création.</summary>
    /// <remarks>
    /// `UpdateCommissionRuleCommand` ne reprend PAS `Scope` ni `TargetId` — le
    /// commentaire du domaine le dit : « le périmètre n'est pas modifiable ».
    /// Laisser les champs actifs ferait croire à une modification qui ne partira
    /// jamais.
    /// </remarks>
    public bool PorteeModifiable => _creation;

    public string Titre => _creation
        ? "Nouvelle règle"
        : _selection is null ? "Règle" : _selection.Resume;

    public string Portee
    {
        get => _portee;
        set { if (Definir(ref _portee, value)) Notifier(nameof(CibleRequise)); }
    }

    /// <summary>Une règle `Global` ne vise personne.</summary>
    public bool CibleRequise => _portee is "Category" or "Seller";

    public string Cible
    {
        get => _cible;
        set => Definir(ref _cible, value);
    }

    public string Taux
    {
        get => _taux;
        set => Definir(ref _taux, value);
    }

    public string Fixe
    {
        get => _fixe;
        set => Definir(ref _fixe, value);
    }

    public string Devise
    {
        get => _devise;
        set => Definir(ref _devise, value);
    }

    public string Minimum
    {
        get => _minimum;
        set => Definir(ref _minimum, value);
    }

    public string Maximum
    {
        get => _maximum;
        set => Definir(ref _maximum, value);
    }

    /// <summary>Date de prise d'effet, en UTC.</summary>
    /// <remarks>
    /// ═══════════════════════════════════════════════════════════════════════
    /// TOUCHER À CE CHAMP EST UN GESTE, ET L'ÉCRAN LE SUIT.
    ///
    /// `UpdateCommissionRuleCommand.EffectiveFromUtc` est nullable, et `null`
    /// signifie « ne touche pas à la date ». C'est la correction d'un bogue que
    /// le dépôt raconte : le champ était non nullable et le BFF comblait
    /// l'absence par `?? DateTime.UtcNow`, si bien que « la moindre correction de
    /// taux sur une règle PROGRAMMÉE la rendait applicable SUR-LE-CHAMP, et la
    /// faisait passer devant ses sœurs de même portée ».
    ///
    /// Cette console n'envoie donc la date que si l'administrateur l'a modifiée —
    /// d'où le drapeau, et non une comparaison de chaînes qui prendrait un
    /// reformatage pour un changement.
    /// ═══════════════════════════════════════════════════════════════════════
    /// </remarks>
    public string Effet
    {
        get => _effet;
        set
        {
            if (Definir(ref _effet, value))
            {
                _dateModifiee = true;
                Notifier(nameof(DateSeraEnvoyee));
            }
        }
    }

    public bool DateSeraEnvoyee => _creation || _dateModifiee;

    public string VendeurEssai
    {
        get => _vendeurEssai;
        set => Definir(ref _vendeurEssai, value);
    }

    public string CategorieEssai
    {
        get => _categorieEssai;
        set => Definir(ref _categorieEssai, value);
    }

    public string MontantEssai
    {
        get => _montantEssai;
        set => Definir(ref _montantEssai, value);
    }

    public string Apercu => _apercu is null
        ? string.Empty
        : $"Commission {Argent.Formater(_apercu.CommissionAmount, _apercu.Currency)}  ·  "
          + $"net vendeur {Argent.Formater(_apercu.NetAmount, _apercu.Currency)}";

    /// <summary>Quelle règle a servi — ou aucune, ce qui n'est pas rien.</summary>
    public string ApercuRegle
    {
        get
        {
            if (_apercu is null)
            {
                return string.Empty;
            }

            if (_apercu.AppliedRuleId is not { } regle)
            {
                return "AUCUNE règle ne s'applique : le moteur a utilisé le taux par défaut de "
                       + "la plateforme. Ce n'est pas zéro, et ce n'est pas ce qui est configuré ici.";
            }

            var nommee = Regles.FirstOrDefault(r => r.Id == regle);

            return nommee is null
                ? $"Règle appliquée : {regle}"
                : $"Règle appliquée : {nommee.Resume}";
        }
    }

    public bool AApercu => _apercu is not null;

    public string? ErreurApercu
    {
        get => _erreurApercu;
        private set { if (Definir(ref _erreurApercu, value)) Notifier(nameof(AErreurApercu)); }
    }

    public bool AErreurApercu => !string.IsNullOrEmpty(_erreurApercu);

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

    public string Position => Regles.Count == 0
        ? "Aucune règle — le taux par défaut de la plateforme s'applique partout"
        : $"{Regles.Count} règle(s), dont {Regles.Count(r => r.Active)} active(s)";

    public CommandeAsync Rafraichir { get; }

    public CommandeAsync Nouvelle { get; }

    public CommandeAsync Enregistrer { get; }

    public CommandeAsync Activer { get; }

    public CommandeAsync Desactiver { get; }

    public CommandeAsync Supprimer { get; }

    public CommandeAsync Calculer { get; }

    private void Reevaluer()
    {
        Nouvelle.Reevaluer();
        Enregistrer.Reevaluer();
        Activer.Reevaluer();
        Desactiver.Reevaluer();
        Supprimer.Reevaluer();
        Calculer.Reevaluer();
    }

    private async Task ChargerAsync()
    {
        EnCours = true;
        Erreur = null;

        try
        {
            var choisie = _selection?.Id;
            var resultat = await _api.ListerCommissionsAsync();

            if (!resultat.Reussi || resultat.Valeur is null)
            {
                Erreur = resultat.Message ?? "Grille indisponible.";
                return;
            }

            Selection = null;
            Regles.Clear();

            // Tri = ordre de résolution : Seller, puis Category, puis Global ;
            // à portée égale, la prise d'effet la plus récente d'abord.
            foreach (var regle in resultat.Valeur
                         .OrderByDescending(r => Specificite(r.Scope))
                         .ThenByDescending(r => r.EffectiveFromUtc))
            {
                Regles.Add(new LigneCommission(regle));
            }

            Selection = Regles.FirstOrDefault(r => r.Id == choisie);

            Notifier(nameof(Position));
            Notifier(nameof(ApercuRegle));
        }
        finally
        {
            EnCours = false;
        }
    }

    private static int Specificite(string portee) => portee switch
    {
        "Seller" => 2,
        "Category" => 1,
        _ => 0,
    };

    private void Remplir(LigneCommission ligne)
    {
        _portee = ligne.Portee;
        _cible = ligne.CibleBrute?.ToString() ?? string.Empty;
        _taux = ligne.TauxBrut.ToString(CultureInfo.InvariantCulture);
        _fixe = ligne.FixeBrut.ToString(CultureInfo.InvariantCulture);
        _devise = ligne.Devise;
        _minimum = ligne.MinimumBrut?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _maximum = ligne.MaximumBrut?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _effet = ligne.EffetBrut.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        // REMIS À FAUX APRÈS AVOIR POSÉ LES CHAMPS, ET C'EST TOUT L'INTÉRÊT.
        //
        // Écrire `Effet` passe par son `set`, qui lève le drapeau. Sans cette
        // remise à zéro, sélectionner une règle suffirait à faire partir sa date
        // au prochain enregistrement — exactement le bogue que la nullabilité de
        // `EffectiveFromUtc` a corrigé côté serveur.
        _dateModifiee = false;

        NotifierLesChamps();
    }

    private void NotifierLesChamps()
    {
        Notifier(nameof(Portee));
        Notifier(nameof(CibleRequise));
        Notifier(nameof(Cible));
        Notifier(nameof(Taux));
        Notifier(nameof(Fixe));
        Notifier(nameof(Devise));
        Notifier(nameof(Minimum));
        Notifier(nameof(Maximum));
        Notifier(nameof(Effet));
        Notifier(nameof(DateSeraEnvoyee));
    }

    private Task NouvelleAsync()
    {
        Erreur = null;
        Confirmation = null;

        Selection = null;
        _creation = true;

        _portee = "Global";
        _cible = string.Empty;
        _taux = "0.10";
        _fixe = "0";
        _devise = "XOF";
        _minimum = string.Empty;
        _maximum = string.Empty;
        _effet = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        _dateModifiee = false;

        NotifierLesChamps();
        Notifier(nameof(EnEdition));
        Notifier(nameof(PorteeModifiable));
        Notifier(nameof(Titre));
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

        var mot = _api.ElevationValide
            ? null
            : await _saisie.MotDePasseAsync(_creation
                ? "Créer une règle de commission"
                : "Modifier une règle de commission");

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
            var resultat = _creation
                ? await _api.CreerCommissionAsync(
                    valeurs.Portee, valeurs.Cible, valeurs.Taux, valeurs.Fixe, valeurs.Devise,
                    valeurs.Minimum, valeurs.Maximum, valeurs.Effet ?? DateTime.UtcNow)
                : await _api.ModifierCommissionAsync(
                    _selection!.Id, valeurs.Taux, valeurs.Fixe, valeurs.Devise,
                    valeurs.Minimum, valeurs.Maximum, valeurs.Effet);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = _creation
                ? "Règle créée. Vérifiez l'aperçu : c'est le moteur qui tranche, pas l'ordre de la liste."
                : valeurs.Effet is null
                    ? "Règle modifiée. Sa date de prise d'effet est inchangée."
                    : "Règle modifiée, DATE DE PRISE D'EFFET COMPRISE — elle peut désormais passer "
                      + "devant ses sœurs de même portée.";

            _creation = false;
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    private async Task AgirAsync(string geste)
    {
        if (_selection is not { } regle)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        if (geste == "supprimer")
        {
            var motif = await _saisie.MotifAsync(
                $"Motif de la suppression de « {regle.Resume} ». Désactiver la garderait lisible");

            if (string.IsNullOrWhiteSpace(motif))
            {
                return;
            }
        }

        var mot = _api.ElevationValide
            ? null
            : await _saisie.MotDePasseAsync($"{geste} — {regle.Resume}");

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
            var resultat = await _api.AgirSurCommissionAsync(regle.Id, geste);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = geste switch
            {
                "deactivate" => "Règle désactivée. Elle reste lisible, et réactivable.",
                "reactivate" => "Règle réactivée.",
                _ => "Règle supprimée.",
            };
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    private async Task CalculerAsync()
    {
        ErreurApercu = null;
        _apercu = null;
        Notifier(nameof(Apercu));
        Notifier(nameof(ApercuRegle));
        Notifier(nameof(AApercu));

        if (!Guid.TryParse(_vendeurEssai.Trim(), out var vendeur)
            || !Guid.TryParse(_categorieEssai.Trim(), out var categorie))
        {
            ErreurApercu = "Il faut un identifiant de vendeur ET un de catégorie : le moteur "
                           + "résout la règle en fonction des deux. Ils se copient depuis Vendeurs "
                           + "et Catégories.";
            return;
        }

        if (!decimal.TryParse(_montantEssai.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var montant)
            || montant <= 0m)
        {
            ErreurApercu = "Le montant brut doit être un nombre strictement positif.";
            return;
        }

        EnCours = true;

        try
        {
            var resultat = await _api.CalculerCommissionAsync(vendeur, categorie, montant, _devise.Trim());

            if (!resultat.Reussi || resultat.Valeur is null)
            {
                ErreurApercu = resultat.Message ?? "Aperçu indisponible.";
                return;
            }

            _apercu = resultat.Valeur;

            Notifier(nameof(Apercu));
            Notifier(nameof(ApercuRegle));
            Notifier(nameof(AApercu));
        }
        finally
        {
            EnCours = false;
        }
    }

    /// <remarks>
    /// LES REFUS ICI SONT CEUX DU DOMAINE, RECOPIÉS POUR ÉVITER L'ALLER-RETOUR.
    ///
    /// `CommissionRule.Create` refuse un taux hors [0, 1] et des frais négatifs ;
    /// le validateur exige une devise de trois lettres. Les vérifier avant
    /// l'envoi rend un message qui nomme le champ, là où le 400 serveur nomme la
    /// règle de validation.
    /// </remarks>
    private bool Lire(out ValeursCommission valeurs, out string? probleme)
    {
        valeurs = default;
        probleme = null;

        if (!decimal.TryParse(_taux.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var taux)
            || taux is < 0m or > 1m)
        {
            probleme = "Le taux doit être compris entre 0 et 1 — 0.15 pour 15 %.";
            return false;
        }

        if (!decimal.TryParse(_fixe.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var fixe)
            || fixe < 0m)
        {
            probleme = "Les frais fixes doivent être un nombre positif ou nul.";
            return false;
        }

        var devise = _devise.Trim().ToUpperInvariant();

        if (devise.Length != 3)
        {
            probleme = "La devise se note sur trois lettres — XOF.";
            return false;
        }

        decimal? minimum = null;
        decimal? maximum = null;

        if (!string.IsNullOrWhiteSpace(_minimum))
        {
            if (!decimal.TryParse(_minimum.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lu))
            {
                probleme = "Le minimum doit être un nombre, ou rester vide.";
                return false;
            }

            minimum = lu;
        }

        if (!string.IsNullOrWhiteSpace(_maximum))
        {
            if (!decimal.TryParse(_maximum.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lu))
            {
                probleme = "Le maximum doit être un nombre, ou rester vide.";
                return false;
            }

            maximum = lu;
        }

        // MIN > MAX N'EST PAS REFUSÉ PAR LE DOMAINE ICI.
        //
        // `ComputeCommission` applique le plancher PUIS le plafond, sans
        // `Math.Clamp` : min supérieur à max ne lève pas, il donne simplement le
        // maximum à chaque fois — un plancher qui ne plafonne rien. Ce n'est pas
        // une panne, c'est un réglage qui ne fait pas ce qu'on croit.
        if (minimum is { } plancher && maximum is { } plafond && plancher > plafond)
        {
            probleme = "Le minimum dépasse le maximum : la commission vaudrait toujours le "
                       + "maximum, et le plancher ne servirait à rien.";
            return false;
        }

        Guid? cible = null;

        if (CibleRequise)
        {
            if (!Guid.TryParse(_cible.Trim(), out var lu))
            {
                probleme = $"Une règle de portée « {_portee} » vise un identifiant précis : "
                           + "celui du vendeur ou de la catégorie.";
                return false;
            }

            cible = lu;
        }

        DateTime? effet = null;

        if (DateSeraEnvoyee)
        {
            if (!DateTime.TryParse(_effet.Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var lu))
            {
                probleme = "La date de prise d'effet suit le format AAAA-MM-JJ HH:MM (UTC).";
                return false;
            }

            effet = lu;
        }

        valeurs = new ValeursCommission(_portee, cible, taux, fixe, devise, minimum, maximum, effet);
        return true;
    }

    private readonly record struct ValeursCommission(
        string Portee, Guid? Cible, decimal Taux, decimal Fixe, string Devise,
        decimal? Minimum, decimal? Maximum, DateTime? Effet);
}

/// <summary>Une règle de commission, telle que la liste l'affiche.</summary>
public sealed class LigneCommission
{
    public LigneCommission(RegleCommission regle)
    {
        Id = regle.Id;
        Portee = regle.Scope;
        CibleBrute = regle.TargetId;
        TauxBrut = regle.Rate;
        FixeBrut = regle.FixedFee;
        Devise = regle.Currency;
        MinimumBrut = regle.MinFee;
        MaximumBrut = regle.MaxFee;
        EffetBrut = regle.EffectiveFromUtc;
        Active = regle.IsActive;

        var portee = regle.Scope switch
        {
            "Global" => "toute la plateforme",
            "Category" => "catégorie",
            "Seller" => "vendeur",
            _ => regle.Scope.ToLowerInvariant(),
        };

        Resume = regle.TargetId is { } cible
            ? $"{portee} {cible.ToString()[..8]}"
            : portee;

        Tarif = $"{regle.Rate * 100m:0.##} %"
                + (regle.FixedFee > 0m ? $" + {Argent.Formater(regle.FixedFee, regle.Currency)}" : string.Empty);

        var bornes = new List<string>();

        if (regle.MinFee is { } minimum)
        {
            bornes.Add($"min {Argent.Formater(minimum, regle.Currency)}");
        }

        if (regle.MaxFee is { } maximum)
        {
            bornes.Add($"max {Argent.Formater(maximum, regle.Currency)}");
        }

        Bornes = bornes.Count == 0 ? "aucune borne" : string.Join(" · ", bornes);

        // UNE RÈGLE ACTIVE PEUT N'ÊTRE PAS ENCORE APPLICABLE.
        //
        // `IsApplicableAt` teste `IsActive && EffectiveFromUtc <= nowUtc`. Une
        // règle programmée pour la semaine prochaine est « active » et ne
        // s'applique pas — deux choses que le seul drapeau ne distingue pas.
        Programmee = regle.IsActive && regle.EffectiveFromUtc > DateTime.UtcNow;

        Effet = regle.EffectiveFromUtc.ToLocalTime().ToString("dd/MM/yyyy");

        Etat = !regle.IsActive ? "désactivée"
            : Programmee ? "programmée"
            : "en vigueur";
    }

    public Guid Id { get; }

    public string Portee { get; }

    public Guid? CibleBrute { get; }

    public decimal TauxBrut { get; }

    public decimal FixeBrut { get; }

    public string Devise { get; }

    public decimal? MinimumBrut { get; }

    public decimal? MaximumBrut { get; }

    public DateTime EffetBrut { get; }

    public bool Active { get; }

    /// <summary>Active mais pas encore applicable.</summary>
    public bool Programmee { get; }

    public string Resume { get; }

    public string Tarif { get; }

    public string Bornes { get; }

    public string Effet { get; }

    public string Etat { get; }

    public bool EnVigueur => Active && !Programmee;
}
