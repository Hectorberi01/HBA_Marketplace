using System.Collections.ObjectModel;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Les rôles : qui peut quoi sur la plateforme.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// CET ÉCRAN VIENT AVANT « UTILISATEURS », ET PAS SEULEMENT DANS LE PANNEAU.
///
/// Assigner un rôle à quelqu'un suppose de savoir quels rôles existent et ce
/// qu'ils portent. Écrire l'écran des utilisateurs d'abord aurait donné une
/// liste déroulante de noms dont personne ne connaît le contenu.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class RolesViewModel : ViewModelBase
{
    private readonly ClientApiAdmin _api;
    private readonly IDemandeurDeSaisie _saisie;

    private LigneRole? _selection;
    private string _nom = string.Empty;
    private string _description = string.Empty;
    private string _permissions = string.Empty;
    private string? _erreur;
    private string? _confirmation;
    private bool _enCours;

    public RolesViewModel(ClientApiAdmin api, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _saisie = saisie;

        Rafraichir = new CommandeAsync(ChargerAsync);
        Creer = new CommandeAsync(CreerAsync, () => !EnCours);
        Enregistrer = new CommandeAsync(EnregistrerAsync, () => Modifiable && !EnCours);
        EnregistrerPermissions = new CommandeAsync(PoserPermissionsAsync, () => ADesSelection && !EnCours);
        Supprimer = new CommandeAsync(SupprimerAsync, () => Supprimable && !EnCours);

        Rafraichir.Execute(null);
    }

    public ObservableCollection<LigneRole> Roles { get; } = [];

    public LigneRole? Selection
    {
        get => _selection;
        set
        {
            if (!Definir(ref _selection, value))
            {
                return;
            }

            // Les champs suivent la sélection : sans cela, on éditerait le rôle
            // précédent en croyant modifier celui qu'on vient de cliquer.
            Nom = value?.Nom ?? string.Empty;
            Description = value?.Description ?? string.Empty;
            Permissions = value is null ? string.Empty : string.Join('\n', value.Permissions);

            Notifier(nameof(ADesSelection));
            Notifier(nameof(Modifiable));
            Notifier(nameof(Supprimable));
            Notifier(nameof(EstSysteme));
            Reevaluer();
        }
    }

    public bool ADesSelection => _selection is not null;

    /// <summary>
    /// Un rôle système se lit et se modifie, mais ne se supprime pas.
    /// </summary>
    /// <remarks>
    /// LE DOMAINE REFUSE LA SUPPRESSION, PAS LA MODIFICATION.
    ///
    /// `Role.IsSystem` est documenté « Rôle système (Buyer, Seller, Admin…) : non
    /// supprimable ». Rien n'interdit d'en changer les permissions — et c'est
    /// heureux, sinon on ne pourrait jamais ajuster ce que peut un vendeur.
    /// L'écran grise donc la suppression, et seulement elle.
    /// </remarks>
    public bool EstSysteme => _selection?.EstSysteme ?? false;

    public bool Modifiable => _selection is not null && !string.IsNullOrWhiteSpace(_nom);

    public bool Supprimable => _selection is not null && !_selection.EstSysteme;

    public string Nom
    {
        get => _nom;
        set { if (Definir(ref _nom, value)) { Notifier(nameof(Modifiable)); Reevaluer(); } }
    }

    public string Description
    {
        get => _description;
        set => Definir(ref _description, value);
    }

    /// <summary>Les permissions, une par ligne.</summary>
    /// <remarks>
    /// UN CHAMP DE TEXTE, PAS UNE LISTE DÉROULANTE.
    ///
    /// Le domaine ne connaît AUCUNE liste fermée de permissions : il valide une
    /// forme, « ressource.action ». Une liste déroulante devrait donc être écrite
    /// à la main dans la console, et elle serait fausse au premier droit ajouté
    /// par un service.
    /// </remarks>
    public string Permissions
    {
        get => _permissions;
        set => Definir(ref _permissions, value);
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

    public string Position => Roles.Count == 0 ? "Aucun rôle" : $"{Roles.Count} rôle(s)";

    public CommandeAsync Rafraichir { get; }

    public CommandeAsync Creer { get; }

    public CommandeAsync Enregistrer { get; }

    public CommandeAsync EnregistrerPermissions { get; }

    public CommandeAsync Supprimer { get; }

    private void Reevaluer()
    {
        Creer.Reevaluer();
        Enregistrer.Reevaluer();
        EnregistrerPermissions.Reevaluer();
        Supprimer.Reevaluer();
    }

    private async Task ChargerAsync()
    {
        EnCours = true;
        Erreur = null;

        try
        {
            var resultat = await _api.ListerRolesAsync();

            if (!resultat.Reussi || resultat.Valeur is null)
            {
                Erreur = resultat.Message ?? "Liste indisponible.";
                return;
            }

            var choisi = _selection?.Id;

            Selection = null;
            Roles.Clear();

            foreach (var role in resultat.Valeur)
            {
                Roles.Add(new LigneRole(role));
            }

            // On retrouve la sélection après rechargement : sans cela, chaque
            // enregistrement ramènerait le panneau à vide, et il faudrait
            // recliquer pour poser la modification suivante.
            Selection = Roles.FirstOrDefault(r => r.Id == choisi);

            Notifier(nameof(Position));
        }
        finally
        {
            EnCours = false;
        }
    }

    private async Task CreerAsync()
    {
        Erreur = null;
        Confirmation = null;

        var nom = await _saisie.MotifAsync("Nom du nouveau rôle");

        if (string.IsNullOrWhiteSpace(nom))
        {
            return;
        }

        EnCours = true;

        try
        {
            // Un rôle naît SANS permission : les poser demande de les lire, et on
            // ne peut pas les lire dans une boîte d'une ligne. La création ouvre
            // la porte, l'édition la remplit.
            var resultat = await _api.CreerRoleAsync(nom.Trim(), null, []);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = $"Rôle « {nom.Trim()} » créé, sans permission.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    private async Task EnregistrerAsync()
    {
        if (_selection is not { } role || string.IsNullOrWhiteSpace(_nom))
        {
            return;
        }

        Erreur = null;
        Confirmation = null;
        EnCours = true;

        try
        {
            var resultat = await _api.RenommerRoleAsync(
                role.Id, _nom.Trim(),
                string.IsNullOrWhiteSpace(_description) ? null : _description.Trim());

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = "Nom et description enregistrés.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    /// <remarks>
    /// LA FORME EST VÉRIFIÉE AVANT L'ENVOI, ET LA LIGNE FAUTIVE EST NOMMÉE.
    ///
    /// Le service refuse une permission mal formée par un 400 qui ne dit pas
    /// laquelle. Sur une liste de vingt lignes, cela oblige à les relire une à
    /// une — alors que le motif est connu et recopié dans `FormatPermission`.
    /// </remarks>
    private async Task PoserPermissionsAsync()
    {
        if (_selection is not { } role)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        var permissions = FormatPermission.Decouper(_permissions);

        if (FormatPermission.PremiereInvalide(permissions) is { } fautive)
        {
            Erreur = $"Permission invalide : « {fautive} ». Format attendu : ressource.action, "
                     + "en minuscules, chiffres et soulignés autorisés.";
            return;
        }

        var mot = _api.ElevationValide
            ? null
            : await _saisie.MotDePasseAsync($"Permissions du rôle {role.Nom}");

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
            var resultat = await _api.PoserPermissionsAsync(role.Id, permissions);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = permissions.Count == 0
                ? "Toutes les permissions ont été retirées."
                : $"{permissions.Count} permission(s) enregistrée(s).";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    private async Task SupprimerAsync()
    {
        if (_selection is not { EstSysteme: false } role)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        var mot = await _saisie.MotDePasseAsync($"Supprimer le rôle {role.Nom}");

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
            var resultat = await _api.SupprimerRoleAsync(role.Id);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = $"Rôle « {role.Nom} » supprimé.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }
}

/// <summary>Un rôle, tel que la liste l'affiche.</summary>
public sealed class LigneRole
{
    public LigneRole(RoleAdmin role)
    {
        Id = role.Id;
        Nom = role.Name;
        Description = role.Description ?? string.Empty;
        EstSysteme = role.IsSystem;
        Permissions = role.Permissions ?? [];
        Compte = Permissions.Count == 0 ? "aucune permission" : $"{Permissions.Count} permission(s)";
    }

    public Guid Id { get; }

    public string Nom { get; }

    public string Description { get; }

    public bool EstSysteme { get; }

    public IReadOnlyList<string> Permissions { get; }

    public string Compte { get; }
}
