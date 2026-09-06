#!/usr/bin/env python3
"""
LA STRUCTURE D'INFRASTRUCTURE EST-ELLE RESPECTEE — CONTROLE, PAS DOCUMENT.

Une convention qui n'est verifiee par rien redevient une convention dans six
semaines : c'est ce qui est arrive aux six conventions de gestionnaires Kafka que
la migration a du reunifier. Ce fichier rend la structure OPPOSABLE.

Sortie 0 si tout est conforme, 1 sinon, avec la liste des ecarts. A brancher dans
la CI a cote des controles de `tools/HBA.Controls`.

CE QU'IL NE VERIFIE PAS : que le contenu d'un dossier a un rapport avec son nom.
Il verifie des emplacements, pas des intentions.
"""
import os, sys

RACINE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SKIP = {"obj", "bin", ".git", "build", "node_modules"}

NOEUDS = [
    "Persistence/DbContext", "Persistence/Configurations", "Persistence/Repositories",
    "Persistence/Migrations", "Persistence/Outbox", "Persistence/Inbox",
    "Messaging/Kafka/Configuration", "Messaging/Kafka/Consumers", "Messaging/Kafka/Producers",
    "Messaging/Kafka/Processors", "Messaging/Kafka/Serialization", "Messaging/Kafka/Headers",
    "Messaging/Kafka/Retry",
    "Grpc/Clients", "Grpc/Services", "Grpc/Mappers", "Grpc/Interceptors", "Grpc/Policies",
    "Grpc/Configuration",
    "Caching/Redis/Configuration", "Caching/Redis/Services", "Caching/Redis/Serialization",
    "Caching/Redis/Keys",
    "Idempotency", "Auditing",
    "Observability/Logging", "Observability/Metrics", "Observability/Tracing",
    "Observability/HealthChecks",
    "Security/Encryption", "Security/Hashing", "Security/Token",
    "Resilience/Retry", "Resilience/CircuitBreaker", "Resilience/Timeout", "Resilience/Bulkhead",
    "BackgroundJobs/Workers", "Serialization/Json", "Time", "DependencyInjection",
]

# des .cs ne doivent jamais se trouver DIRECTEMENT dans ces dossiers : ils ont des
# sous-dossiers pour ca, et un fichier a plat a cote d'un dossier vide annonce une
# regle que son voisin dement.
SANS_FICHIERS_A_PLAT = ["Persistence", "Messaging/Kafka", "Caching/Redis", "Observability",
                        "Security", "Resilience", "BackgroundJobs", "Serialization"]

# ces emplacements n'existent plus : leur contenu a rejoint l'arborescence
DISPARUS = ["Messaging/Kafka/Outbox", "Messaging/Kafka/Inbox", "Redis", "GrpcServices"]


def projets():
    for base in ("services", "apps"):
        for d, dirs, fs in os.walk(os.path.join(RACINE, base)):
            dirs[:] = [x for x in dirs if x not in SKIP]
            if os.path.basename(d) == "src":
                for x in sorted(dirs):
                    if x.endswith(".Infrastructure"):
                        yield os.path.join(d, x)


def main():
    ecarts = []
    n = 0
    for p in projets():
        n += 1
        nom = os.path.basename(p)
        for noeud in NOEUDS:
            if not os.path.isdir(os.path.join(p, noeud.replace("/", os.sep))):
                ecarts.append(f"{nom}: noeud absent — {noeud}")
        for dossier in SANS_FICHIERS_A_PLAT:
            chemin = os.path.join(p, dossier.replace("/", os.sep))
            if not os.path.isdir(chemin):
                continue
            # `DependencyInjection.cs` A SA PLACE A LA RACINE DE CHAQUE SECTION :
            # c'est le point d'entree unique de la section, et la structure cible le
            # place la explicitement. Le signaler serait signaler la regle elle-meme.
            plats = [f for f in os.listdir(chemin)
                     if f.endswith(".cs") and f != "DependencyInjection.cs"
                     and os.path.isfile(os.path.join(chemin, f))]
            for f in plats:
                ecarts.append(f"{nom}: fichier a plat — {dossier}/{f}")
        for disparu in DISPARUS:
            if os.path.isdir(os.path.join(p, disparu.replace("/", os.sep))):
                ecarts.append(f"{nom}: emplacement abandonne encore present — {disparu}")
        racine_cs = [f for f in os.listdir(p)
                     if f.endswith(".cs") and os.path.isfile(os.path.join(p, f))]
        for f in racine_cs:
            ecarts.append(f"{nom}: fichier a la racine du projet — {f}")

    print(f"{n} projets d'infrastructure controles, {len(ecarts)} ecarts.")
    for e in ecarts:
        print("  " + e)
    return 1 if ecarts else 0


if __name__ == "__main__":
    sys.exit(main())
