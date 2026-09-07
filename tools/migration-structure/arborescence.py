#!/usr/bin/env python3
"""
L'ARBORESCENCE D'INFRASTRUCTURE, POSEE DANS LES 26 SERVICES.

POURQUOI DES FICHIERS DANS LES DOSSIERS VIDES.

Git ne versionne pas les dossiers, seulement les fichiers. « La forme partout,
meme vide » n'est donc PAS exprimable sans un fichier par dossier : un dossier
vide disparait au prochain clone, et la structure ne serait vraie que sur la
machine ou elle a ete creee.

Chaque dossier sans contenu recoit donc un LISEZMOI.md qui dit trois choses :
ce qui va la, ou l'implementation vit AUJOURD'HUI, et ce que ce service-ci a.
Un marqueur qui n'expliquerait rien serait du bruit ; celui-ci repond a la
question qu'on se pose en ouvrant un dossier vide.

CE SCRIPT NE DEPLACE RIEN ET N'ECRASE RIEN. Il ne touche qu'aux dossiers absents
ou vides, et il est rejouable.
"""
import os, io, sys, collections

RACINE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SKIP = {"obj", "bin", ".git", "build", "node_modules"}
SEC = "--ecrire" in sys.argv

# noeud -> (ce qui va la, ou ca vit aujourd'hui)
NOEUDS = {
 "Persistence": ("le DbContext du service, ses configurations EF et ses depots",
   "ici meme, a plat — les sous-dossiers ci-dessous rangent l'existant"),
 "Persistence/DbContext": ("`<Service>DbContext.cs`",
   "a plat dans `Persistence/` dans les 25 services qui en ont un"),
 "Persistence/Configurations": ("un `IEntityTypeConfiguration<T>` par entite", "ici, 22 services sur 26"),
 "Persistence/Repositories": ("les depots de ce service", "a plat dans `Persistence/` pour la plupart"),
 "Persistence/Migrations": ("les migrations EF de ce service",
   "A LA RACINE du projet dans 20 services. Les deplacer touche l'outillage "
   "`dotnet ef`, pas seulement des fichiers : sans `--output-dir`, la migration "
   "suivante revient a la racine. C'est un lot a part."),
 "Persistence/Outbox": ("le CABLAGE de l'outbox de ce service, pas une copie de l'entite",
   "`HBA.Shared.Infrastructure.Outbox` — `OutboxMessage` est une entite EF dont "
   "les colonnes sont creees par les migrations de 18 services sur la MEME table, "
   "et `OutboxProcessor<TDbContext>` est deja generique sur le DbContext de ce "
   "service. Le service possede donc deja son outbox a l'execution ; une copie du "
   "code ne lui donnerait que le droit de diverger de la table."),
 "Persistence/Inbox": ("le cablage de la garde anti-doublon",
   "`HBA.Shared.Infrastructure.Inbox` — `ConsumerInboxEntry` et `EfConsumerInbox`. "
   "MANQUE ENCORE : rien ne purge `consumer_inbox`, contrairement a l'outbox et a "
   "l'idempotence qui ont leur purger."),
 "Messaging/Kafka/Configuration": ("les sujets ecoutes par CE service, et la garde de cablage", "ici — 26 services sur 26"),
 "Messaging/Kafka/Consumers": ("un fichier par gestionnaire d'evenement", "ici"),
 "Messaging/Kafka/Producers": ("`EvenementsPublies.cs` — ce que ce service publie, DECLARE", "ici"),
 "Messaging/Kafka/Processors": ("le cablage de l'outbox et de l'inbox",
   "`Outbox/` et `Inbox/` de ce module, plus `OutboxProcessor<T>` partage"),
 "Messaging/Kafka/Serialization": ("un convertisseur propre a ce service, s'il en a un",
   "`HbaEventEnvelope` — l'enveloppe est le FORMAT SUR LE FIL. Deux implementations, "
   "c'est deux formats, et un consommateur qui ne reconnait plus ce qu'un producteur "
   "ecrit : la panne `livraison.*` / `service.*` de ce mois-ci."),
 "Messaging/Kafka/Headers": ("les en-tetes propres a ce service",
   "portes par l'enveloppe partagee — correlation, `traceparent`, type et version"),
 "Messaging/Kafka/Retry": ("la politique de rejeu de ce service",
   "`OutboxRetryPolicy` (10 tentatives, backoff) et la file de lettres mortes `<sujet>.dlq`"),
 "Grpc/Clients": ("les adaptateurs `I<X>ModuleApi` vers le stub gRPC",
   "`shared/contracts/HBA.<X>.Contracts.Grpc`, un par domaine. Un adaptateur "
   "implemente l'interface ENTIERE : une copie par consommateur serait integrale."),
 "Grpc/Services": ("rien — le serveur gRPC est la SURFACE du service",
   "`<Service>.Api/Grpc/Services/` depuis le lot B. Ce dossier existe pour que "
   "l'arborescence soit la meme partout ; le serveur, lui, n'est pas un detail "
   "d'infrastructure."),
 "Grpc/Mappers": ("les traductions proto <-> enregistrements de contrats",
   "dans l'assemblage de contrats du domaine, en un exemplaire"),
 "Grpc/Interceptors": ("un intercepteur propre a ce service, s'il en a un",
   "`HBA.Shared.Hosting.Grpc` — disjoncteur, identite interne, correlation, "
   "traduction des erreurs. Uniformes sur les 17 clients, verifie."),
 "Grpc/Policies": ("la resilience des appels sortants de ce service",
   "disjoncteur par service appele, et l'echeance par defaut de 5 s posee par "
   "`InternalCallClientInterceptor`. La surcharge se declare dans "
   "`Grpc/Configuration/DestinationsGrpc.cs`."),
 "Grpc/Configuration": ("ce que ce service appelle, et avec quelle echeance", "ici, pour les 15 services appelants"),
 "Caching/Redis/Configuration": ("les reglages Redis de ce service",
   "`RedisOptions` vient du deploiement — une seule source pour l'adresse et le mot de passe"),
 "Caching/Redis/Services": ("un cache propre a ce service",
   "`HBA.Shared.Infrastructure.Caching` — `DistributedCacheService` et `NoOpCacheService`"),
 "Caching/Redis/Serialization": ("un serialiseur de cache propre a ce service", "JSON par defaut, non specialise"),
 "Caching/Redis/Keys": ("les cles de cache de CE service — elles lui appartiennent vraiment",
   "a plat dans l'Infrastructure des services qui en ont (`CartCacheKeys`, `FoodCartCacheKeys`)"),
 "Idempotency": ("le cablage de l'idempotence de ce service",
   "`HBA.Shared.Infrastructure.Idempotency` — store, entite, purger. Elle protege "
   "AUSSI les routes HTTP annotees `AllowIdempotency()`, qui n'ont rien a voir avec "
   "Kafka : elle reste enregistree dans `<Service>ModuleInstaller`."),
 "Auditing": ("la lecture du journal d'audit de ce service",
   "`AuditEntry` et `AuditConfiguration` partages ; la table, elle, est celle de ce "
   "service. MANQUENT : l'intercepteur d'audit, l'accesseur de contexte et les options."),
 "Observability/Logging": ("un enrichisseur de journal propre a ce service",
   "`HbaTelemetry`, pose sur les 24 services d'un coup par `AddHbaService`"),
 "Observability/Metrics": ("les metriques metier de ce service", "`HbaTelemetry` + `NoOpMetrics`"),
 "Observability/Tracing": ("les sources d'activite de ce service", "`HbaTelemetry`, OpenTelemetry"),
 "Observability/HealthChecks": ("les controles de sante des dependances de CE service",
   "`AddDbContextCheck<TDbContext>(\"database\")`, et RIEN D'AUTRE. Redis, Kafka et "
   "gRPC ne sont pas verifies : un service dont le consommateur Kafka est mort "
   "repond `ready`, et le deploiement individuel le croit sain. C'est un vrai trou."),
 "Security/Encryption": ("le chiffrement propre a ce service",
   "`AesGcmSecretProtector` — meme cle des deux cotes, sans quoi rien n'est dechiffrable"),
 "Security/Hashing": ("le hachage propre a ce service", "identity-service a le sien, dans son `Security/`"),
 "Security/Token": ("les jetons propres a ce service", "identity-service a le sien ; les autres n'en emettent pas"),
 "Resilience/Retry": ("la politique de reprise de ce service", "Polly, via le socle"),
 "Resilience/CircuitBreaker": ("le disjoncteur de ce service",
   "`DisjoncteurClientInterceptor`, par service appele, sur les 17 clients gRPC"),
 "Resilience/Timeout": ("les delais de ce service", "voir `Grpc/Configuration/` — 5 s par defaut"),
 "Resilience/Bulkhead": ("le cloisonnement des appels de ce service",
   "N'EXISTE NULLE PART. Aucun cloisonnement dans le depot aujourd'hui."),
 "BackgroundJobs/Workers": ("les taches de fond de ce service", "ici pour les 6 services qui en ont"),
 "Serialization/Json": ("les options JSON propres a ce service",
   "`HBA.Shared.Infrastructure.Serialization` — le format de l'enveloppe en depend"),
 "Time": ("l'horloge de ce service",
   "N'EXISTE PAS : `DateTime.UtcNow` partout. La bonne reponse n'est PAS d'ecrire "
   "`IClock` 26 fois — .NET 9 fournit `TimeProvider`, que les tests remplacent par "
   "`FakeTimeProvider`. Ce dossier restera vide, et c'est la reponse."),
 "DependencyInjection": ("le cablage decoupe par preoccupation",
   "`<Service>ModuleInstaller.cs`, en un seul fichier"),
}


def contenu(noeud, projet, quoi, ou):
    return f"""# `{noeud}/`

Vide dans **{projet}**.

**Ce qui va ici :** {quoi}.

**Ou ca vit aujourd'hui :** {ou}

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
"""


def main():
    projets = []
    for base in ("services", "bff"):
        for d, dirs, fs in os.walk(base):
            dirs[:] = [x for x in dirs if x not in SKIP]
            if os.path.basename(d) == "src":
                for x in sorted(dirs):
                    if x.endswith(".Infrastructure"):
                        projets.append(os.path.join(RACINE, d, x))

    crees, marques, laisses = 0, 0, 0
    for p in projets:
        for noeud, (quoi, ou) in NOEUDS.items():
            chemin = os.path.join(p, noeud.replace("/", os.sep))
            existe = os.path.isdir(chemin)
            contenu_present = existe and any(
                f for f in os.listdir(chemin) if f != "LISEZMOI.md")
            if contenu_present:
                laisses += 1
                continue
            if not existe:
                crees += 1
            lisezmoi = os.path.join(chemin, "LISEZMOI.md")
            if os.path.exists(lisezmoi):
                continue
            marques += 1
            if SEC:
                os.makedirs(chemin, exist_ok=True)
                io.open(lisezmoi, "w", encoding="utf-8").write(
                    contenu(noeud, os.path.basename(p), quoi, ou))

    print(f"{len(projets)} projets x {len(NOEUDS)} noeuds = {len(projets)*len(NOEUDS)} dossiers vises")
    print(f"  deja pourvus de contenu : {laisses}")
    print(f"  dossiers a creer        : {crees}")
    print(f"  LISEZMOI a ecrire       : {marques}")
    if not SEC:
        print("\nSIMULATION. Relancer avec --ecrire pour appliquer.")


if __name__ == "__main__":
    main()
