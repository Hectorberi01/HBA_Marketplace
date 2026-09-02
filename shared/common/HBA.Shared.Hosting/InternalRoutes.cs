namespace HBA.Shared.Hosting;

/// <summary>
/// Protection des appels de service à service.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE FICHIER A PERDU SES ROUTES REST, ET C'EST VOULU.
///
/// Il portait `RequireInternalCaller()`, un filtre appliqué à huit routes
/// `/internal/*`. Ces routes ont été remplacées par du gRPC : le transport
/// synchrone entre services passe désormais par le port dédié, avec
/// <see cref="Grpc.InternalCallServerInterceptor"/> à la place du filtre.
///
/// Ne subsistent ici que les éléments communs aux deux mondes — le nom de la
/// clé et sa comparaison — parce qu'ils décrivent une politique, pas un
/// transport, et que le monolithe les utilisera aussi pendant l'étranglement.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class InternalRoutes
{
    /// <summary>En-tête HTTP portant le secret partagé.</summary>
    public const string HeaderName = "X-Internal-Key";

    /// <summary>
    /// La même clé, en métadonnée gRPC.
    /// </summary>
    /// <remarks>
    /// MINUSCULES OBLIGATOIRES. NE PAS « CORRIGER » LA CASSE.
    ///
    /// gRPC impose des clés de métadonnées en minuscules et lève une exception
    /// à l'exécution sinon — au premier appel réel, donc ni à la compilation ni
    /// au démarrage. Écrire `X-Internal-Key` ici passerait toutes les revues et
    /// casserait en production.
    /// </remarks>
    public const string MetadataKey = "x-internal-key";

    /// <summary>
    /// Comparaison à temps constant du secret présenté.
    /// </summary>
    /// <remarks>
    /// `==` sur des chaînes s'arrête au premier caractère différent : le temps de
    /// réponse révèle alors combien de caractères initiaux sont corrects, ce qui
    /// permet de reconstituer le secret caractère par caractère. L'attaque est
    /// lente mais parfaitement praticable sur un réseau local.
    ///
    /// `FixedTimeEquals` exige des longueurs égales. La comparaison de longueur
    /// ci-dessous fuit donc la TAILLE du secret, pas son contenu — information
    /// sans valeur, contrairement au préfixe correct que révélerait un `==`.
    /// </remarks>
    public static bool SecretsMatch(string? presented, string expected)
    {
        if (string.IsNullOrEmpty(presented))
        {
            return false;
        }

        var a = System.Text.Encoding.UTF8.GetBytes(presented);
        var b = System.Text.Encoding.UTF8.GetBytes(expected);

        return a.Length == b.Length
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }
}

/// <summary>Secret partagé des appels entre services.</summary>
public sealed class InternalCallOptions
{
    public const string SectionName = "Internal";

    /// <summary>
    /// JAMAIS DANS UN appsettings VERSIONNÉ — uniquement `Internal__ApiKey`.
    ///
    /// Cette clé ouvre l'API interne de TOUS les services : elle n'authentifie
    /// pas un service en particulier, elle atteste seulement l'appartenance au
    /// réseau de confiance. Suffisant pour treize services maîtrisés ; à
    /// remplacer par mTLS ou un jeton d'identité de charge de travail avant
    /// qu'un tiers n'entre sur ce réseau.
    ///
    /// CE PARAGRAPHE DÉCRIVAIT UNE DETTE ; ELLE EST DEPUIS EN PARTIE PAYÉE.
    ///
    /// `ServiceName`, `PrivateKey` et `PublicKeys` ci-dessous portent
    /// l'attestation d'identité par signature asymétrique — voir
    /// `Grpc/IdentiteInterne.cs`. Cette clé-ci demeure : elle reste la barrière
    /// d'APPARTENANCE au réseau, la moins chère à vérifier, celle qui écarte un
    /// appel qui n'a rien à faire là avant tout travail de cryptographie.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Nom de CET hôte, tel qu'il figure dans <see cref="Grpc.AutorisationsGrpc"/>.
    /// </summary>
    /// <remarks>
    /// LE DÉFAUT EST LE NOM DE L'ASSEMBLY D'ENTRÉE, ET C'EST DÉLIBÉRÉ.
    ///
    /// `SERVICE_NAME` était le candidat évident — il existe déjà et sert au
    /// champ `producer` de Kafka. Il a été écarté pour deux raisons vérifiables
    /// dans le dépôt :
    ///
    ///   1. LES VOCABULAIRES NE S'ACCORDENT PAS SUR LES NOMS. Le même hôte est
    ///      `seller-service` dans `docker-compose.dev.yml` et `merchant-service`
    ///      dans les déploiements Kubernetes ; de même cart/commerce,
    ///      restaurant/food, payment/financial, review/engagement. Une identité
    ///      d'autorisation qui change selon le fichier de déploiement n'est pas
    ///      une identité. (Ce paragraphe citait `infra/docker/compose.services.yml`
    ///      comme second vocabulaire ; ce dossier a été retiré du dépôt le
    ///      27 août, et c'est désormais k8s qui porte les noms par domaine —
    ///      l'écart, lui, n'a pas bougé.)
    ///
    ///   2. `SERVICE_NAME` N'EST PAS UNE IDENTITÉ FIABLE. Il est posé par le
    ///      compose de développement et par les déploiements, donc par le
    ///      fichier qui lance l'hôte et non par l'hôte lui-même : un service
    ///      démarré autrement — un test, un `dotnet run`, un conteneur lancé à
    ///      la main — n'en a aucun.
    ///
    /// Le nom d'assembly, lui, vient du code, est unique, et ne peut pas être
    /// oublié dans un fichier d'environnement. Le renommer sans mettre à jour la
    /// table d'autorisations couperait le service — c'est précisément ce que
    /// le contrôle `autorisations-grpc` refuse.
    /// </remarks>
    public string? ServiceName { get; init; }

    /// <summary>
    /// JAMAIS VERSIONNÉE — PKCS#8 en base64, propre à CET hôte.
    ///
    /// Engendrée par `scripts/generer-identites-internes.sh`. C'est le seul
    /// secret d'identité qu'un hôte détient : le compromettre permet d'usurper
    /// CE service, et aucun autre. C'est toute la différence avec
    /// <see cref="ApiKey"/>.
    /// </summary>
    public string? PrivateKey { get; init; }

    /// <summary>
    /// Registre `nom=base64;nom=base64` des clés PUBLIQUES, identique partout.
    /// </summary>
    /// <remarks>
    /// Aucune valeur pour un attaquant : ces clés ne servent qu'à vérifier. Elles
    /// pourraient être versionnées ; elles transitent par l'environnement pour
    /// qu'une rotation ne demande pas de reconstruire les images.
    /// </remarks>
    public string? PublicKeys { get; init; }

    /// <summary>
    /// Accepte une identité NON SIGNÉE. Développement uniquement.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CECI DÉSARME LA SIGNATURE. LE DÉMARRAGE ÉCHOUE HORS DÉVELOPPEMENT.
    ///
    /// `AddHbaGrpc` lève si ce drapeau est vrai et que l'environnement n'est pas
    /// `Development` — au démarrage, pas au premier appel. Un déploiement qui
    /// l'aurait hérité d'un fichier de développement ne part pas.
    ///
    /// POURQUOI IL EXISTE PLUTÔT QUE VINGT-QUATRE CLÉS DANS LE COMPOSE DE DÉV.
    ///
    /// `docker-compose.dev.yml` monte vingt-trois hôtes et mutualise leur
    /// configuration commune dans une seule ancre YAML (`x-dev-auth`). Une clé
    /// privée est par définition PROPRE À UN HÔTE : elle ne peut pas vivre dans
    /// l'ancre. Câbler l'identité en développement demanderait donc vingt-trois
    /// lignes de base64 recopiées à la main, plus un registre de trois kilo-octets
    /// sur une ligne — que personne ne relirait, et dont une erreur se
    /// manifesterait par un `Unauthenticated` sans cause lisible.
    ///
    /// CE QUI RESTE VÉRIFIÉ EN DÉVELOPPEMENT, ET C'EST L'ESSENTIEL :
    /// la table <see cref="Grpc.AutorisationsGrpc"/> s'applique quand même. Un
    /// appel non autorisé est refusé en développement comme en production — donc
    /// une autorisation manquante se voit à l'exécution des tests d'intégration,
    /// et non au premier déploiement.
    ///
    /// CE QUI N'EST PAS VÉRIFIÉ : la signature, donc l'authenticité du nom
    /// présenté. En développement, n'importe quel processus du réseau Docker peut
    /// se dire n'importe qui. C'est le même degré de confiance que la clé
    /// interne de développement écrite en clair dans le même fichier.
    ///
    /// Le précédent est dans le dépôt : `AesGcmSecretProtector` embarque une clé
    /// de développement et REFUSE de démarrer sans clé fournie en production.
    /// Même forme, même garde.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public bool IdentitesNonSignees { get; init; }
}
