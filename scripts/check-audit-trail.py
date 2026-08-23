#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
UN JOURNAL D'AUDIT PROMIS PAR LE MODÈLE, ABSENT DE LA BASE.

CE DÉFAUT NE SE VOIT NI À LA COMPILATION, NI AU DÉMARRAGE.

`ModuleDbContext.KeepsAuditTrail` est une propriété du MODÈLE. La passer à `true`
mappe l'entité `AuditEntry` et fait écrire une ligne par entité mutée — dans la
même transaction que la mutation. Si la TABLE n'existe pas, l'échec arrive au
premier `SaveChanges` d'un geste métier : ce n'est pas le journal qui casse,
c'est la commande de l'utilisateur.

C'est arrivé : `ReturnRefundDbContext` a porté `KeepsAuditTrail => true` sans
table, et sa migration de rattrapage le dit en toutes lettres — « la table était
déclarée, promise à l'exploitant, et absente de la base ».

ET LA RÉCIPROQUE COMPTE AUTANT.

Deux commentaires du dépôt — `AuditQueries.cs` et `SellersDbContext.cs` —
affirmaient qu'un journal existait dans catalog, inventory et order. Aucun des
trois n'avait ni surcharge, ni table. Un lecteur planifiait un lot en croyant la
moitié du travail déjà faite. Ce contrôle refuse aussi ce sens-là : une table
`audit_entries` sans surcharge est une table que personne n'alimente.

CE QU'IL VÉRIFIE, POUR CHAQUE CONTEXTE QUI JOURNALISE
  1. une migration de son service crée `<schema>.audit_entries` ;
  2. son `*ModelSnapshot.cs` porte le bloc `AuditEntry` — sans quoi la prochaine
     migration générée voudra recréer la table ;
  3. son `OnModelCreating` appelle `base.OnModelCreating` — c'est LÀ que
     `AuditConfiguration` est appliquée, et un override qui l'oublie mappe tout
     sauf le journal, en silence ;
  4. réciproquement, aucun service ne porte une table `audit_entries` sans
     `KeepsAuditTrail => true`.

CE QU'IL NE VÉRIFIE PAS : que la table existe dans une base RÉELLE. Il lit le
dépôt. `check-migrations.py` rejoue les migrations à froid contre le snapshot ;
les deux ensemble couvrent le chemin, pas une base éditée à la main.
═══════════════════════════════════════════════════════════════════════════════
"""
import os
import re
import sys

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IGNORES = ("obj", "bin", "_to_delete", "node_modules", ".git")

CONTEXTE = re.compile(r"class\s+(\w*DbContext)\s*:\s*ModuleDbContext")
SCHEMA = re.compile(r'SchemaName\s*=\s*"([^"]+)"')
ACTIF = re.compile(r"KeepsAuditTrail\s*=>\s*true")
BASE_APPELEE = re.compile(r"base\.OnModelCreating")


def fichiers_cs(racine):
    for dossier, sous, fichiers in os.walk(racine):
        sous[:] = [d for d in sous if d not in IGNORES and not d.startswith(".")]
        for fichier in fichiers:
            if fichier.endswith(".cs"):
                yield os.path.join(dossier, fichier)


def service_de(chemin):
    """Le dossier de service qui contient ce fichier — services/<famille>/<service>."""
    relatif = os.path.relpath(chemin, RACINE).split(os.sep)
    return os.path.join(RACINE, *relatif[:3]) if len(relatif) >= 3 else None


def main():
    services_racine = os.path.join(RACINE, "services")
    if not os.path.isdir(services_racine):
        print("· Aucun dossier « services » — contrôle sauté.")
        return 0

    contextes = []          # (nom, schema, actif, base_appelee, service, fichier)
    for chemin in fichiers_cs(services_racine):
        texte = open(chemin, encoding="utf-8", errors="replace").read()
        trouve = CONTEXTE.search(texte)
        if not trouve:
            continue
        schema = SCHEMA.search(texte)
        contextes.append((
            trouve.group(1),
            schema.group(1) if schema else None,
            bool(ACTIF.search(texte)),
            bool(BASE_APPELEE.search(texte)),
            service_de(chemin),
            chemin))

    anomalies = []
    actifs = [c for c in contextes if c[2]]

    for nom, schema, _, base_ok, service, chemin in actifs:
        relatif = os.path.relpath(chemin, RACINE)

        if schema is None:
            anomalies.append("%s (%s) : journalise mais ne déclare aucun SchemaName."
                             % (nom, relatif))
            continue

        if not base_ok:
            anomalies.append(
                "%s (%s) : `OnModelCreating` n'appelle pas `base.OnModelCreating` — "
                "`AuditConfiguration` n'est donc jamais appliquée et `audit_entries` "
                "n'est pas mappée." % (nom, relatif))

        migrations, snapshots = [], []
        for candidat in fichiers_cs(service):
            base = os.path.basename(candidat)
            texte = open(candidat, encoding="utf-8", errors="replace").read()
            if base.endswith("ModelSnapshot.cs") and "Audit.AuditEntry" in texte:
                snapshots.append(candidat)
            elif "Migrations" in candidat and "audit_entries" in texte and not base.endswith("ModelSnapshot.cs"):
                migrations.append(candidat)

        if not migrations:
            anomalies.append(
                "%s : `KeepsAuditTrail => true`, mais AUCUNE migration de %s ne crée "
                "`%s.audit_entries`. Le premier geste métier lèvera."
                % (nom, os.path.relpath(service, RACINE), schema))

        if not snapshots:
            anomalies.append(
                "%s : `AuditEntry` absent du ModelSnapshot de %s. La prochaine "
                "migration générée voudra recréer la table."
                % (nom, os.path.relpath(service, RACINE)))

    # Le sens inverse : une table sans surcharge.
    services_avec_table = set()
    for chemin in fichiers_cs(services_racine):
        if "Migrations" not in chemin or os.path.basename(chemin).endswith("ModelSnapshot.cs"):
            continue
        if "audit_entries" in open(chemin, encoding="utf-8", errors="replace").read():
            services_avec_table.add(service_de(chemin))

    services_actifs = {c[4] for c in actifs}
    for service in sorted(services_avec_table - services_actifs):
        anomalies.append(
            "%s : une migration crée `audit_entries`, mais aucun contexte de ce "
            "service ne porte `KeepsAuditTrail => true`. Table que personne "
            "n'alimente." % os.path.relpath(service, RACINE))

    print()
    print("  %d contexte(s) de module, dont %d journalisent."
          % (len(contextes), len(actifs)))
    print()
    if actifs:
        print("  ── Schémas qui tiennent un journal")
        for nom, schema, _, _, _, _ in sorted(actifs, key=lambda c: c[1] or ""):
            print("       ✓ %-22s %s" % (schema, nom))
        print()

    for message in anomalies:
        print("  ❌ " + message)

    print("%d anomalie(s) de journal d'audit." % len(anomalies))
    return 1 if anomalies else 0


if __name__ == "__main__":
    sys.exit(main())
