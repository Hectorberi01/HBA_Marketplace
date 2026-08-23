using System.Collections.ObjectModel;
using System.Text.Json;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>L'arbre des catégories et le schéma d'attributs de chacune.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// C'EST L'ÉCRAN QUI DÉCIDE DE CE QU'UN VENDEUR PEUT METTRE EN VENTE.
///
/// Deux choses en sortent, et aucune des deux n'est cosmétique. La publication
/// d'une catégorie décide de sa présence dans la vitrine ; le rattachement d'un
/// attribut REQUIS décide qu'une fiche sans cet attribut sera refusée à la
/// soumission — `ChangeProductStatusCommandHandler` appelle
/// `ValidationDesAttributs.Valider` au passage en `PendingReview`.
///
/// L'écran le dit à chaque geste, parce que rien ne le rappelle ailleurs : le
/// vendeur, lui, découvrira la règle en butant dessus.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class CategoriesViewModel : ViewModelBase
{
    private readonly ClientApiAdmin _api;
    private readonly IDemandeurDeSaisie _saisie;
    private readonly List<LigneCategorie> _arbre = [];

    private LigneCategorie? _selection;
    private LigneDefinition? _definition;
    private string _recherche = string.Empty;
    private string _nom = string.Empty;
    private string _image = string.Empty;
    private string _schema = string.Empty;
    private string _ordre = "0";
    private bool _requis;
    private bool _variante;
    private bool _cascade;
    private string? _incoherences;
    private string? _erreur;
    private string? _confirmation;
    private bool _enCours;

    public CategoriesViewModel(ClientApiAdmin api, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _saisie = saisie;

        Rafraichir = new CommandeAsync(ChargerAsync);
        Creer = new CommandeAsync(CreerAsync, () => !EnCours);
        Enregistrer = new CommandeAsync(EnregistrerAsync, () => Modifiable && !EnCours);
        Publier = new CommandeAsync(() => BasculerAsync(true), () => Basculable && !EnCours);
        Depublier = new CommandeAsync(() => BasculerAsync(false), () => Basculable && !EnCours);
        Supprimer = new CommandeAsync(SupprimerAsync, () => Supprimable && !EnCours);
        Rattacher = new CommandeAsync(RattacherAsync, () => Rattachable && !EnCours);
        Detacher = new CommandeAsync<LigneAttributCategorie>(DetacherAsync, _ => !EnCours);
        ChargerAttributs = new CommandeAsync(ChargerAttributsAsync);
        CreerDefinition = new CommandeAsync(CreerDefinitionAsync, () => !EnCours);

        Rafraichir.Execute(null);
    }

    public ObservableCollection<LigneCategorie> Categories { get; } = [];

    public ObservableCollection<LigneAttributCategorie> Attributs { get; } = [];

    public ObservableCollection<LigneDefinition> Definitions { get; } = [];

    public LigneCategorie? Selection
    {
        get => _selection;
        set
        {
            if (!Definir(ref _selection, value))
            {
                return;
            }

            Nom = value?.Nom ?? string.Empty;
            Image = value?.Image ?? string.Empty;
            Schema = value?.Schema ?? string.Empty;

            Notifier(nameof(ADesSelection));
            Notifier(nameof(Modifiable));
            Notifier(nameof(Basculable));
            Notifier(nameof(Supprimable));
            Notifier(nameof(Rattachable));
            Notifier(nameof(Archivee));
            Notifier(nameof(RefusDeSuppression));
            Notifier(nameof(ARefusDeSuppression));
            Notifier(nameof(LibelleCreation));
            Reevaluer();

            // Le schéma d'une catégorie ne se déduit pas de la liste : il faut le
            // demander. Sans sélection, on vide plutôt que de laisser à l'écran
            // les attributs de la catégorie précédente.
            Attributs.Clear();
            Notifier(nameof(PositionAttributs));

            if (value is not null)
            {
                ChargerAttributs.Execute(null);
            }
        }
    }

    public LigneDefinition? DefinitionChoisie
    {
        get => _definition;
        set
        {
            if (Definir(ref _definition, value))
            {
                Notifier(nameof(Rattachable));
                Reevaluer();
            }
        }
    }

    /// <summary>Filtre local sur le nom et le chemin.</summary>
    /// <remarks>
    /// LE CHEMIN RESTE AFFICHÉ SOUS CHAQUE LIGNE, ET C'EST CE QUI REND LE FILTRE
    ///    LISIBLE.
    ///
    /// Filtrer un arbre affiché en retrait produit des enfants sans leur parent :
    /// le retrait ne veut alors plus rien dire. Le chemin complet, lui, situe la
    /// ligne sans dépendre de ce qui est affiché au-dessus.
    /// </remarks>
    public string Recherche
    {
        get => _recherche;
        set { if (Definir(ref _recherche, value)) Filtrer(); }
    }

    public bool ADesSelection => _selection is not null;

    public bool Modifiable => _selection is not null && !string.IsNullOrWhiteSpace(_nom);

    /// <summary>Une catégorie archivée ne se publie ni ne se dépublie.</summary>
    /// <remarks>
    /// `Category.Publish()` et `Category.Unpublish()` refusent tous deux l'état
    /// `Archived`. Et aucun endpoint n'appelle `Category.Archive()` : cet état
    /// n'est atteignable que par une écriture directe en base ou par un import.
    /// </remarks>
    public bool Basculable => _selection is not null && !Archivee;

    public bool Archivee => _selection is { Statut: "Archived" };

    /// <summary>La suppression est refusée sur un nœud qui porte une branche.</summary>
    /// <remarks>
    /// ═══════════════════════════════════════════════════════════════════════
    /// C'EST L'ÉCRAN QUI POSE CETTE RÈGLE, PAS LE SERVEUR.
    ///
    /// `DeleteCategoryCommandHandler` supprime sans compter les enfants, et
    /// `ParentId` n'a pas de clé étrangère : la base laisse faire. Supprimer un
    /// nœud intermédiaire laisserait toute sa branche avec des parents qui
    /// n'existent plus.
    ///
    /// CE QUE CETTE GARDE NE COUVRE PAS : les produits. Aucune route ne permet
    /// de demander combien de fiches visent une catégorie, donc supprimer une
    /// FEUILLE reste un geste dont l'écran ne connaît pas la portée.
    /// ═══════════════════════════════════════════════════════════════════════
    /// </remarks>
    public bool Supprimable => _selection is { ADesEnfants: false };

    public string? RefusDeSuppression => _selection is { ADesEnfants: true, NombreEnfants: var n }
        ? $"Cette catégorie porte {n} sous-catégorie(s) : supprimez-les d'abord. "
          + "Le serveur ne le vérifie pas et laisserait la branche orpheline."
        : null;

    public bool ARefusDeSuppression => RefusDeSuppression is not null;

    public bool Rattachable => _selection is not null && _definition is not null;

    public string LibelleCreation => _selection is null
        ? "Nouvelle catégorie racine"
        : $"Nouvelle sous-catégorie de « {_selection.Nom} »";

    public string Nom
    {
        get => _nom;
        set { if (Definir(ref _nom, value)) { Notifier(nameof(Modifiable)); Reevaluer(); } }
    }

    public string Image
    {
        get => _image;
        set => Definir(ref _image, value);
    }

    /// <summary>Le schéma d'attributs, en JSON.</summary>
    /// <remarks>
    /// ═══════════════════════════════════════════════════════════════════════
    /// CE CHAMP N'EST PAS CE QUI VALIDE LES FICHES, ET IL FAUT LE SAVOIR.
    ///
    /// Deux mécanismes portent le même mot. La colonne `attribute_schema` est un
    /// `jsonb` stocké et rendu au contrat ; la validation, elle, lit la TABLE
    /// `category_attributes` — c'est `ListByCategoryAsync` que
    /// `ChangeProductStatusCommandHandler` appelle. Modifier ce JSON ne change
    /// donc rien à ce qu'un vendeur doit renseigner : c'est le bloc « attributs
    /// exigés » plus bas qui décide.
    ///
    /// LA FORME EST VÉRIFIÉE ICI PARCE QUE PERSONNE NE LA VÉRIFIE AVANT LA BASE.
    ///
    /// Ni `CreateCategoryCommandValidator` ni `UpdateCategoryCommandValidator` ne
    /// regardent ce champ, et la colonne est de type `jsonb` : un JSON mal formé
    /// est refusé par PostgreSQL, ce qui remonte en 500. L'administrateur verrait
    /// « La passerelle a répondu 500 » sur une accolade oubliée.
    /// ═══════════════════════════════════════════════════════════════════════
    /// </remarks>
    public string Schema
    {
        get => _schema;
        set => Definir(ref _schema, value);
    }

    public bool Requis
    {
        get => _requis;
        set => Definir(ref _requis, value);
    }

    public bool Variante
    {
        get => _variante;
        set => Definir(ref _variante, value);
    }

    public string Ordre
    {
        get => _ordre;
        set => Definir(ref _ordre, value);
    }

    /// <summary>Propager la bascule à toute la descendance.</summary>
    public bool Cascade
    {
        get => _cascade;
        set => Definir(ref _cascade, value);
    }

    /// <summary>Les incohérences de chemin détectées dans l'arbre.</summary>
    public string? Incoherences
    {
        get => _incoherences;
        private set { if (Definir(ref _incoherences, value)) Notifier(nameof(AIncoherences)); }
    }

    public bool AIncoherences => !string.IsNullOrEmpty(_incoherences);

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

    public string Position => _arbre.Count == Categories.Count
        ? $"{_arbre.Count} catégorie(s)"
        : $"{Categories.Count} sur {_arbre.Count} catégorie(s)";

    public string PositionAttributs => Attributs.Count == 0
        ? "Aucun attribut exigé"
        : $"{Attributs.Count} attribut(s) exigé(s)";

    public CommandeAsync Rafraichir { get; }

    public CommandeAsync Creer { get; }

    public CommandeAsync Enregistrer { get; }

    public CommandeAsync Publier { get; }

    public CommandeAsync Depublier { get; }

    public CommandeAsync Supprimer { get; }

    public CommandeAsync Rattacher { get; }

    public CommandeAsync<LigneAttributCategorie> Detacher { get; }

    public CommandeAsync CreerDefinition { get; }

    /// <summary>Chargement du schéma de la sélection, déclenché par la sélection.</summary>
    /// <remarks>
    /// ═══════════════════════════════════════════════════════════════════════
    /// UNE COMMANDE, PARCE QUE LE `set` D'UNE PROPRIÉTÉ NE PEUT PAS ATTENDRE.
    ///
    /// Sélectionner une catégorie doit déclencher un appel réseau. Le `set` est
    /// synchrone : il faut donc bien lancer la tâche sans l'attendre, et c'est
    /// exactement ce que fait `ICommand.Execute` — déjà utilisé partout ailleurs
    /// dans cette application, y compris pour le premier chargement.
    ///
    /// CE QUE CELA NE PROTÈGE PAS : `CommandeAsync.Execute` est un `async void`.
    /// Une exception levée dans l'action y serait non observée. Elle ne l'est pas
    /// ici parce que `EnvoyerAsync` attrape `HttpRequestException` et
    /// `TaskCanceledException` et rend un `Resultat` en échec — la garantie vient
    /// du client HTTP, pas de la commande.
    ///
    /// LA COMMANDE EST CRÉÉE UNE FOIS, DANS LE CONSTRUCTEUR.
    ///
    /// Une propriété calculée `=> new(...)` en fabriquerait une à chaque accès :
    /// celle sur laquelle on s'abonne ne serait jamais celle que l'on exécute.
    /// ═══════════════════════════════════════════════════════════════════════
    /// </remarks>
    public CommandeAsync ChargerAttributs { get; }

    private void Reevaluer()
    {
        Creer.Reevaluer();
        Enregistrer.Reevaluer();
        Publier.Reevaluer();
        Depublier.Reevaluer();
        Supprimer.Reevaluer();
        Rattacher.Reevaluer();
        Detacher.Reevaluer();
        CreerDefinition.Reevaluer();
    }

    private async Task ChargerAsync()
    {
        EnCours = true;
        Erreur = null;

        try
        {
            var choisie = _selection?.Id;

            var categories = await _api.ListerCategoriesAsync();
            var definitions = await _api.ListerDefinitionsAttributsAsync();

            var soucis = new List<string>();

            Selection = null;
            _arbre.Clear();

            if (categories.Reussi && categories.Valeur is not null)
            {
                Batir(categories.Valeur);
            }
            else
            {
                soucis.Add(categories.Message ?? "Arbre indisponible.");
            }

            Filtrer();

            var choisieDefinition = _definition?.Id;
            DefinitionChoisie = null;
            Definitions.Clear();

            if (definitions.Reussi && definitions.Valeur is not null)
            {
                foreach (var definition in definitions.Valeur.OrderBy(d => d.Code, StringComparer.Ordinal))
                {
                    Definitions.Add(new LigneDefinition(definition));
                }
            }
            else
            {
                soucis.Add(definitions.Message ?? "Référentiel d'attributs indisponible.");
            }

            DefinitionChoisie = Definitions.FirstOrDefault(d => d.Id == choisieDefinition);
            Selection = Categories.FirstOrDefault(c => c.Id == choisie);

            Erreur = soucis.Count == 0 ? null : string.Join(" ", soucis);

            Notifier(nameof(Position));
        }
        finally
        {
            EnCours = false;
        }
    }

    /// <summary>
    /// Construit les lignes, calcule le retrait et repère les branches coupées.
    /// </summary>
    /// <remarks>
    /// ═══════════════════════════════════════════════════════════════════════
    /// LE TRI SE FAIT SUR LE CHEMIN, ET C'EST CE QUI DONNE L'ORDRE DE L'ARBRE.
    ///
    /// Trier « /animaux », « /animaux/chats », « /animaux/chiens », « /maison »
    /// par ordre alphabétique de chemin produit exactement un parcours en
    /// profondeur. Aucun tri récursif n'est nécessaire — et surtout, une ligne
    /// dont le chemin est devenu faux apparaît là où son chemin la place, ce qui
    /// est précisément ce que l'on veut voir.
    ///
    /// LA DÉTECTION DE BRANCHE COUPÉE EST LE VRAI APPORT DE CETTE MÉTHODE.
    ///
    /// Renommer une catégorie recalcule SON chemin et pas celui de ses enfants
    /// (`Category.Update`). Comme `ListDescendantsAsync` cherche
    /// `Path.StartsWith(parent + "/")`, la branche cesse silencieusement de
    /// suivre : une publication en cascade rend « 1 » et ne touche plus rien.
    /// Rien côté serveur ne le signale ; l'écran compare donc chaque chemin à
    /// celui de son parent déclaré.
    /// ═══════════════════════════════════════════════════════════════════════
    /// </remarks>
    private void Batir(IReadOnlyList<CategorieAdmin> categories)
    {
        var parIdentifiant = categories.ToDictionary(c => c.Id);

        var enfants = categories
            .Where(c => c.ParentId is not null)
            .GroupBy(c => c.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var coupees = new List<string>();

        foreach (var categorie in categories.OrderBy(c => c.Path, StringComparer.Ordinal))
        {
            string? rupture = null;

            if (categorie.ParentId is { } parentId)
            {
                if (!parIdentifiant.TryGetValue(parentId, out var parent))
                {
                    rupture = "parent absent de l'arbre";
                }
                else if (!categorie.Path.StartsWith(parent.Path.TrimEnd('/') + "/", StringComparison.Ordinal))
                {
                    rupture = $"chemin hors de « {parent.Path} »";
                }
            }

            if (rupture is not null)
            {
                coupees.Add($"{categorie.Path} ({rupture})");
            }

            enfants.TryGetValue(categorie.Id, out var compte);
            _arbre.Add(new LigneCategorie(categorie, compte, rupture));
        }

        Incoherences = coupees.Count == 0
            ? null
            : $"{coupees.Count} catégorie(s) dont le chemin ne descend plus du parent déclaré : "
              + string.Join(" ; ", coupees.Take(3))
              + (coupees.Count > 3 ? " …" : string.Empty)
              + ". La publication en cascade ne les atteindra pas. Aucune route ne permet de "
              + "réécrire un chemin : la correction est à faire côté catalog-service.";
    }

    private void Filtrer()
    {
        var terme = _recherche.Trim();
        var choisie = _selection?.Id;

        Categories.Clear();

        foreach (var ligne in _arbre)
        {
            if (terme.Length == 0 || ligne.Correspond(terme))
            {
                Categories.Add(ligne);
            }
        }

        if (choisie is not null && Categories.All(c => c.Id != choisie))
        {
            Selection = null;
        }

        Notifier(nameof(Position));
    }

    private async Task ChargerAttributsAsync()
    {
        if (_selection is not { } categorie)
        {
            return;
        }

        var resultat = await _api.ListerAttributsDeCategorieAsync(categorie.Id);

        // Les attributs sont un complément de la fiche : leur indisponibilité ne
        // doit pas effacer l'erreur d'un geste précédent, ni la remplacer.
        if (!resultat.Reussi || resultat.Valeur is null)
        {
            Erreur = resultat.Message ?? "Schéma de la catégorie indisponible.";
            return;
        }

        Attributs.Clear();

        foreach (var attribut in resultat.Valeur.OrderBy(a => a.DisplayOrder).ThenBy(a => a.Code, StringComparer.Ordinal))
        {
            Attributs.Add(new LigneAttributCategorie(attribut));
        }

        Notifier(nameof(PositionAttributs));
    }

    private async Task CreerAsync()
    {
        Erreur = null;
        Confirmation = null;

        var parent = _selection;
        var nom = await _saisie.MotifAsync(parent is null
            ? "Nom de la nouvelle catégorie racine"
            : $"Nom de la sous-catégorie de « {parent.Nom} »");

        if (string.IsNullOrWhiteSpace(nom))
        {
            return;
        }

        EnCours = true;

        try
        {
            var resultat = await _api.CreerCategorieAsync(nom.Trim(), parent?.Id);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            // Une catégorie naît en `Draft` : elle n'est pas dans la vitrine tant
            // qu'on ne l'a pas publiée, et le message le dit plutôt que de laisser
            // croire que la création suffit.
            Confirmation = $"Catégorie « {nom.Trim()} » créée en brouillon. "
                           + "Elle n'apparaîtra dans la vitrine qu'une fois publiée.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    private async Task EnregistrerAsync()
    {
        if (_selection is not { } categorie || string.IsNullOrWhiteSpace(_nom))
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        var schema = string.IsNullOrWhiteSpace(_schema) ? "{}" : _schema.Trim();

        if (!EstUnJsonValide(schema))
        {
            Erreur = "Le schéma d'attributs n'est pas un JSON valide. La colonne est de type "
                     + "jsonb : la base le refuserait, et l'erreur remonterait en 500.";
            return;
        }

        var renomme = !string.Equals(categorie.Nom, _nom.Trim(), StringComparison.Ordinal);

        if (renomme && categorie.ADesEnfants)
        {
            // On ne bloque pas : renommer reste légitime. Mais l'administrateur doit
            // savoir AVANT d'envoyer que la branche va se détacher, et pourquoi.
            var accord = await _saisie.MotifAsync(
                $"Renommer « {categorie.Nom} » recalcule son chemin sans toucher à celui de ses "
                + $"{categorie.NombreEnfants} sous-catégorie(s) : la cascade ne les atteindra plus. "
                + "Tapez le motif pour confirmer");

            if (string.IsNullOrWhiteSpace(accord))
            {
                return;
            }
        }

        EnCours = true;

        try
        {
            var resultat = await _api.ModifierCategorieAsync(
                categorie.Id, _nom.Trim(),
                string.IsNullOrWhiteSpace(_image) ? null : _image.Trim(),
                schema);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = renomme
                ? "Catégorie enregistrée. Son chemin a été recalculé ; celui de ses "
                  + "descendants ne l'est pas."
                : "Catégorie enregistrée.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    private async Task BasculerAsync(bool publier)
    {
        if (_selection is not { } categorie)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;
        EnCours = true;

        try
        {
            var resultat = await _api.PublierCategorieAsync(categorie.Id, publier, _cascade);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            var verbe = publier ? "publiée(s)" : "dépubliée(s)";

            // Le compte peut être inférieur à la descendance : les catégories
            // archivées sont sautées. On rend le chiffre du serveur plutôt que le
            // nombre d'enfants connu de l'écran, qui ne dirait pas la même chose.
            Confirmation = _cascade
                ? $"{resultat.Valeur} catégorie(s) {verbe}. Les descendants archivés, s'il y en a, "
                  + "ont été sautés."
                : $"« {categorie.Nom} » {verbe}.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    private async Task SupprimerAsync()
    {
        if (_selection is not { ADesEnfants: false } categorie)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        var motif = await _saisie.MotifAsync(
            $"Motif de la suppression de « {categorie.Chemin} ». Aucune route ne permet de savoir "
            + "combien de fiches produit visent cette catégorie");

        if (string.IsNullOrWhiteSpace(motif))
        {
            return;
        }

        var mot = await _saisie.MotDePasseAsync($"Supprimer la catégorie {categorie.Nom}");

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
            var resultat = await _api.SupprimerCategorieAsync(categorie.Id);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = $"Catégorie « {categorie.Chemin} » supprimée.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    private async Task RattacherAsync()
    {
        if (_selection is not { } categorie || _definition is not { } definition)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        if (!int.TryParse(_ordre.Trim(), out var ordre))
        {
            Erreur = "L'ordre d'affichage doit être un nombre entier.";
            return;
        }

        // Rendre un attribut obligatoire ferme la porte à des soumissions dès
        // l'enregistrement. Le geste reste à un clic, mais il est nommé.
        if (_requis)
        {
            var accord = await _saisie.MotifAsync(
                $"« {definition.Nom} » deviendra OBLIGATOIRE dans « {categorie.Nom} » : toute "
                + "nouvelle soumission sans cet attribut sera refusée. Tapez le motif pour confirmer");

            if (string.IsNullOrWhiteSpace(accord))
            {
                return;
            }
        }

        EnCours = true;

        try
        {
            var resultat = await _api.RattacherAttributAsync(
                categorie.Id, definition.Id, _requis, _variante, ordre);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = $"« {definition.Nom} » rattaché à « {categorie.Nom} »"
                           + (_requis ? ", et exigé à la soumission." : ".");
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAttributsAsync();
    }

    private async Task DetacherAsync(LigneAttributCategorie attribut)
    {
        if (_selection is not { } categorie)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;
        EnCours = true;

        try
        {
            var resultat = await _api.DetacherAttributAsync(categorie.Id, attribut.Id);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = $"« {attribut.Nom} » retiré du schéma. Les fiches existantes "
                           + "conservent la valeur qu'elles portaient.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAttributsAsync();
    }

    private async Task CreerDefinitionAsync()
    {
        Erreur = null;
        Confirmation = null;

        // Trois saisies successives plutôt qu'un formulaire : la création d'une
        // définition est rare, et un panneau permanent occuperait l'écran pour un
        // geste que l'on fait quelques fois par an.
        var code = await _saisie.MotifAsync("Code de l'attribut, par exemple « couleur »");

        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var nom = await _saisie.MotifAsync($"Libellé affiché pour « {code.Trim()} »");

        if (string.IsNullOrWhiteSpace(nom))
        {
            return;
        }

        var type = await _saisie.MotifAsync(
            "Type : TEXT, TEXTAREA, INTEGER, DECIMAL, BOOLEAN, SELECT, MULTI_SELECT, COLOR ou DATE");

        if (string.IsNullOrWhiteSpace(type))
        {
            return;
        }

        var normalise = type.Trim().ToUpperInvariant();

        // La liste fermée est recopiée du serveur : `Enum.TryParse` sur
        // `AttributeValueType`, soulignés retirés. Vérifier ici évite un
        // aller-retour dont le message serait la seule explication.
        if (!TypesDAttribut.Contains(normalise))
        {
            Erreur = $"Type inconnu : « {type.Trim()} ». Attendu : {string.Join(", ", TypesDAttribut)}.";
            return;
        }

        EnCours = true;

        try
        {
            var resultat = await _api.CreerDefinitionAttributAsync(
                code.Trim(), nom.Trim(), normalise, null, []);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = normalise is "SELECT" or "MULTI_SELECT"
                ? $"Attribut « {nom.Trim()} » créé SANS option. Un {normalise} sans option ne "
                  + "propose rien au vendeur : les options se posent côté serveur, aucune route "
                  + "de modification n'existe."
                : $"Attribut « {nom.Trim()} » créé. Rattachez-le à une catégorie pour qu'il serve.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }

    /// <summary>Les neuf types acceptés, recopiés du message d'erreur du service.</summary>
    private static readonly string[] TypesDAttribut =
    [
        "TEXT", "TEXTAREA", "INTEGER", "DECIMAL", "BOOLEAN", "SELECT", "MULTI_SELECT", "COLOR", "DATE",
    ];

    private static bool EstUnJsonValide(string texte)
    {
        try
        {
            using var _ = JsonDocument.Parse(texte);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>Une catégorie, telle que l'arbre l'affiche.</summary>
public sealed class LigneCategorie
{
    public LigneCategorie(CategorieAdmin categorie, int nombreEnfants, string? rupture)
    {
        Id = categorie.Id;
        ParentId = categorie.ParentId;
        Nom = categorie.Name;
        Slug = categorie.Slug;
        Chemin = categorie.Path;
        Statut = categorie.Status;
        Image = categorie.ImageUrl ?? string.Empty;
        Schema = categorie.AttributeSchema ?? "{}";
        NombreEnfants = nombreEnfants;
        Rupture = rupture;

        Etat = categorie.Status switch
        {
            "Published" => "publiée",
            "Draft" => "brouillon",
            "Archived" => "archivée",
            _ => categorie.Status.ToLowerInvariant(),
        };

        // Le retrait vient du chemin, pas d'un parcours : « /a/b/c » a deux
        // séparateurs de plus que la racine, donc deux niveaux.
        var profondeur = Math.Max(0, categorie.Path.Trim('/').Count(c => c == '/'));
        Retrait = profondeur * 16d;
    }

    public Guid Id { get; }

    public Guid? ParentId { get; }

    public string Nom { get; }

    public string Slug { get; }

    public string Chemin { get; }

    public string Statut { get; }

    public string Etat { get; }

    public string Image { get; }

    public string Schema { get; }

    public int NombreEnfants { get; }

    public bool ADesEnfants => NombreEnfants > 0;

    /// <summary>Non nul quand le chemin ne descend plus du parent déclaré.</summary>
    public string? Rupture { get; }

    public bool ACoupure => Rupture is not null;

    public double Retrait { get; }

    public bool EstPubliee => Statut == "Published";

    public bool Correspond(string terme)
        => Nom.Contains(terme, StringComparison.CurrentCultureIgnoreCase)
           || Chemin.Contains(terme, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Un attribut exigé par la catégorie sélectionnée.</summary>
public sealed class LigneAttributCategorie
{
    public LigneAttributCategorie(AttributCategorie attribut)
    {
        Id = attribut.AttributeDefinitionId;
        Code = attribut.Code;
        Nom = attribut.Name;
        Type = attribut.Type;
        Requis = attribut.Required;
        Variante = attribut.Variant;

        var details = new List<string> { attribut.Type.ToLowerInvariant() };

        if (!string.IsNullOrWhiteSpace(attribut.Unit))
        {
            details.Add(attribut.Unit);
        }

        details.Add($"ordre {attribut.DisplayOrder}");

        Details = string.Join(" · ", details);
    }

    public Guid Id { get; }

    public string Code { get; }

    public string Nom { get; }

    public string Type { get; }

    public bool Requis { get; }

    /// <summary>
    /// L'attribut distingue les variantes d'une même fiche (taille, couleur).
    /// </summary>
    public bool Variante { get; }

    public string Details { get; }
}

/// <summary>Une définition d'attribut, telle que la liste déroulante l'affiche.</summary>
public sealed class LigneDefinition
{
    public LigneDefinition(DefinitionAttribut definition)
    {
        Id = definition.Id;
        Code = definition.Code;
        Nom = definition.Name;
        Type = definition.Type;

        var options = definition.Options ?? [];

        Libelle = options.Count == 0
            ? $"{definition.Code} — {definition.Name} ({definition.Type.ToLowerInvariant()})"
            : $"{definition.Code} — {definition.Name} ({definition.Type.ToLowerInvariant()}, "
              + $"{options.Count} option(s))";
    }

    public Guid Id { get; }

    public string Code { get; }

    public string Nom { get; }

    public string Type { get; }

    public string Libelle { get; }
}
