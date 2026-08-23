using Avalonia.Media;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Ce que la fenêtre affiche : la connexion, puis le back-office.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// UN SEUL ENDROIT DÉCIDE DE L'ÉCRAN COURANT.
///
/// `Deconnecter` ramène TOUJOURS à la connexion, quel que soit l'écran d'où l'on
/// vient — la règle « on ne voit le back-office que session ouverte » vit donc à
/// un seul endroit plutôt que dans chaque transition.
///
/// LA CARTE DES SECTIONS EST BÂTIE SUR L'INVENTAIRE RÉEL DES ROUTES.
///
/// Les six groupes reprennent l'organisation de la console précédente. Ce qui
/// change est l'état : chaque section déclare si son écran EXISTE, s'il RESTE À
/// ÉCRIRE sur un amont déjà relayé, ou si AUCUN SERVICE ne rend cette donnée.
/// Les trois ne coûtent pas le même travail, et un panneau qui les afficherait
/// pareillement rendrait tout arbitrage faux.
///
/// DEUX SECTIONS N'ÉTAIENT PAS DANS L'ANCIENNE CONSOLE : « Livreurs » et
/// « Tarification ». Leurs routes existent pourtant depuis les vagues 5 et 8 —
/// cinq chacune, relayées. C'était un manque de la console, pas du serveur.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class FenetrePrincipaleViewModel : ViewModelBase
{
    /// <summary>Largeur du panneau déployé, puis réduit.</summary>
    /// <remarks>
    /// 64 EST LA LARGEUR D'UNE CIBLE DE CLIC, PAS UNE VALEUR ESTHÉTIQUE.
    ///
    /// L'icône fait 20 unités ; le reste est la marge qui rend l'entrée
    /// cliquable sans viser. Descendre à 48 rendrait le panneau plus fin et les
    /// clics plus difficiles — sur un poste où l'on navigue toute la journée,
    /// c'est le mauvais échange.
    /// </remarks>
    private const double LargeurDeployee = 268;
    private const double LargeurReduite = 64;

    private readonly ClientApiAdmin _api;
    private readonly SessionAdmin _session;
    private readonly IDemandeurDeSaisie _saisie;

    private ViewModelBase _contenu;
    private SectionAdmin? _active;
    private bool _reduit;

    public FenetrePrincipaleViewModel(
        ClientApiAdmin api, SessionAdmin session, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _session = session;
        _saisie = saisie;
        _contenu = new ConnexionViewModel(api, Ouvrir);

        Groupes = Batir();

        Aller = new CommandeAsync<SectionAdmin>(section =>
        {
            Naviguer(section);
            return Task.CompletedTask;
        });

        Basculer = new CommandeAsync(() =>
        {
            Reduit = !Reduit;
            return Task.CompletedTask;
        });
    }

    /// <summary>Les six groupes du panneau, dans l'ordre.</summary>
    public IReadOnlyList<GroupeAdmin> Groupes { get; }

    public ViewModelBase Contenu
    {
        get => _contenu;
        private set => Definir(ref _contenu, value);
    }

    /// <summary>Le panneau est-il réduit aux icônes ?</summary>
    /// <remarks>
    /// L'ÉTAT VIT EN MÉMOIRE, ET N'EST PAS CONSERVÉ ENTRE DEUX LANCEMENTS.
    ///
    /// Le conserver demanderait un fichier de préférences — un chemin par
    /// système, une écriture, un format à faire évoluer — pour retenir un
    /// booléen, dans une application qui n'écrit rien sur le disque par principe
    /// (voir `SessionAdmin`). Un clic par jour contre le premier pas dans
    /// l'autre sens.
    /// </remarks>
    public bool Reduit
    {
        get => _reduit;
        private set
        {
            if (Definir(ref _reduit, value))
            {
                Notifier(nameof(LargeurPanneau));
                Notifier(nameof(Deploye));
                Notifier(nameof(IconeBascule));
                Notifier(nameof(InfoBulleBascule));
            }
        }
    }

    public bool Deploye => !_reduit;

    public double LargeurPanneau => _reduit ? LargeurReduite : LargeurDeployee;

    /// <summary>Le chevron de la bascule, orienté selon l'état.</summary>
    /// <remarks>
    /// DEUX TRACÉS PLUTÔT QU'UNE ROTATION : lier un angle à un booléen
    /// demanderait un convertisseur — une classe et un enregistrement de
    /// ressource pour retourner une flèche. Le second tracé est le miroir du
    /// premier et tient sur une ligne.
    /// </remarks>
    public Geometry IconeBascule => _reduit ? Icones.ChevronDroit : Icones.ChevronGauche;

    /// <summary>Ce que la bascule fera, et non l'état où elle se trouve.</summary>
    public string InfoBulleBascule => _reduit ? "Déployer le panneau" : "Réduire le panneau";

    public Geometry IconeMarque => Icones.Marque;

    public Geometry IconeSortie => Icones.Sortie;

    public string Administrateur => _session.Nom;

    public string Courriel => _session.Courriel;

    /// <summary>Première lettre du nom, affichée quand le panneau est réduit.</summary>
    public string Initiale => string.IsNullOrWhiteSpace(_session.Nom)
        ? "?"
        : _session.Nom.Trim()[..1].ToUpperInvariant();

    public bool SessionOuverte => _session.EstOuverte;

    public CommandeAsync<SectionAdmin> Aller { get; }

    public CommandeAsync Basculer { get; }

    /// <summary>Ferme la session et revient à l'écran de connexion.</summary>
    public void Deconnecter()
    {
        _api.Deconnecter();

        if (_active is not null)
        {
            _active.Active = false;
            _active = null;
        }

        Contenu = new ConnexionViewModel(_api, Ouvrir);
        Rafraichir();
    }

    private void Ouvrir()
    {
        Naviguer(Groupes[0].Sections[0]);
        Rafraichir();
    }

    /// <remarks>
    /// L'ANCIENNE SECTION EST ÉTEINTE AVANT QUE LA NOUVELLE NE S'ALLUME.
    ///
    /// Sans cela, deux entrées resteraient marquées actives — l'état vit sur la
    /// section, pas dans une liste qui n'en tolère qu'une. C'est le prix de
    /// s'être passé d'une `ListBox`, et il se paie ici, une fois.
    /// </remarks>
    private void Naviguer(SectionAdmin section)
    {
        if (ReferenceEquals(section, _active))
        {
            return;
        }

        if (_active is not null)
        {
            _active.Active = false;
        }

        _active = section;
        _active.Active = true;

        Contenu = section.Construire();
    }

    private void Rafraichir()
    {
        Notifier(nameof(Administrateur));
        Notifier(nameof(Courriel));
        Notifier(nameof(Initiale));
        Notifier(nameof(SessionOuverte));
    }

    // ─────────────────────────────────────────────────────── la carte

    private SectionAdmin Prete(string cle, string libelle, Geometry icone, Func<ViewModelBase> fabrique)
        => new(cle, libelle, icone, EtatSection.Pret, fabrique);

    /// <summary>Une section dont l'amont est relayé et l'écran reste à écrire.</summary>
    private SectionAdmin AEcrire(string cle, string libelle, Geometry icone, string routes)
    {
        SectionAdmin section = null!;
        section = new SectionAdmin(cle, libelle, icone, EtatSection.AEcrire,
            () => new PageAVenirViewModel(section, routes));
        return section;
    }

    /// <summary>Une section qu'aucun service ne rend.</summary>
    private SectionAdmin SansAmont(string cle, string libelle, Geometry icone, string explication)
    {
        SectionAdmin section = null!;
        section = new SectionAdmin(cle, libelle, icone, EtatSection.SansAmont,
            () => new PageAVenirViewModel(section, explication));
        return section;
    }

    private IReadOnlyList<GroupeAdmin> Batir() =>
    [
        new GroupeAdmin("VUE D'ENSEMBLE",
        [
            Prete("accueil", "Tableau de bord", Icones.TableauDeBord,
                () => new AccueilViewModel(_api)),
        ]),

        new GroupeAdmin("OPÉRATIONS",
        [
            Prete("vendeurs", "Vendeurs & KYB", Icones.Boutique,
                () => new VendeursViewModel(_api, _saisie)),

            Prete("commandes", "Commandes", Icones.Panier,
                () => new CommandesViewModel(_api, _saisie)),

            Prete("paiements", "Paiements", Icones.Carte,
                () => new PaiementsViewModel(_api, _saisie)),

            Prete("remboursements", "Remboursements", Icones.Retour,
                () => new RemboursementsViewModel(_api, _saisie)),

            Prete("retraits", "Retraits", Icones.Billet,
                () => new RetraitsViewModel(_api, _saisie)),

            Prete("livreurs", "Livreurs", Icones.Livreur,
                () => new LivreursViewModel(_api, _saisie)),

            Prete("tarification", "Tarification", Icones.Tarif,
                () => new TarificationViewModel(_api, _saisie)),
        ]),

        new GroupeAdmin("GESTION",
        [
            // ═════════════════════════════════════════════════════════════════
            // DÉBLOQUÉE : LA REQUÊTE EXISTAIT, LA ROUTE MANQUAIT.
            //
            // `ListUsersQuery` — recherche, statut, tri, pagination, comptage —
            // était écrite depuis le début sans qu'aucune route ne la monte.
            // `GET /api/identity/users` a été ouverte dans le même lot.
            // ═════════════════════════════════════════════════════════════════
            Prete("utilisateurs", "Utilisateurs", Icones.Utilisateurs,
                () => new UtilisateursViewModel(_api, _saisie)),

            Prete("roles", "Rôles", Icones.Cle,
                () => new RolesViewModel(_api, _saisie)),

            Prete("categories", "Catégories", Icones.Arborescence,
                () => new CategoriesViewModel(_api, _saisie)),

            Prete("marques", "Marques", Icones.Etiquette,
                () => new MarquesViewModel(_api, _saisie)),

            Prete("produits", "Produits", Icones.Colis,
                () => new ProduitsViewModel(_api, _saisie)),
        ]),

        new GroupeAdmin("FINANCE",
        [
            Prete("portefeuille", "Portefeuille", Icones.Portefeuille,
                () => new PortefeuilleViewModel(_api)),

            // ═════════════════════════════════════════════════════════════════
            // LA LISTE MANQUANTE A ÉTÉ ÉCRITE, PAS CONTOURNÉE.
            //
            // Les quatre écritures existaient ; la lecture ne se faisait que par
            // identifiant ou par vendeur, ce qui ne fait pas un écran
            // d'administration — on ne peut pas ouvrir la liste des factures
            // qu'on n'a pas encore identifiées.
            //
            // `ListInvoicesQuery` + `IInvoiceRepository.ListForAdminAsync` ont
            // été ajoutées, la route `GET /api/financial/invoices` montée avec
            // `.RequireAdmin()`, PUIS la passerelle ouverte — jamais l'inverse.
            //
            // CE QUE CET ÉCRAN NE COUVRE PAS : le détail des lignes. `Invoice`
            // possède ses `InvoiceLine`, le dépôt les charge, et
            // `InvoiceMapper.ToSummary` les laisse tomber ; `GetInvoiceQuery`
            // rend ce même résumé. Une ligne ajoutée n'est donc jamais relue,
            // seul le total bouge. L'écran le DIT. Corriger cela élargirait un
            // contrat que les clients vendeur consomment déjà.
            // ═════════════════════════════════════════════════════════════════
            Prete("factures", "Factures", Icones.Facture,
                () => new FacturesViewModel(_api, _saisie)),

            // ═════════════════════════════════════════════════════════════════
            // ÉCRAN DE LECTURE, PAR DÉCISION D'ARCHITECTURE.
            //
            // La route `settlements` de la passerelle est `GET, HEAD, OPTIONS`.
            // Les quatre écritures — lancer, annuler, marquer payé, marquer
            // échoué — vivent dans un `MapAdminGroup` voisin, non relayé : elles
            // ne sont atteignables que depuis le réseau interne. Ce n'est pas un
            // manque à combler ici ; l'écran les nomme.
            // ═════════════════════════════════════════════════════════════════
            Prete("settlement", "Reversements", Icones.Balance,
                () => new ReglementViewModel(_api)),

            // ═════════════════════════════════════════════════════════════════
            // LA GARDE D'ABORD, LA PASSERELLE ENSUITE. JAMAIS L'INVERSE.
            //
            // Cet écran était bloqué par un mur documenté ici : billing-service
            // exposait les sept routes de commission, et `appsettings.json` de
            // la passerelle ne menait à aucune. Ajouter la ligne manquante
            // aurait suffi à afficher l'écran — et aurait rouvert une fuite.
            //
            // `commissions.MapGet("/", ListCommissionRulesAsync)` n'avait PAS de
            // `.RequireAdmin()`, contrairement aux cinq écritures voisines, et
            // la liste porte les règles de portée `Seller` : le taux négocié,
            // vendeur par vendeur. Exactement la donnée que
            // `ComputeCommissionAsync` avait cessé d'exposer.
            //
            // Les deux corrections ont été faites DANS CET ORDRE : la garde sur
            // la route de liste, puis l'entrée de passerelle. Relayer avant de
            // garder aurait publié la grille négociée de la plateforme pendant
            // toute la durée d'un déploiement.
            //
            // CE QUE CET ÉCRAN NE COUVRE PAS : le taux par défaut appliqué
            // quand aucune règle ne correspond. Il vit dans la configuration du
            // service financier, pas dans la grille — l'aperçu le signale.
            // ═════════════════════════════════════════════════════════════════
            Prete("commissions", "Commissions", Icones.Balance,
                () => new CommissionsViewModel(_api, _saisie)),
        ]),

        new GroupeAdmin("PLATEFORME",
        [
            SansAmont("marketing", "Marketing", Icones.Megaphone,
                "promotion-service existe, mais TOUTE sa surface est vendeur :\n"
                + "  /api/v1/merchant/promotions   (le vendeur gère les siennes)\n"
                + "  /api/v1/promotions            (validation d'un code)\n\n"
                + "Aucun groupe d'administration. Une campagne PLATEFORME —\n"
                + "budget, ciblage, arbitrage entre vendeurs — n'a ni agrégat\n"
                + "ni route."),

            Prete("stock", "Stock", Icones.Entrepot,
                () => new StockViewModel(_api)),

            SansAmont("taxes", "Taxes", Icones.Taxe,
                "Aucun service, aucune route, aucun agrégat.\n"
                + "Le mot n'apparaît nulle part dans les services."),
        ]),

        new GroupeAdmin("CONTENU & SUPERVISION",
        [
            SansAmont("bannieres", "Bannières", Icones.Banniere,
                "Aucun service de contenu éditorial.\n\n"
                + "L'application cliente déclare le même manque sous le nom\n"
                + "`content` dans son inventaire `not_migrated.dart`."),

            // ═════════════════════════════════════════════════════════════════
            // L'ÉCRITURE EXISTAIT DEPUIS L'ORIGINE ; PERSONNE NE POUVAIT LA RELIRE.
            //
            // `POST /api/engagement/recommendations` persiste réellement, sur le
            // groupe admin. Les trois lectures du service sont toutes ADRESSÉES
            // — par produit, par utilisateur, ou « les miennes » : aucune ne
            // répond à « qu'est-ce qui est mis en avant en ce moment ». Même
            // situation que les avis avant la file de modération.
            //
            // `ListRecommendationsQuery` + `IRecommendationRepository.ListAsync`
            // ont été écrites, et montées sur le groupe ADMIN — cette page dit
            // quels produits la plateforme pousse et sur les fiches de qui,
            // c'est-à-dire exactement la donnée que la garde d'écriture protège.
            // Aucune route de passerelle à ajouter : `/api/recommendations` est
            // relayée depuis un lot antérieur et couvre tous les verbes.
            //
            // CE QUE CET ÉCRAN NE COUVRE PAS. Trois choses, et elles sont dites
            // sur place :
            //   — rien ne SUPPRIME une recommandation, le dépôt n'expose
            //     qu'`AddAsync` et deux lectures ;
            //   — rien ne distingue une ligne écrite à la main d'une ligne
            //     calculée : un recalcul remplacera l'une comme l'autre ;
            //   — les produits ne sont que des identifiants, le service n'ayant
            //     aucun accès au catalogue.
            // ═════════════════════════════════════════════════════════════════
            Prete("recommandations", "Recommandations", Icones.Etincelles,
                () => new RecommandationsViewModel(_api, _saisie)),

            // ═════════════════════════════════════════════════════════════════
            // DEUX ENTRÉES, PARCE QUE CE SONT DEUX MÉTIERS.
            //
            // « Modération » arbitre des RESTAURANTS — un dossier partenaire, une
            // décision commerciale. « Modération des avis » arbitre du CONTENU,
            // et chaque geste y déplace la note affichée d'un produit et d'une
            // boutique. Les fondre en un seul écran mêlerait deux files qui ne se
            // traitent ni au même rythme ni par les mêmes personnes.
            // ═════════════════════════════════════════════════════════════════
            Prete("moderation", "Modération", Icones.Bouclier,
                () => new ModerationViewModel(_api, _saisie)),

            Prete("moderation-avis", "Modération des avis", Icones.Etoile,
                () => new ModerationAvisViewModel(_api, _saisie)),

            SansAmont("notifications", "Notifications", Icones.Cloche,
                "/api/notifications est relayé, mais sa surface est celle du\n"
                + "DESTINATAIRE : lire et marquer ses propres notifications.\n\n"
                + "Aucun envoi de masse, aucune vue d'exploitation, aucun\n"
                + "gabarit administrable."),

            SansAmont("fraude", "Fraude", Icones.Empreinte,
                "Aucun service, aucun score, aucune règle.\n"
                + "Le mot n'apparaît que dans un commentaire de merchant-service."),

            SansAmont("outbox", "Outbox", Icones.Enveloppe,
                "`outbox_messages` est une table INTERNE à chaque service.\n"
                + "Aucune route ne l'expose, et c'est délibéré : la lire de\n"
                + "l'extérieur donnerait accès aux charges utiles d'événements,\n"
                + "qui portent des données personnelles.\n\n"
                + "Un écran d'exploitation demanderait une projection dédiée,\n"
                + "pas un accès direct."),

            SansAmont("analytics", "Analytics", Icones.Histogramme,
                "Aucune agrégation côté serveur. La série temporelle des ventes\n"
                + "était calculée par le BFF du monolithe ; ni service HBA ni\n"
                + "route BFF ne la rend.\n\n"
                + "Le même manque bloque l'écran `analytics` de l'application\n"
                + "vendeur."),

            SansAmont("monitoring", "Monitoring", Icones.Pouls,
                "Prometheus et Grafana tournent déjà —\n"
                + "voir infra/docker/compose.monitoring.yml.\n\n"
                + "Les recopier dans cette application n'apporterait rien :\n"
                + "un lien vers Grafana est la bonne réponse, pas un écran."),
        ]),
    ];
}
