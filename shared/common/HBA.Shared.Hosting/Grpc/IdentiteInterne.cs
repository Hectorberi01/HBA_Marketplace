using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace HBA.Shared.Hosting.Grpc;

/// <summary>
/// Attestation d'identité de l'appelant sur un appel gRPC interne.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA CLÉ PARTAGÉE N'ATTESTE PAS QUI APPELLE — ELLE ATTESTE QU'ON EST DEDANS.
///
/// `Internal:ApiKey` est UNE seule chaîne, la MÊME pour les dix-neuf hôtes. Un
/// service compromis — n'importe lequel — la lit dans son environnement et peut
/// dès lors appeler N'IMPORTE QUEL RPC de N'IMPORTE QUEL service en se
/// présentant comme n'importe qui. Ce n'est pas théorique : la surface atteinte
/// comprend `GetSellerPayout` (le numéro Mobile Money d'un vendeur, énumérable
/// par identifiant), `RefundPayment`, `ReleaseReservation` et `CancelDelivery`.
///
/// POURQUOI UNE CLÉ PAR SERVICE NE SUFFIT PAS.
///
/// C'était la correction évidente. Elle échoue sur un point : avec un secret
/// SYMÉTRIQUE, celui qui VÉRIFIE doit connaître le secret de celui qui SIGNE.
/// financial-service, pour vérifier order-service, détiendrait la clé
/// d'order-service — donc compromettre financial-service rendrait toutes les
/// clés qu'il vérifie. On aurait déplacé le problème, pas fermé.
///
/// D'où une signature ASYMÉTRIQUE. Chaque hôte détient SA clé privée et
/// seulement elle ; le registre des clés PUBLIQUES est le même partout et n'a
/// aucune valeur pour un attaquant. Compromettre un service donne le pouvoir
/// d'usurper ce service-là, et rien d'autre.
///
/// CE QUE CETTE MÉCANIQUE NE COUVRE PAS. À LIRE AVANT DE LA CROIRE SUFFISANTE.
///
///   • LE RÉSEAU EST EN CLAIR. Il n'y a pas de TLS entre services (voir
///     `GrpcHostExtensions` : deux ports justement parce qu'ALPN n'existe pas
///     sans TLS). Un attaquant EN COUPURE lit les charges utiles et peut
///     REJOUER un jeton observé pendant sa durée de vie. Le modèle de menace
///     fermé ici est « un service compromis », pas « un observateur du
///     réseau ». mTLS reste la réponse à celui-là, et il reste à faire.
///
///   • LA DURÉE DE VIE EST LA SEULE PROTECTION CONTRE LE REJEU. Trente
///     secondes, liées à la MÉTHODE appelée : un jeton capté ne vaut ni pour un
///     autre RPC, ni au-delà de la fenêtre. Un cache anti-rejeu côté serveur
///     fermerait la fenêtre — il exige un état partagé entre répliques, donc
///     Redis sur le chemin critique de chaque appel interne. Pas fait, assumé.
///
///   • L'AUTORISATION EST AILLEURS. Ce fichier répond à « qui appelle » ;
///     `AutorisationsGrpc` répond à « a-t-il le droit ». Les deux sont
///     nécessaires : une identité vérifiée sans liste d'autorisations laisse
///     user-service appeler `RefundPayment`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class IdentiteInterne
{
    /// <summary>Métadonnée gRPC portant l'attestation. MINUSCULES OBLIGATOIRES.</summary>
    /// <remarks>
    /// Même contrainte que <see cref="InternalRoutes.MetadataKey"/> : une clé de
    /// métadonnée contenant une majuscule est rejetée à l'EXÉCUTION, au premier
    /// appel réel — ni à la compilation, ni au démarrage.
    /// </remarks>
    public const string MetadataKey = "x-internal-identity";

    /// <summary>Durée de validité d'une attestation, à la frappe.</summary>
    public static readonly TimeSpan Duree = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Tolérance d'horloge acceptée à la vérification.
    /// </summary>
    /// <remarks>
    /// Les conteneurs partagent l'horloge de l'hôte Docker, donc l'écart réel est
    /// nul. Cette tolérance existe pour le jour où ils ne la partageront plus —
    /// une dérive de quelques secondes ferait alors échouer des appels
    /// parfaitement légitimes, avec un `Unauthenticated` que personne ne
    /// rattacherait à une horloge.
    /// </remarks>
    public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(30);

    private const string Version = "1";
    private const char Separateur = '|';

    // Le décodage d'une clé DER coûte quelques dizaines de microsecondes : refait
    // à chaque appel, il pèserait plus que la signature elle-même.
    private static readonly ConcurrentDictionary<string, ECDsa> _clesPubliques = new();
    private static readonly ConcurrentDictionary<string, ECDsa> _clesPrivees = new();

    /// <summary>
    /// Fabrique l'attestation qu'un appelant joint à son appel.
    /// </summary>
    /// <param name="appelant">Nom de l'hôte appelant, ex. `HBA.Order.Api`.</param>
    /// <param name="methode">Méthode gRPC complète, ex. `/hba.delivery.v1.DeliveryApi/LookupQuote`.</param>
    /// <param name="clePriveeBase64">PKCS#8 en base64 — la clé privée de CET hôte.</param>
    /// <param name="maintenant">Injecté pour les tests ; sinon <see cref="DateTimeOffset.UtcNow"/>.</param>
    public static string Signer(
        string appelant, string methode, string clePriveeBase64, DateTimeOffset? maintenant = null)
    {
        // LE SÉPARATEUR NE DOIT PAS APPARAÎTRE DANS LES CHAMPS.
        //
        // Sans ce refus, un appelant nommé `a|/x/y|9999999999|z` produirait une
        // charge utile que le vérificateur découperait autrement qu'elle n'a été
        // écrite — signature valide, contenu différent de l'intention. C'est la
        // faille classique des formats délimités non échappés. Aucun nom
        // d'assembly ni aucune méthode gRPC ne peut contenir `|` : le refus est
        // donc inatteignable en fonctionnement normal, et c'est exactement ce
        // qu'on veut d'un garde-fou.
        if (appelant.Contains(Separateur) || methode.Contains(Separateur))
        {
            throw new InvalidOperationException(
                "Un nom d'appelant ou de méthode ne peut pas contenir '|'.");
        }

        var expiration = (maintenant ?? DateTimeOffset.UtcNow).Add(Duree).ToUnixTimeSeconds();

        // `jti` n'est vérifié par personne aujourd'hui : il n'existe ni cache
        // anti-rejeu ni journal d'audit des appels internes. Il est signé quand
        // même pour que deux attestations émises la même seconde diffèrent — sans
        // quoi elles seraient identiques bit pour bit, et un cache anti-rejeu
        // ajouté plus tard rejetterait des appels légitimes.
        var jti = Convertir(RandomNumberGenerator.GetBytes(9));

        var charge = $"{Version}{Separateur}{appelant}{Separateur}{methode}{Separateur}{expiration}{Separateur}{jti}";
        var octets = Encoding.UTF8.GetBytes(charge);

        var cle = _clesPrivees.GetOrAdd(clePriveeBase64, valeur =>
        {
            var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(valeur), out _);
            return ecdsa;
        });

        var signature = cle.SignData(octets, HashAlgorithmName.SHA256);

        return $"{Convertir(octets)}.{Convertir(signature)}";
    }

    /// <summary>
    /// Vérifie une attestation et rend le nom de l'appelant, ou `null`.
    /// </summary>
    /// <remarks>
    /// REND `null` SANS DIRE POURQUOI, VOLONTAIREMENT.
    ///
    /// Distinguer « signature invalide » de « expiré » de « mauvaise méthode »
    /// dans la réponse apprendrait à un attaquant lequel de ses essais approche.
    /// L'appelant de cette méthode répond `Unauthenticated` sans détail. Le
    /// diagnostic d'exploitation passe par le journal du service appelé, pas par
    /// la réponse.
    /// </remarks>
    /// <param name="attestation">Contenu de la métadonnée <see cref="MetadataKey"/>.</param>
    /// <param name="methodeAttendue">La méthode réellement invoquée.</param>
    /// <param name="registre">Clés publiques connues, par nom d'hôte.</param>
    public static string? Verifier(
        string? attestation,
        string methodeAttendue,
        IReadOnlyDictionary<string, string> registre,
        DateTimeOffset? maintenant = null)
    {
        if (string.IsNullOrWhiteSpace(attestation))
        {
            return null;
        }

        var point = attestation.IndexOf('.');
        if (point <= 0 || point == attestation.Length - 1)
        {
            return null;
        }

        byte[] octets;
        byte[] signature;
        try
        {
            octets = Reconvertir(attestation[..point]);
            signature = Reconvertir(attestation[(point + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }

        var champs = Encoding.UTF8.GetString(octets).Split(Separateur);
        if (champs.Length != 5 || champs[0] != Version)
        {
            return null;
        }

        var appelant = champs[1];
        var methode = champs[2];

        // LA MÉTHODE EST VÉRIFIÉE AVANT LA SIGNATURE, ET C'EST SANS DANGER.
        //
        // Comparer un champ non encore authentifié n'apprend rien à l'attaquant :
        // c'est LUI qui l'a écrit. Le faire d'abord évite une vérification de
        // signature — quelques dizaines de microsecondes — sur une attestation
        // qui sera rejetée de toute façon.
        if (!string.Equals(methode, methodeAttendue, StringComparison.Ordinal))
        {
            return null;
        }

        if (!long.TryParse(champs[3], out var expiration))
        {
            return null;
        }

        var instant = maintenant ?? DateTimeOffset.UtcNow;

        if (DateTimeOffset.FromUnixTimeSeconds(expiration) < instant - Tolerance)
        {
            return null;
        }

        // UNE EXPIRATION TROP LOINTAINE EST REFUSÉE, MÊME SIGNÉE.
        //
        // La signature garantit qu'un hôte légitime a écrit cette date — pas
        // qu'il a eu raison de l'écrire. Un hôte dont le code serait modifié pour
        // frapper des attestations d'un an fabriquerait des laissez-passer
        // permanents, parfaitement valides. Le plafond est ici parce que le
        // VÉRIFICATEUR est le seul à ne pas dépendre du bon comportement du
        // signataire.
        if (DateTimeOffset.FromUnixTimeSeconds(expiration) > instant + Duree + Tolerance)
        {
            return null;
        }

        if (!registre.TryGetValue(appelant, out var clePublique)
            || string.IsNullOrWhiteSpace(clePublique))
        {
            return null;
        }

        ECDsa cle;
        try
        {
            cle = _clesPubliques.GetOrAdd(clePublique, valeur =>
            {
                var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(valeur), out _);
                return ecdsa;
            });
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            // Clé publique illisible dans le registre = faute de configuration, pas
            // faute de l'appelant. On refuse l'appel — mais on ne laisse pas
            // l'exception remonter : elle sortirait du gabarit `Unauthenticated`
            // et, traduite en `Internal`, compterait comme une PANNE dans le
            // disjoncteur de l'appelant (voir `DisjoncteurClientInterceptor`).
            return null;
        }

        return cle.VerifyData(octets, signature, HashAlgorithmName.SHA256) ? appelant : null;
    }

    /// <summary>
    /// Lit le registre `nom=base64;nom=base64` des clés publiques.
    /// </summary>
    /// <remarks>
    /// UNE SEULE VARIABLE D'ENVIRONNEMENT, PAS DIX-NEUF.
    ///
    /// Le registre est le MÊME pour tous les hôtes et ne contient que des clés
    /// PUBLIQUES — rien à protéger. Le décliner en `Internal__PublicKeys__<nom>`
    /// donnerait dix-neuf lignes par service dans le compose, soit trois cent
    /// soixante et une lignes tenues à la main, dont une seule de fausse suffit
    /// à couper un appelant. Une chaîne unique se copie telle quelle.
    ///
    /// Les entrées illisibles sont IGNORÉES, pas fatales : une clé mal collée ne
    /// doit pas empêcher les dix-huit autres appelants de fonctionner. Elle se
    /// manifestera par des `Unauthenticated` limités à ce seul appelant.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> LireRegistre(string? valeur)
    {
        var registre = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(valeur))
        {
            return registre;
        }

        foreach (var entree in valeur.Split(';', StringSplitOptions.RemoveEmptyEntries
                                                 | StringSplitOptions.TrimEntries))
        {
            var egal = entree.IndexOf('=');
            if (egal <= 0 || egal == entree.Length - 1)
            {
                continue;
            }

            registre[entree[..egal].Trim()] = entree[(egal + 1)..].Trim();
        }

        return registre;
    }

    /// <summary>
    /// Refuse le mode non signé partout sauf en développement.
    /// </summary>
    /// <remarks>
    /// AU DÉMARRAGE, PAS AU PREMIER APPEL.
    ///
    /// Un drapeau de développement hérité par une configuration de production —
    /// un `appsettings` recopié, une variable laissée dans un profil de
    /// déploiement — ne produirait AUCUN symptôme : les appels passeraient, avec
    /// des identités que personne ne vérifie. C'est le pire des cas, celui qui
    /// dure. Levée ici, l'exception empêche l'hôte de se construire.
    ///
    /// Appelée depuis `AddHbaGrpc` pour les vingt-trois services et depuis
    /// `TokenRevocationExtensions` pour la passerelle, qui n'est pas un serveur
    /// gRPC et n'appelle donc pas `AddHbaGrpc`.
    /// </remarks>
    public static void RefuserLeModeNonSigneHorsDeveloppement(
        bool identitesNonSignees, bool estDeveloppement)
    {
        if (identitesNonSignees && !estDeveloppement)
        {
            throw new InvalidOperationException(
                "Internal:IdentitesNonSignees n'est autorisé qu'en environnement "
                + "Development. Fournir Internal:PrivateKey et Internal:PublicKeys.");
        }
    }

    // BASE64URL À LA MAIN : `Base64Url` EST .NET 9, CE PROJET EST net8.0.
    //
    // Le base64 ordinaire contient `+`, `/` et `=`. Une métadonnée gRPC dont la
    // clé ne finit pas par `-bin` doit être de l'ASCII imprimable — ce que le
    // base64 ordinaire est aussi. Le choix de base64url n'est donc pas une
    // obligation du transport : il évite qu'un intermédiaire (journal, proxy,
    // outil de rejeu) traite `+` comme un espace, ce que fait tout décodage
    // d'URL appliqué par erreur.
    private static string Convertir(byte[] octets)
        => Convert.ToBase64String(octets).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Reconvertir(string valeur)
    {
        var brut = valeur.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(brut.PadRight((brut.Length + 3) / 4 * 4, '='));
    }
}
