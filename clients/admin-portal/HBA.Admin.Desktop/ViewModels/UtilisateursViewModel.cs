using System.Collections.ObjectModel;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Les comptes de la plateforme : les trouver, les suspendre, leur donner des rôles.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// CET ÉCRAN N'EXISTAIT PAS PARCE QU'UNE ROUTE MANQUAIT, PAS UNE REQUÊTE.
///
/// `ListUsersQuery` était écrite depuis le début — recherche, statut, tri,
/// pagination, comptage par statut — et rien ne la montait. Les cinq gestes
/// d'administration étant tous adressés par GUID, il fallait connaître
/// l'identifiant d'un compte pour le suspendre : autrement dit interroger la
/// base à la main.
///
/// LA RECHERCHE NE TROUVE PAS PAR E-MAIL, ET C'EST LE GESTE LE PLUS COURANT.
///
/// `Email` et `PhoneNumber` sont des value objects convertis : PostgreSQL ne sait
/// pas les comparer en `ILike`. Chercher « untel@exemple.com » rend donc une
/// liste vide, sans erreur — l'écran l'annonce en toutes lettres au lieu de
/// laisser conclure que le compte n'existe pas.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class UtilisateursViewModel : ViewModelBase
{
    private readonly ClientApiAdmin _api;
    private readonly IDemandeurDeSaisie _saisie;
    private readonly Dictionary<Guid, string> _nomsDeRoles = [];

    private LigneCompte? _selection;
    private LigneRoleAssignable? _roleChoisi;
    private string _recherche = string.Empty;
    private string? _statut;
    private int _page = 1;
    private long _total;
    private bool _suivante;
    private string? _erreur;
    private string? _confirmation;
    private bool _enCours;

    private const int Taille = 25;

    public UtilisateursViewModel(ClientApiAdmin api, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _saisie = saisie;

        Rafraichir = new CommandeAsync(ChargerAsync);
        Chercher = new CommandeAsync(ChercherAsync, () => !EnCours);
        Precedente = new CommandeAsync(() => AllerAsync(_page - 1), () => _page > 1 && !EnCours);
        Suivante = new CommandeAsync(() => AllerAsync(_page + 1), () => _suivante && !EnCours);
        Filtrer = new CommandeAsync<string>(FiltrerAsync, _ => !EnCours);
        Suspendre = new CommandeAsync(() => BasculerAsync(true), () => Suspendable && !EnCours);
        Reactiver = new CommandeAsync(() => BasculerAsync(false), () => Reactivable && !EnCours);
        Assigner = new CommandeAsync(AssignerAsync, () => Assignable && !EnCours);
        Retirer = new CommandeAsync<LigneRoleAssignable>(RetirerAsync, _ => !EnCours);

        Rafraichir.Execute(null);
    }

    public ObservableCollection<LigneCompte> Comptes { get; } = [];

    /// <summary>Les rôles du compte sélectionné, noms résolus.</summary>
    public ObservableCollection<LigneRoleAssignable> RolesDuCompte { get; } = [];

    /// <summary>Tous les rôles, pour l'assignation.</summary>
    public ObservableCollection<LigneRoleAssignable> RolesDisponibles { get; } = [];

    public LigneCompte? Selection
    {
        get => _selection;
        set
        {
            if (!Definir(ref _selection, value))
            {
                return;
            }

            RolesDuCompte.Clear();

            foreach (var role in value?.Roles ?? [])
            {
                _nomsDeRoles.TryGetValue(role, out var nom);
                RolesDuCompte.Add(new LigneRoleAssignable(role, nom ?? role.ToString()));
            }

            Notifier(nameof(ADesSelection));
            Notifier(nameof(Suspendable));
            Notifier(nameof(Reactivable));
            Notifier(nameof(Assignable));
            Notifier(nameof(Anonymise));
            Notifier(nameof(VerificationEmail));
            Reevaluer();
        }
    }

    public LigneRoleAssignable? RoleChoisi
    {
        get => _roleChoisi;
        set
        {
            if (Definir(ref _roleChoisi, value))
            {
                Notifier(nameof(Assignable));
                Reevaluer();
            }
        }
    }

    public string Recherche
    {
        get => _recherche;
        set => Definir(ref _recherche, value);
    }

    public bool ADesSelection => _selection is not null;

    /// <summary>Un compte anonymisé ne se suspend ni ne se réactive.</summary>
    /// <remarks>
    /// `UserStatus.Deleted` est irréversible par construction : « aucune méthode ne
    /// fait sortir de cet état — les données d'origine n'existent plus, il n'y a
    /// rien à restaurer ». Envoyer un geste ne produirait rien de visible.
    /// </remarks>
    public bool Anonymise => _selection is { Statut: "Deleted" };

    public bool Suspendable => _selection is { Statut: not "Suspended" and not "Deleted" };

    public bool Reactivable => _selection is { Statut: "Suspended" };

    public bool Assignable => _selection is not null && _roleChoisi is not null && !Anonymise;

    /// <summary>« Vérifié » et « vérifié sur parole » ne valent pas la même chose.</summary>
    public string VerificationEmail => _selection switch
    {
        null => string.Empty,
        { ParAdmin: true } => "vérifié PAR UN ADMINISTRATEUR, sur attestation",
        { EmailVerifie: true } => "vérifié par le titulaire",
        _ => "non vérifié",
    };

    public string? Statut
    {
        get => _statut;
        private set { if (Definir(ref _statut, value)) Notifier(nameof(LibelleFiltre)); }
    }

    public string LibelleFiltre => _statut is null ? "tous" : _statut;

    /// <summary>Les onglets de statut, avec leur compte venu des facettes.</summary>
    public ObservableCollection<LigneFacette> Facettes { get; } = [];

    public string Position => _total == 0
        ? "Aucun compte"
        : $"{_total} compte(s)  ·  page {_page}";

    /// <summary>Ce que la recherche ne sait pas faire, dit avant qu'on s'en aperçoive.</summary>
    public string LimiteRecherche =>
        "La recherche porte sur le prénom et le nom uniquement. L'e-mail et le téléphone "
        + "sont des objets convertis que la base ne sait pas comparer ainsi : les chercher "
        + "rend une liste vide, sans erreur.";

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

    public CommandeAsync Chercher { get; }

    public CommandeAsync Precedente { get; }

    public CommandeAsync Suivante { get; }

    public CommandeAsync<string> Filtrer { get; }

    public CommandeAsync Suspendre { get; }

    public CommandeAsync Reactiver { get; }

    public CommandeAsync Assigner { get; }

    public CommandeAsync<LigneRoleAssignable> Retirer { get; }

    private void Reevaluer()
    {
        Chercher.Reevaluer();
        Precedente.Reevaluer();
        Suivante.Reevaluer();
        Filtrer.Reevaluer();
        Suspendre.Reevaluer();
        Reactiver.Reevaluer();
        Assigner.Reevaluer();
        Retirer.Reevaluer();
    }

    /// <remarks>
    /// LES RÔLES SONT CHARGÉS UNE FOIS, ET SERVENT À NOMMER CE QUE LA PAGE REND.
    ///
    /// `UserSummary.RoleIds` ne porte que des GUID : le service des comptes et
    /// celui des rôles sont le même, mais deux routes, et aucune ne les joint.
    /// Afficher les identifiants bruts rendrait la colonne « rôles » illisible.
    /// </remarks>
    private async Task ChargerAsync()
    {
        EnCours = true;
        Erreur = null;

        try
        {
            if (_nomsDeRoles.Count == 0)
            {
                var roles = await _api.ListerRolesAsync();

                if (roles.Reussi && roles.Valeur is not null)
                {
                    RolesDisponibles.Clear();

                    foreach (var role in roles.Valeur.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
                    {
                        _nomsDeRoles[role.Id] = role.Name;
                        RolesDisponibles.Add(new LigneRoleAssignable(role.Id, role.Name));
                    }
                }
            }

            var page = await _api.ListerComptesAsync(_page, Taille, _recherche, _statut);

            if (!page.Reussi || page.Valeur?.Data is null)
            {
                Erreur = page.Message ?? "Liste des comptes indisponible.";
                return;
            }

            var choisi = _selection?.Id;

            Selection = null;
            Comptes.Clear();

            foreach (var compte in page.Valeur.Data)
            {
                Comptes.Add(new LigneCompte(compte, _nomsDeRoles));
            }

            _total = page.Valeur.Meta?.Total ?? Comptes.Count;
            _suivante = page.Valeur.Meta?.HasNext ?? false;

            RemplirFacettes(page.Valeur.Meta?.Facets);

            Selection = Comptes.FirstOrDefault(c => c.Id == choisi);

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

        // L'ordre suit l'énumération du domaine, pas l'ordre alphabétique : un
        // administrateur lit « en attente, actifs, suspendus, supprimés » comme un
        // cycle de vie, et il l'est.
        foreach (var cle in new[] { "PendingVerification", "Active", "Suspended", "Deleted" })
        {
            if (facettes.TryGetValue(cle, out var nombre))
            {
                Facettes.Add(new LigneFacette(cle, nombre, cle == _statut));
            }
        }
    }

    private async Task ChercherAsync()
    {
        _page = 1;
        await ChargerAsync();
    }

    private async Task AllerAsync(int page)
    {
        _page = Math.Max(1, page);
        await ChargerAsync();
    }

    private async Task FiltrerAsync(string statut)
    {
        // Cliquer l'onglet déjà actif le désactive : c'est le retour à « tous »
        // sans un bouton de plus, et le libellé le montre.
        Statut = string.Equals(_statut, statut, StringComparison.Ordinal) ? null : statut;
        _page = 1;
        await ChargerAsync();
    }

    private async Task BasculerAsync(bool suspendre)
    {
        if (_selection is not { } compte)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        // Suspendre coupe l'accès d'une personne : mot de passe exigé, comme pour
        // les autres gestes qui portent à conséquence sur un tiers.
        var mot = _api.ElevationValide
            ? null
            : await _saisie.MotDePasseAsync(suspendre
                ? $"Suspendre le compte de {compte.Nom}"
                : $"Réactiver le compte de {compte.Nom}");

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
            var resultat = await _api.BasculerCompteAsync(compte.Id, suspendre);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = suspendre
                ? $"Compte de {compte.Nom} suspendu. Les jetons de rafraîchissement sont révoqués, "
                  + "et la passerelle cesse d'accepter le jeton d'accès sous trente secondes."
                : $"Compte de {compte.Nom} réactivé.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    private async Task AssignerAsync()
    {
        if (_selection is not { } compte || _roleChoisi is not { } role)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        // Donner un rôle, c'est donner des permissions. Le geste passe par la même
        // porte que la suspension.
        var mot = _api.ElevationValide
            ? null
            : await _saisie.MotDePasseAsync($"Donner le rôle « {role.Nom} » à {compte.Nom}");

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
            var resultat = await _api.AssignerRoleAsync(compte.Id, role.Id);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = $"Rôle « {role.Nom} » donné à {compte.Nom}.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    private async Task RetirerAsync(LigneRoleAssignable role)
    {
        if (_selection is not { } compte)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        var mot = _api.ElevationValide
            ? null
            : await _saisie.MotDePasseAsync($"Retirer le rôle « {role.Nom} » à {compte.Nom}");

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
            var resultat = await _api.RetirerRoleAsync(compte.Id, role.Id);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = $"Rôle « {role.Nom} » retiré à {compte.Nom}.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }
}

/// <summary>Un compte, tel que la liste l'affiche.</summary>
public sealed class LigneCompte
{
    public LigneCompte(CompteAdmin compte, IReadOnlyDictionary<Guid, string> noms)
    {
        Id = compte.Id;
        Nom = $"{compte.FirstName} {compte.LastName}".Trim();
        Courriel = compte.Email;
        Telephone = compte.PhoneNumber;
        Statut = compte.Status;
        EmailVerifie = compte.EmailVerified;
        ParAdmin = compte.EmailVerifiedByAdminOnUtc is not null;
        Mfa = compte.MfaEnabled;
        Roles = compte.RoleIds ?? [];

        Etat = compte.Status switch
        {
            "Active" => "actif",
            "Suspended" => "suspendu",
            "PendingVerification" => "en attente",
            "Deleted" => "anonymisé",
            _ => compte.Status.ToLowerInvariant(),
        };

        LibelleRoles = Roles.Count == 0
            ? "aucun rôle"
            : string.Join(", ", Roles.Select(r => noms.TryGetValue(r, out var n) ? n : r.ToString()[..8]));

        Conditions = compte.AcceptedTermsVersion is { Length: > 0 } version
            ? $"CGU {version} acceptées le {compte.AcceptedTermsOnUtc?.ToLocalTime():dd/MM/yyyy}"
            : "CGU jamais acceptées";
    }

    public Guid Id { get; }

    public string Nom { get; }

    public string Courriel { get; }

    public string Telephone { get; }

    public string Statut { get; }

    public string Etat { get; }

    public bool EmailVerifie { get; }

    /// <summary>L'e-mail a été validé par un administrateur, sur attestation.</summary>
    public bool ParAdmin { get; }

    public bool Mfa { get; }

    public IReadOnlyList<Guid> Roles { get; }

    public string LibelleRoles { get; }

    public string Conditions { get; }

    public bool EstActif => Statut == "Active";
}

/// <summary>Un rôle, nommé, tel que l'écran le manipule.</summary>
public sealed class LigneRoleAssignable
{
    public LigneRoleAssignable(Guid id, string nom)
    {
        Id = id;
        Nom = nom;
    }

    public Guid Id { get; }

    public string Nom { get; }
}

/// <summary>Un onglet de statut, avec son compte.</summary>
/// <remarks>
/// PARTAGÉE ENTRE PLUSIEURS ÉCRANS, D'OÙ LE LIBELLÉ EN PARAMÈTRE.
///
/// Les facettes ont la même forme partout — une clé, un nombre, un état actif —
/// mais pas les mêmes noms : « Suspendus » pour un compte, « ARBITRAGE » pour un
/// dossier de retour. Le `switch` ci-dessous ne sert que de repli pour les
/// statuts de compte ; tout autre écran passe son propre libellé.
/// </remarks>
public sealed class LigneFacette
{
    public LigneFacette(string cle, int nombre, bool actif, string? libelle = null)
    {
        Cle = cle;
        Actif = actif;

        Libelle = libelle is { Length: > 0 }
            ? $"{libelle} ({nombre})"
            : cle switch
            {
                "Active" => $"Actifs ({nombre})",
                "Suspended" => $"Suspendus ({nombre})",
                "PendingVerification" => $"En attente ({nombre})",
                "Deleted" => $"Anonymisés ({nombre})",
                _ => $"{cle} ({nombre})",
            };
    }

    public string Cle { get; }

    public string Libelle { get; }

    public bool Actif { get; }
}
