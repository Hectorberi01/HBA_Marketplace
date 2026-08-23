using System.Collections.ObjectModel;
using System.Globalization;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>La grille tarifaire des courses.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// L'ÉCRAN LE PLUS LOURD DE CONSÉQUENCES DE TOUTE LA CONSOLE.
///
/// Une règle décide de ce que l'acheteur paie pour sa livraison et de ce que la
/// plateforme reverse. Une erreur ici ne se voit pas : elle se facture.
///
/// TROIS CHOSES QUE LE SERVEUR NE DIT PAS, ET QUE CET ÉCRAN DIT.
///
/// 1. UNE SEULE RÈGLE S'APPLIQUE. `CreateQuoteAsync` prend la première par
///    priorité décroissante parmi les actives en fenêtre. `Scope`,
///    `ServiceLevel` et `VehicleType` ne sont PAS dans le filtre : ils
///    n'entrent pas dans le choix. La console marque donc la règle qui gagne.
///
/// 2. DÉSACTIVER LA DERNIÈRE RÈGLE ÉLIGIBLE CASSE LE PASSAGE DE COMMANDE.
///    La requête finit par `FirstAsync`, pas `FirstOrDefaultAsync` : sans règle,
///    elle lève, et tout devis répond 500. L'écran refuse ce geste.
///
/// 3. UN MINIMUM SUPÉRIEUR AU PLAFOND FAIT LEVER CHAQUE DEVIS.
///    `Math.Clamp(subtotal, MinFee, MaxFee)` lève `ArgumentException` quand le
///    minimum dépasse le maximum. L'écran refuse d'enregistrer une telle règle.
///
/// Aucune de ces trois gardes n'existe côté serveur : ce sont des règles du
/// client, et elles ne remplacent pas les corrections qui manquent en amont.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class TarificationViewModel : ViewModelBase
{
    private readonly ClientApiAdmin _api;
    private readonly IDemandeurDeSaisie _saisie;

    private LigneRegle? _selection;
    private bool _creation;
    private string _nom = string.Empty;
    private string _portee = "GLOBAL";
    private string _niveau = "STANDARD";
    private string _vehicule = string.Empty;
    private string _baseFee = "0";
    private string _parKm = "0";
    private string _parMinute = "0";
    private string _minimum = "0";
    private string _maximum = string.Empty;
    private string _debut = string.Empty;
    private string _fin = string.Empty;
    private string _priorite = "100";
    private string _multiplicateur = "1";
    private string _distance = "5";
    private string _duree = "15";
    private string? _avertissement;
    private string? _erreur;
    private string? _confirmation;
    private bool _enCours;

    public TarificationViewModel(ClientApiAdmin api, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _saisie = saisie;

        Rafraichir = new CommandeAsync(ChargerAsync);
        Nouvelle = new CommandeAsync(NouvelleAsync, () => !EnCours);
        Enregistrer = new CommandeAsync(EnregistrerAsync, () => Modifiable && !EnCours);
        Activer = new CommandeAsync(() => BasculerAsync(true), () => Activable && !EnCours);
        Desactiver = new CommandeAsync(() => BasculerAsync(false), () => Desactivable && !EnCours);

        Rafraichir.Execute(null);
    }

    public ObservableCollection<LigneRegle> Regles { get; } = [];

    public LigneRegle? Selection
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
            Notifier(nameof(Modifiable));
            Notifier(nameof(Activable));
            Notifier(nameof(Desactivable));
            Notifier(nameof(RefusDeDesactivation));
            Notifier(nameof(ARefusDeDesactivation));
            Notifier(nameof(Titre));
            Simuler();
            Reevaluer();
        }
    }

    public bool ADesSelection => _selection is not null;

    /// <summary>Le panneau est ouvert : soit sur une règle, soit sur une création.</summary>
    public bool EnEdition => _selection is not null || _creation;

    public bool Modifiable => EnEdition && !string.IsNullOrWhiteSpace(_nom);

    public bool Activable => _selection is { Active: false };

    public bool Desactivable => _selection is { Active: true } && RefusDeDesactivation is null;

    /// <summary>
    /// Non nul quand désactiver cette règle ne laisserait aucune règle éligible.
    /// </summary>
    /// <remarks>
    /// LE CALCUL SE FAIT SUR LA FENÊTRE, PAS SEULEMENT SUR LE STATUT.
    ///
    /// Une règle « ACTIVE » dont `ActiveTo` est passé n'est PAS éligible : elle
    /// ne sauverait pas la plateforme. Compter les statuts au lieu des éligibles
    /// laisserait donc passer exactement le cas que cette garde doit empêcher.
    /// </remarks>
    public string? RefusDeDesactivation
    {
        get
        {
            if (_selection is not { Active: true, Eligible: true })
            {
                return null;
            }

            var restantes = Regles.Count(r => r.Eligible && r.Id != _selection.Id);

            return restantes > 0
                ? null
                : "C'est la dernière règle éligible. La désactiver ferait échouer TOUS les devis de "
                  + "course — `CreateQuoteAsync` lève au lieu de rendre null — et donc tout passage "
                  + "de commande, marketplace comme repas. Créez et activez la règle qui remplace "
                  + "celle-ci avant de la retirer.";
        }
    }

    public bool ARefusDeDesactivation => RefusDeDesactivation is not null;

    public string Titre => _creation ? "Nouvelle règle" : _selection?.Nom ?? "Règle";

    public string Nom
    {
        get => _nom;
        set { if (Definir(ref _nom, value)) { Notifier(nameof(Modifiable)); Reevaluer(); } }
    }

    /// <summary>Portée déclarée — stockée, affichée, et sans effet sur le choix.</summary>
    public string Portee
    {
        get => _portee;
        set => Definir(ref _portee, value);
    }

    public string Niveau
    {
        get => _niveau;
        set => Definir(ref _niveau, value);
    }

    public string Vehicule
    {
        get => _vehicule;
        set => Definir(ref _vehicule, value);
    }

    public string BaseFee
    {
        get => _baseFee;
        set { if (Definir(ref _baseFee, value)) Simuler(); }
    }

    public string ParKm
    {
        get => _parKm;
        set { if (Definir(ref _parKm, value)) Simuler(); }
    }

    public string ParMinute
    {
        get => _parMinute;
        set { if (Definir(ref _parMinute, value)) Simuler(); }
    }

    public string Minimum
    {
        get => _minimum;
        set { if (Definir(ref _minimum, value)) Simuler(); }
    }

    /// <summary>Plafond. Vide signifie AUCUN plafond, pas « inchangé ».</summary>
    public string Maximum
    {
        get => _maximum;
        set { if (Definir(ref _maximum, value)) Simuler(); }
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

    public string Priorite
    {
        get => _priorite;
        set => Definir(ref _priorite, value);
    }

    public string Multiplicateur
    {
        get => _multiplicateur;
        set { if (Definir(ref _multiplicateur, value)) Simuler(); }
    }

    public string Distance
    {
        get => _distance;
        set { if (Definir(ref _distance, value)) Simuler(); }
    }

    public string Duree
    {
        get => _duree;
        set { if (Definir(ref _duree, value)) Simuler(); }
    }

    public string Simulation { get; private set; } = string.Empty;

    public string Detail { get; private set; } = string.Empty;

    /// <summary>Anomalies de grille : priorités en doublon, aucune règle éligible.</summary>
    public string? Avertissement
    {
        get => _avertissement;
        private set { if (Definir(ref _avertissement, value)) Notifier(nameof(AAvertissement)); }
    }

    public bool AAvertissement => !string.IsNullOrEmpty(_avertissement);

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
        ? "Aucune règle"
        : $"{Regles.Count} règle(s), dont {Regles.Count(r => r.Eligible)} éligible(s)";

    public CommandeAsync Rafraichir { get; }

    public CommandeAsync Nouvelle { get; }

    public CommandeAsync Enregistrer { get; }

    public CommandeAsync Activer { get; }

    public CommandeAsync Desactiver { get; }

    private void Reevaluer()
    {
        Nouvelle.Reevaluer();
        Enregistrer.Reevaluer();
        Activer.Reevaluer();
        Desactiver.Reevaluer();
    }

    private async Task ChargerAsync()
    {
        EnCours = true;
        Erreur = null;

        try
        {
            var choisie = _selection?.Id;
            var resultat = await _api.ListerReglesTarifairesAsync();

            if (!resultat.Reussi || resultat.Valeur is null)
            {
                Erreur = resultat.Message ?? "Grille indisponible.";
                return;
            }

            Selection = null;
            Regles.Clear();

            var maintenant = DateTimeOffset.UtcNow;
            var gagnanteTrouvee = false;

            // L'ordre du serveur est conservé : c'est celui dans lequel le moteur
            // de devis regarde les règles. La première éligible rencontrée est
            // celle qui tarife tout.
            foreach (var regle in resultat.Valeur)
            {
                var eligible = regle.Status == "ACTIVE"
                               && regle.ActiveFrom <= maintenant
                               && (regle.ActiveTo is null || regle.ActiveTo > maintenant);

                var gagnante = eligible && !gagnanteTrouvee;
                gagnanteTrouvee |= gagnante;

                Regles.Add(new LigneRegle(regle, eligible, gagnante));
            }

            Selection = Regles.FirstOrDefault(r => r.Id == choisie);

            Verifier();
            Notifier(nameof(Position));
        }
        finally
        {
            EnCours = false;
        }
    }

    /// <summary>Signale ce qui, dans la grille, produira un comportement inattendu.</summary>
    private void Verifier()
    {
        var soucis = new List<string>();

        var eligibles = Regles.Where(r => r.Eligible).ToList();

        if (eligibles.Count == 0)
        {
            soucis.Add("AUCUNE règle n'est éligible en ce moment : tout devis de course répond 500, "
                       + "et aucune commande ne peut être passée.");
        }

        // À priorité égale, `OrderByDescending` seul ne départage pas : le moteur
        // peut rendre l'une ou l'autre, et pas forcément la même deux fois.
        var doublons = eligibles
            .GroupBy(r => r.PrioriteBrute)
            .Where(g => g.Count() > 1)
            .Select(g => $"priorité {g.Key} : {string.Join(", ", g.Select(r => r.Nom))}")
            .ToList();

        if (doublons.Count > 0)
        {
            soucis.Add("Plusieurs règles éligibles partagent la même priorité — le tri ne les "
                       + "départage pas, la règle appliquée n'est pas prévisible : "
                       + string.Join(" ; ", doublons) + ".");
        }

        Avertissement = soucis.Count == 0 ? null : string.Join(" ", soucis);
    }

    private void Remplir(LigneRegle ligne)
    {
        _nom = ligne.Nom;
        _portee = ligne.Portee;
        _niveau = ligne.Niveau;
        _vehicule = ligne.Vehicule;
        _baseFee = ligne.BaseFeeBrut.ToString(CultureInfo.InvariantCulture);
        _parKm = ligne.ParKmBrut.ToString(CultureInfo.InvariantCulture);
        _parMinute = ligne.ParMinuteBrut.ToString(CultureInfo.InvariantCulture);
        _minimum = ligne.MinimumBrut.ToString(CultureInfo.InvariantCulture);
        _maximum = ligne.MaximumBrut?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _debut = ligne.DebutBrut.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        _fin = ligne.FinBrute?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
        _priorite = ligne.PrioriteBrute.ToString(CultureInfo.InvariantCulture);
        _multiplicateur = ligne.MultiplicateurBrut.ToString(CultureInfo.InvariantCulture);

        NotifierLesChamps();
    }

    /// <summary>
    /// Notifie les treize champs d'un coup, après les avoir posés en champ privé.
    /// </summary>
    /// <remarks>
    /// LES CHAMPS SONT ÉCRITS DIRECTEMENT, PAS PAR LEURS PROPRIÉTÉS.
    ///
    /// Passer par les `set` déclencherait treize `Simuler()` en cascade, chacun
    /// sur un état à moitié rempli — donc treize aperçus dont douze faux, et le
    /// dernier seul correct. On pose les champs, puis on notifie, puis on simule
    /// une fois.
    /// </remarks>
    private void NotifierLesChamps()
    {
        Notifier(nameof(Nom));
        Notifier(nameof(Portee));
        Notifier(nameof(Niveau));
        Notifier(nameof(Vehicule));
        Notifier(nameof(BaseFee));
        Notifier(nameof(ParKm));
        Notifier(nameof(ParMinute));
        Notifier(nameof(Minimum));
        Notifier(nameof(Maximum));
        Notifier(nameof(Debut));
        Notifier(nameof(Fin));
        Notifier(nameof(Priorite));
        Notifier(nameof(Multiplicateur));
    }

    /// <summary>
    /// Recalcule le prix d'une course d'essai à partir des champs affichés.
    /// </summary>
    /// <remarks>
    /// ═══════════════════════════════════════════════════════════════════════
    /// LE CALCUL EST RECOPIÉ DE `PricingPolicy`, ET C'EST UNE DUPLICATION
    ///    ASSUMÉE.
    ///
    /// Il n'existe aucune route pour demander « combien coûterait telle course
    /// avec telle règle » : `POST /quotes` crée un devis réel, persisté et
    /// publié en événement — on ne simule pas avec cela. Les cinq lignes de
    /// `PricingPolicy` sont donc reprises ici :
    ///
    ///     distanceFee   = ceil(mètres / 1000 × perKm)
    ///     minuteFee     = ceil(secondes / 60 × perMinute)
    ///     sousTotalBase = base + distanceFee + minuteFee
    ///     majoré        = round(sousTotalBase × surge, AwayFromZero)
    ///     total         = clamp(majoré, min, max)
    ///
    /// CE QUE CELA IMPLIQUE : si le service change sa formule, cet aperçu ment
    /// jusqu'à ce que quelqu'un le remarque. C'est le prix à payer pour voir
    /// l'effet d'un réglage AVANT de le facturer à des clients ; l'aperçu est
    /// donc étiqueté comme un calcul local, pas comme une réponse du serveur.
    /// ═══════════════════════════════════════════════════════════════════════
    /// </remarks>
    private void Simuler()
    {
        Simulation = string.Empty;
        Detail = string.Empty;

        if (!EnEdition)
        {
            Notifier(nameof(Simulation));
            Notifier(nameof(Detail));
            return;
        }

        if (!long.TryParse(_baseFee.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var basique)
            || !long.TryParse(_parKm.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parKm)
            || !long.TryParse(_parMinute.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parMinute)
            || !long.TryParse(_minimum.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minimum)
            || !decimal.TryParse(_multiplicateur.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var surge)
            || !decimal.TryParse(_distance.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var km)
            || !decimal.TryParse(_duree.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes))
        {
            Simulation = "Aperçu indisponible : un des montants n'est pas un nombre.";
            Notifier(nameof(Simulation));
            Notifier(nameof(Detail));
            return;
        }

        long? maximum = null;

        if (!string.IsNullOrWhiteSpace(_maximum))
        {
            if (!long.TryParse(_maximum.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var plafond))
            {
                Simulation = "Aperçu indisponible : le plafond n'est pas un nombre.";
                Notifier(nameof(Simulation));
                Notifier(nameof(Detail));
                return;
            }

            maximum = plafond;
        }

        if (maximum is { } borne && minimum > borne)
        {
            Simulation = "Minimum supérieur au plafond : chaque devis lèverait "
                         + "(`Math.Clamp` refuse un minimum plus grand que le maximum).";
            Notifier(nameof(Simulation));
            Notifier(nameof(Detail));
            return;
        }

        var metres = (int)Math.Max(0m, km * 1000m);
        var secondes = (int)Math.Max(0m, minutes * 60m);

        var fraisDistance = (long)Math.Ceiling(metres / 1000m * parKm);
        var fraisMinute = (long)Math.Ceiling(secondes / 60m * parMinute);
        var sousTotal = basique + fraisDistance + fraisMinute;
        var majore = (long)Math.Round(sousTotal * surge, MidpointRounding.AwayFromZero);
        var majoration = Math.Max(0, majore - sousTotal);
        var total = Math.Clamp(sousTotal + majoration, minimum, maximum ?? long.MaxValue);

        Simulation = $"Course d'essai : {Argent.Formater(total, "XOF")}";

        var morceaux = new List<string>
        {
            $"base {Argent.Formater(basique, "XOF")}",
            $"distance {Argent.Formater(fraisDistance, "XOF")}",
            $"durée {Argent.Formater(fraisMinute, "XOF")}",
        };

        if (majoration > 0)
        {
            morceaux.Add($"majoration {Argent.Formater(majoration, "XOF")}");
        }

        if (total != sousTotal + majoration)
        {
            morceaux.Add(total == minimum ? "ramené au minimum" : "ramené au plafond");
        }

        Detail = string.Join("  ·  ", morceaux) + "  ·  calcul local, recopié de PricingPolicy";

        Notifier(nameof(Simulation));
        Notifier(nameof(Detail));
    }

    private Task NouvelleAsync()
    {
        Erreur = null;
        Confirmation = null;

        Selection = null;
        _creation = true;

        _nom = string.Empty;
        _portee = "GLOBAL";
        _niveau = "STANDARD";
        _vehicule = string.Empty;
        _baseFee = "0";
        _parKm = "0";
        _parMinute = "0";
        _minimum = "0";
        _maximum = string.Empty;

        // Une règle créée sans date de début ne s'appliquerait jamais tant que
        // `ActiveFrom` reste dans le futur — et vaudrait `0001-01-01` si le champ
        // partait vide. On propose donc maintenant, en UTC comme le serveur.
        _debut = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        _fin = string.Empty;

        // Priorité volontairement basse : une nouvelle règle ne doit pas prendre
        // la main sur la grille en place au seul fait d'avoir été créée.
        _priorite = "10";
        _multiplicateur = "1";

        NotifierLesChamps();

        Notifier(nameof(EnEdition));
        Notifier(nameof(Modifiable));
        Notifier(nameof(Titre));
        Simuler();
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

        // Toucher à la grille tarifaire n'est pas destructeur au sens d'une
        // suppression, mais cela change ce que la plateforme facture dès le devis
        // suivant. La ré-authentification est ici la garantie que la personne
        // devant l'écran est bien celle qui a ouvert la session.
        var mot = _api.ElevationValide
            ? null
            : await _saisie.MotDePasseAsync(_creation
                ? $"Créer la règle tarifaire « {valeurs.Nom} »"
                : $"Modifier la règle tarifaire « {valeurs.Nom} »");

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
            var resultat = await _api.EnregistrerRegleTarifaireAsync(
                _creation ? null : _selection?.Id,
                valeurs.Nom, valeurs.Portee, valeurs.Niveau, valeurs.Vehicule,
                valeurs.Base, valeurs.ParKm, valeurs.ParMinute, valeurs.Minimum, valeurs.Maximum,
                valeurs.Debut, valeurs.Fin, valeurs.Priorite, valeurs.Multiplicateur);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            // Une règle créée naît ACTIVE — `AddRuleAsync` pose « ACTIVE » en dur.
            // Si sa priorité dépasse celle de la règle en place, elle tarife tout
            // dès maintenant, et le message doit le dire.
            Confirmation = _creation
                ? $"Règle « {valeurs.Nom} » créée, et ACTIVE immédiatement : le service la pose "
                  + "ainsi. Vérifiez ci-contre laquelle gagne désormais."
                : $"Règle « {valeurs.Nom} » enregistrée. Son statut n'a pas changé — "
                  + "activation et désactivation passent par leurs propres gestes.";

            _creation = false;
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    private async Task BasculerAsync(bool activer)
    {
        if (_selection is not { } regle)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        if (!activer && RefusDeDesactivation is { } refus)
        {
            Erreur = refus;
            return;
        }

        var mot = _api.ElevationValide
            ? null
            : await _saisie.MotDePasseAsync(activer
                ? $"Activer « {regle.Nom} »"
                : $"Désactiver « {regle.Nom} »");

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
            var resultat = await _api.BasculerRegleTarifaireAsync(regle.Id, activer);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = activer
                ? $"« {regle.Nom} » activée."
                : $"« {regle.Nom} » désactivée.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    /// <summary>Lit et vérifie les treize champs avant l'envoi.</summary>
    /// <remarks>
    /// LES REFUS ICI SONT CEUX QUE LE SERVEUR N'OPPOSE PAS.
    ///
    /// Il n'y a aucun validateur sur `PricingRuleRequest` : un minimum supérieur
    /// au plafond, une date de fin antérieure au début ou un multiplicateur
    /// négatif sont acceptés et stockés. Les deux premiers cassent le calcul du
    /// devis, le troisième produit des prix négatifs ramenés au minimum.
    /// </remarks>
    private bool Lire(out ValeursRegle valeurs, out string? probleme)
    {
        valeurs = default;
        probleme = null;

        if (string.IsNullOrWhiteSpace(_nom))
        {
            probleme = "Le nom est obligatoire.";
            return false;
        }

        if (!long.TryParse(_baseFee.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var basique)
            || !long.TryParse(_parKm.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parKm)
            || !long.TryParse(_parMinute.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parMinute)
            || !long.TryParse(_minimum.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minimum))
        {
            probleme = "Les montants doivent être des nombres entiers, en francs CFA sans décimale.";
            return false;
        }

        if (basique < 0 || parKm < 0 || parMinute < 0 || minimum < 0)
        {
            probleme = "Aucun montant ne peut être négatif.";
            return false;
        }

        long? maximum = null;

        if (!string.IsNullOrWhiteSpace(_maximum))
        {
            if (!long.TryParse(_maximum.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var plafond))
            {
                probleme = "Le plafond doit être un nombre entier, ou rester vide pour aucun plafond.";
                return false;
            }

            if (plafond < minimum)
            {
                probleme = "Le plafond est inférieur au minimum. `Math.Clamp` lèverait à chaque devis, "
                           + "et tout passage de commande répondrait 500.";
                return false;
            }

            maximum = plafond;
        }

        if (!int.TryParse(_priorite.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var priorite))
        {
            probleme = "La priorité doit être un nombre entier.";
            return false;
        }

        if (!decimal.TryParse(_multiplicateur.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var surge)
            || surge <= 0m)
        {
            probleme = "Le multiplicateur doit être un nombre strictement positif — 1 pour aucun effet.";
            return false;
        }

        if (!LireDate(_debut, out var debut))
        {
            probleme = "La date de début est obligatoire, au format AAAA-MM-JJ HH:MM (UTC).";
            return false;
        }

        DateTimeOffset? fin = null;

        if (!string.IsNullOrWhiteSpace(_fin))
        {
            if (!LireDate(_fin, out var borne))
            {
                probleme = "La date de fin doit suivre le format AAAA-MM-JJ HH:MM (UTC), ou rester vide.";
                return false;
            }

            if (borne <= debut)
            {
                probleme = "La date de fin précède le début : la règle ne serait jamais éligible.";
                return false;
            }

            fin = borne;
        }

        valeurs = new ValeursRegle(
            _nom.Trim(),
            string.IsNullOrWhiteSpace(_portee) ? "GLOBAL" : _portee.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(_niveau) ? "STANDARD" : _niveau.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(_vehicule) ? null : _vehicule.Trim().ToUpperInvariant(),
            basique, parKm, parMinute, minimum, maximum, debut, fin, priorite, surge);

        return true;
    }

    /// <remarks>
    /// LA DATE EST LUE EN UTC, PARCE QUE LE SERVEUR COMPARE À `UtcNow`.
    ///
    /// `AssumeUniversal | AdjustToUniversal` : une saisie sans fuseau est prise
    /// pour de l'UTC. L'interpréter dans le fuseau du poste décalerait la fenêtre
    /// d'une heure ou deux selon la machine de l'administrateur, ce qui est
    /// exactement le genre d'écart qu'on ne voit jamais à l'écran.
    /// </remarks>
    private static bool LireDate(string texte, out DateTimeOffset valeur)
        => DateTimeOffset.TryParse(
            texte.Trim(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out valeur);

    private readonly record struct ValeursRegle(
        string Nom, string Portee, string Niveau, string? Vehicule,
        long Base, long ParKm, long ParMinute, long Minimum, long? Maximum,
        DateTimeOffset Debut, DateTimeOffset? Fin, int Priorite, decimal Multiplicateur);
}

/// <summary>Une règle tarifaire, telle que la liste l'affiche.</summary>
public sealed class LigneRegle
{
    public LigneRegle(RegleTarifaire regle, bool eligible, bool gagnante)
    {
        Id = regle.Id;
        Nom = regle.Name;
        Portee = regle.Scope;
        Niveau = regle.ServiceLevel;
        Vehicule = regle.VehicleType ?? string.Empty;
        BaseFeeBrut = regle.BaseFee;
        ParKmBrut = regle.PerKmFee;
        ParMinuteBrut = regle.PerMinuteFee;
        MinimumBrut = regle.MinFee;
        MaximumBrut = regle.MaxFee;
        DebutBrut = regle.ActiveFrom;
        FinBrute = regle.ActiveTo;
        PrioriteBrute = regle.Priority;
        MultiplicateurBrut = regle.SurgeMultiplier;
        Active = regle.Status == "ACTIVE";
        Eligible = eligible;
        Gagnante = gagnante;

        Resume = $"{regle.Scope} · {regle.ServiceLevel}"
                 + (string.IsNullOrWhiteSpace(regle.VehicleType) ? string.Empty : $" · {regle.VehicleType}")
                 + $"  —  priorité {regle.Priority}";

        Tarif = $"{Argent.Formater(regle.BaseFee, "XOF")} + {Argent.Formater(regle.PerKmFee, "XOF")}/km"
                + (regle.PerMinuteFee > 0 ? $" + {Argent.Formater(regle.PerMinuteFee, "XOF")}/min" : string.Empty)
                + (regle.SurgeMultiplier == 1m ? string.Empty : $"  ×{regle.SurgeMultiplier}");

        // Une règle « ACTIVE » hors fenêtre n'est pas éligible, et c'est le genre
        // d'écart qu'on ne voit pas en lisant un statut.
        Etat = !Active ? "inactive"
            : eligible ? "active"
            : regle.ActiveFrom > DateTimeOffset.UtcNow ? "pas encore"
            : "expirée";
    }

    public Guid Id { get; }

    public string Nom { get; }

    public string Portee { get; }

    public string Niveau { get; }

    public string Vehicule { get; }

    public long BaseFeeBrut { get; }

    public long ParKmBrut { get; }

    public long ParMinuteBrut { get; }

    public long MinimumBrut { get; }

    public long? MaximumBrut { get; }

    public DateTimeOffset DebutBrut { get; }

    public DateTimeOffset? FinBrute { get; }

    public int PrioriteBrute { get; }

    public decimal MultiplicateurBrut { get; }

    public bool Active { get; }

    /// <summary>Active ET dans sa fenêtre de validité.</summary>
    public bool Eligible { get; }

    /// <summary>Celle que le moteur de devis appliquera à la prochaine course.</summary>
    public bool Gagnante { get; }

    public string Resume { get; }

    public string Tarif { get; }

    public string Etat { get; }
}
