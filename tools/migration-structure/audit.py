#!/usr/bin/env python3
"""
LE JOURNAL D'AUDIT DESCEND DANS CHAQUE SERVICE QUI EN TIENT UN.

CE QUI ETAIT PARTAGE, ET POURQUOI C'EST DIFFERENT DU CACHE.

`AuditEntry` n'est pas un service : c'est une ENTITE EF, et sa table
`audit_entries` est creee par les migrations de QUATORZE services — ceux dont le
contexte surcharge `KeepsAuditTrail => true`. Le socle ne se contentait pas de la
declarer : `ModuleDbContext.RecordAuditTrail()` ECRIVAIT les lignes, dans la meme
transaction que le metier.

CE QUI DESCEND, ET CE QUI RESTE.

Descend : l'entite `AuditEntry`, sa configuration EF, et l'ecriture des lignes.
Chacun des quatorze services les porte dans `Auditing/`.

Reste : l'enum `AuditOperation` et la COLLECTE des mutations. La collecte lit le
ChangeTracker, decide ce qui compte comme une mutation, resout l'acteur et fixe un
instant unique pour toute la transaction — ce sont des regles identiques partout,
et les dupliquer quatorze fois donnerait quatorze journaux qui ne se comparent
plus. Le socle appelle desormais deux points d'extension vides par defaut :

    ConfigurerLeJournalDAudit(ModelBuilder)
    AjouterUneEntreeDAudit(type, id, operation, acteur, typeActeur, correlation, instant)

Le service repond avec SON entite. Le socle ne connait plus aucune table d'audit.

CE QUE ÇA COUTE, ET IL FAUT LE FAIRE : les instantanes de modele des QUATORZE
migrations referencent « HBA.Shared.Infrastructure.Audit.AuditEntry » sous forme
de chaine. Ils compilent, et les migrations s'appliquent toujours par identifiant
— rien ne casse a l'execution. Mais le modele et l'instantane divergent : il faut
un `dotnet ef migrations add` par service pour les regenerer, et le diff de schema
sera VIDE puisque la table ne change pas.
"""
import os, re, io, sys, shutil

RACINE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SKIP = {"obj", "bin", ".git", "build", "node_modules"}
SEC = "--ecrire" in sys.argv
AUDIT = os.path.join(RACINE, "shared", "common", "HBA.Shared.Infrastructure", "Audit")
SOCLE = os.path.join(RACINE, "shared", "common", "HBA.Shared.Infrastructure", "Persistence", "ModuleDbContext.cs")


def fichiers_cs(base):
    for d, dirs, fs in os.walk(base):
        dirs[:] = [x for x in dirs if x not in SKIP]
        for f in fs:
            if f.endswith(".cs"):
                yield os.path.join(d, f)


def racine_de_namespace(dossier):
    declares = []
    for f in fichiers_cs(dossier):
        m = re.search(r'^namespace\s+([\w\.]+)', io.open(f, encoding="utf-8", errors="replace").read(), re.M)
        if m: declares.append(m.group(1).split("."))
    if not declares: return None
    commun = declares[0]
    for d in declares[1:]:
        n = 0
        while n < min(len(commun), len(d)) and commun[n] == d[n]: n += 1
        commun = commun[:n]
    while len(commun) > 1 and os.path.isdir(os.path.join(dossier, commun[-1])):
        commun = commun[:-1]
    return ".".join(commun) if commun else None


def contextes_avec_journal():
    for base in ("services",):
        for f in fichiers_cs(os.path.join(RACINE, base)):
            s = io.open(f, encoding="utf-8", errors="replace").read()
            if re.search(r'protected\s+override\s+bool\s+KeepsAuditTrail\s*=>\s*true', s):
                d = f
                while os.path.basename(d) and not os.path.basename(d).endswith(".Infrastructure"):
                    d = os.path.dirname(d)
                yield f, d


def main():
    cibles = sorted(contextes_avec_journal())
    print(f"{len(cibles)} contextes tiennent un journal d'audit :")
    for f, infra in cibles:
        print(f"   {os.path.basename(infra):46} {os.path.basename(f)}")
    if not SEC:
        print("\nSIMULATION.")
        return

    source_entite = io.open(os.path.join(AUDIT, "AuditEntry.cs"), encoding="utf-8").read()
    source_config = io.open(os.path.join(AUDIT, "AuditConfiguration.cs"), encoding="utf-8").read()

    # l'enum reste au socle : il fait partie de la signature du point d'extension
    m = re.search(r'(/// <summary>.*?)?public enum AuditOperation\s*\{[^}]*\}', source_entite, re.S)
    bloc_enum = m.group(0)
    entite_sans_enum = source_entite.replace(bloc_enum, "").replace("namespace HBA.Shared.Infrastructure.Audit;", "@@NS@@")

    io.open(os.path.join(RACINE, "shared", "common", "HBA.Shared.Infrastructure",
                         "Persistence", "AuditOperation.cs"), "w", encoding="utf-8").write(
        """namespace HBA.Shared.Infrastructure.Persistence;

// ═════════════════════════════════════════════════════════════════════════════
// L'ENUM RESTE AU SOCLE, L'ENTITE EST DESCENDUE DANS LES SERVICES.
//
// Ce n'est pas une exception a la regle, c'est la regle : ce type fait partie de
// la SIGNATURE du point d'extension `AjouterUneEntreeDAudit`. Le dupliquer par
// service donnerait quatorze enums distincts pour une meme colonne, et deux
// journaux ne se compareraient plus.
// ═════════════════════════════════════════════════════════════════════════════
""" + bloc_enum + "\n")

    for _, infra in cibles:
        racine_ns = racine_de_namespace(infra)
        dossier = os.path.join(infra, "Auditing")
        os.makedirs(dossier, exist_ok=True)
        l = os.path.join(dossier, "LISEZMOI.md")
        if os.path.exists(l): os.remove(l)

        entete = f"""// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Shared.Infrastructure.Audit`.
//
// La table `audit_entries` de CE service est creee par SES migrations : l'entite
// qui la decrit lui appartient donc. Le socle ne connait plus aucune table
// d'audit — il collecte les mutations et appelle `AjouterUneEntreeDAudit`, que le
// contexte de ce service remplit avec l'entite ci-dessous.
//
// A REGENERER : l'instantane de modele des migrations de ce service reference
// encore « HBA.Shared.Infrastructure.Audit.AuditEntry » sous forme de chaine. Il
// compile et les migrations s'appliquent toujours — mais le modele et
// l'instantane divergent jusqu'a un `dotnet ef migrations add`, dont le diff de
// schema sera VIDE puisque la table ne change pas.
// ═════════════════════════════════════════════════════════════════════════════
"""
        e = entite_sans_enum.replace("@@NS@@", entete + f"\nnamespace {racine_ns}.Auditing;")
        e = re.sub(r'^public sealed class AuditEntry', "internal sealed class AuditEntry", e, flags=re.M)
        if "using HBA.Shared.Infrastructure.Persistence;" not in e:
            e = "using HBA.Shared.Infrastructure.Persistence;\n" + e
        io.open(os.path.join(dossier, "AuditEntry.cs"), "w", encoding="utf-8").write(e)

        c = source_config.replace("namespace HBA.Shared.Infrastructure.Audit;",
                                  entete + f"\nnamespace {racine_ns}.Auditing;")
        c = re.sub(r'^public sealed class AuditConfiguration', "internal sealed class AuditConfiguration",
                   c, flags=re.M)
        if "using HBA.Shared.Infrastructure.Persistence;" not in c:
            c = "using HBA.Shared.Infrastructure.Persistence;\n" + c
        io.open(os.path.join(dossier, "AuditConfiguration.cs"), "w", encoding="utf-8").write(c)

    # les contextes repondent aux deux points d'extension
    for f, infra in cibles:
        racine_ns = racine_de_namespace(infra)
        s = io.open(f, encoding="utf-8").read()
        if "AjouterUneEntreeDAudit" in s:
            continue
        m = re.search(r'([ \t]*)protected override bool KeepsAuditTrail => true;\n', s)
        if not m:
            print("  ancrage introuvable :", f)
            continue
        i = m.group(1)
        surcharges = f"""
{i}// ═════════════════════════════════════════════════════════════════════════
{i}// LE JOURNAL D'AUDIT DE CE SERVICE — L'ENTITE ET SA TABLE LUI APPARTIENNENT.
{i}//
{i}// Le socle collecte les mutations, resout l'acteur et fixe l'instant unique de
{i}// la transaction ; il ne connait plus aucune table d'audit. Ces deux methodes
{i}// sont ce qu'il appelle, et elles repondent avec l'entite de `Auditing/`.
{i}// ═════════════════════════════════════════════════════════════════════════
{i}protected override void ConfigurerLeJournalDAudit(ModelBuilder modelBuilder)
{i}    => modelBuilder.ApplyConfiguration(new AuditConfiguration());

{i}protected override void AjouterUneEntreeDAudit(
{i}    string typeDEntite,
{i}    string identifiant,
{i}    AuditOperation operation,
{i}    Guid? acteur,
{i}    string typeDActeur,
{i}    string? correlation,
{i}    DateTime instantUtc)
{i}    => Set<AuditEntry>().Add(new AuditEntry
{i}    {{
{i}        EntityType = typeDEntite,
{i}        EntityId = identifiant,
{i}        Operation = operation,
{i}        ActorUserId = acteur,
{i}        ActorType = typeDActeur,
{i}        CorrelationId = correlation,
{i}        OccurredOnUtc = instantUtc
{i}    }});
"""
        s = s[:m.end()] + surcharges + s[m.end():]
        for besoin in (f"using {racine_ns}.Auditing;", "using HBA.Shared.Infrastructure.Persistence;",
                       "using Microsoft.EntityFrameworkCore;"):
            if not re.search(r'^' + re.escape(besoin), s, re.M):
                u = list(re.finditer(r'^using\s+[^\n]+;\s*$', s, re.M))
                s = s[:u[-1].end()] + "\n" + besoin + s[u[-1].end():]
        io.open(f, "w", encoding="utf-8").write(s)

    print(f"\n{len(cibles)} services pourvus de leur journal.")


if __name__ == "__main__":
    main()
