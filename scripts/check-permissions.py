#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
UNE PERMISSION QUE PERSONNE N'INTERROGE EST UN DROIT SANS EFFET.

IL Y EN AVAIT SEPT, ET RIEN NE PERMETTAIT DE LE SAVOIR.

`MerchantPermission` déclare cinquante-sept permissions. Chacune est attribuée à
des rôles, affichée au vendeur, cochable dans un rôle personnalisé. Sept
n'étaient exigées par AUCUNE route ni AUCUN handler :

  • `INVENTORY_TRANSFER` et `STOCK_MOVEMENT_VIEW` — le rôle `INVENTORY_MANAGER`
    promet « Stocks, ajustements, transferts », et le mot « transfert »
    n'apparaissait nulle part dans inventory-service (fermées au lot 7.3) ;
  • `RETURN_DISPUTE_VIEW` — aucune notion de litige n'existe ;
  • `REVIEW_VIEW` — la lecture est ouverte à tout compte authentifié ;
  • `ROLE_ASSIGN` — doublon de `MEMBER_ASSIGN_ROLE` ;
  • `BANK_ACCOUNT_UPDATE` — doublon de `PAYOUT_CONFIGURE`, tous deux critiques et
    réservés au propriétaire ;
  • `SECURITY_POLICY_UPDATE` — sans objet, et c'était déjà écrit.

Le constat demandait de croiser cinquante-sept déclarations avec tous les appels
du dépôt. Personne ne le fait deux fois.

CE CONTRÔLE A MENTI À SA PREMIÈRE EXÉCUTION, ET C'EST INSTRUCTIF.

Il cherchait les usages sous la seule forme `MerchantPermission.X` /
`MerchantCapabilities.X`. Or `SellerReturnsEndpoints` recopiait les codes en
chaînes littérales dans ses propres `private const string`. Le contrôle a donc
annoncé cinq `RETURN_*` « sans garde » alors qu'elles gardaient DIX routes — et
la correction évidente aurait été de les inscrire dans `SansGardeAssumee`, donc
de graver dans le dépôt le contraire de la vérité.

C'est la quatrième fois dans ce chantier qu'un contrôle partage l'hypothèse
fausse du code qu'il contrôle. D'où la troisième règle ci-dessous : le littéral
n'est plus seulement VU, il est REFUSÉ. Un code de permission recopié à la main
compile même mal orthographié, et `Can("RETURN_VEIW")` est faux pour tout le
monde — une garde qui refuse tout le monde est aussi cassée qu'une garde
absente, et personne ne s'en aperçoit avant le premier vendeur bloqué.

CE QU'IL VÉRIFIE

Il calcule l'ensemble des permissions RÉELLEMENT exigées — tout
`MerchantPermission.X`, tout `MerchantCapabilities.X`, et tout code littéral
`"X"` hors des deux fichiers de déclaration et hors des commentaires — et le
compare à `MerchantPermissions.SansGardeAssumee`. Trois anomalies :

  • une permission sans garde ET absente de la liste : un droit sans effet que
    personne n'a assumé. Il faut la brancher, ou l'inscrire en disant pourquoi ;
  • une permission inscrite dans la liste ET pourtant gardée : la liste ment, et
    un lecteur planifiera un lot déjà fait — c'est exactement ce que
    `AuditQueries` faisait pour les journaux d'audit ;
  • un code de permission écrit en chaîne littérale hors des déclarations : la
    garde existe peut-être, mais rien ne garantit qu'elle vise la bonne
    permission. `MerchantCapabilities` expose une constante par code — c'est le
    compilateur qui doit tenir cette correspondance, pas la vigilance du lecteur.

LES COMMENTAIRES SONT RETIRÉS AVANT ANALYSE. Sans cela, un bandeau qui cite
`"RETURN_VIEW"` pour EXPLIQUER le problème passerait pour la garde elle-même —
le fichier corrigé de return-refund fait exactement cela.

CE QU'IL NE VÉRIFIE PAS : que la garde soit au BON endroit, ni qu'elle couvre
toutes les routes qu'elle devrait. Une permission exigée par une seule route sur
cinq passe ce contrôle.
═══════════════════════════════════════════════════════════════════════════════
"""
import os
import re
import sys

from _lecture_csharp import sans_commentaires

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IGNORES = ("obj", "bin", "_to_delete", "node_modules", ".git")

CATALOGUE = os.path.join(
    RACINE, "services", "marketplace", "seller-service", "src",
    "HBA.Merchants.Domain", "Members", "MerchantPermission.cs")

# Les fichiers qui DÉCLARENT ou ATTRIBUENT : les y voir n'est pas une garde.
DECLARATIONS = ("MerchantPermission.cs", "MerchantCapabilities.cs", "SellerRole.cs")

ENTREE = re.compile(r'new\(MerchantPermission\.(\w+),\s*"([A-Z_]+)"')
USAGE = re.compile(r"Merchant(?:Permission|Capabilities)\.(\w+)")
ASSUMEE = re.compile(r"MerchantPermission\.(\w+)")




def main():
    if not os.path.isfile(CATALOGUE):
        print("· Catalogue de permissions introuvable — contrôle sauté.")
        return 0

    with open(CATALOGUE, encoding="utf-8") as flux:
        source = flux.read()

    catalogue = {nom: code for nom, code in ENTREE.findall(source)}
    if not catalogue:
        print("· Aucune entrée de catalogue reconnue — contrôle sans objet.")
        return 0

    # L'ensemble assumé, entre les accolades de `SansGardeAssumee`.
    debut = source.find("SansGardeAssumee")
    assumees = set()
    if debut != -1:
        ouverture = source.index("{", debut)
        fermeture = source.index("};", ouverture)
        assumees = set(ASSUMEE.findall(source[ouverture:fermeture]))

    # Le chemin inverse : du code au nom, pour rendre compte d'un littéral.
    par_code = {code: nom for nom, code in catalogue.items()}

    # Tout usage réel, hors déclaration et attribution, hors tests.
    employees = set()
    litteraux = {}
    for dossier, sous, fichiers in os.walk(RACINE):
        sous[:] = [d for d in sous if d not in IGNORES and not d.startswith(".")]
        if os.sep + "tests" + os.sep in dossier + os.sep:
            continue
        for fichier in fichiers:
            if not fichier.endswith(".cs") or fichier in DECLARATIONS:
                continue
            chemin = os.path.join(dossier, fichier)
            with open(chemin, encoding="utf-8", errors="replace") as flux:
                brut = flux.read()

            # Le symbole se lit sur le source entier : le voir dans un commentaire
            # ne coûte rien puisqu'il faudrait de toute façon qu'il existe.
            employees.update(USAGE.findall(brut))

            # Le littéral, lui, se lit sur le source SANS commentaires — sinon un
            # bandeau qui cite un code passerait pour la garde elle-même.
            code_seul = sans_commentaires(brut)
            for code in par_code:
                if '"%s"' % code in code_seul:
                    employees.add(par_code[code])
                    litteraux.setdefault(code, []).append(
                        os.path.relpath(chemin, RACINE))

    connues = set(catalogue)
    sans_garde = connues - employees
    anomalies = []

    for nom in sorted(sans_garde - assumees):
        anomalies.append(
            "« %s » n'est exigée par aucune route ni aucun handler, et n'est pas "
            "inscrite dans `MerchantPermissions.SansGardeAssumee`. C'est un droit "
            "affiché au vendeur, cochable dans un rôle, et sans le moindre effet — "
            "à brancher, ou à assumer en écrivant pourquoi." % catalogue[nom])

    for nom in sorted(assumees - sans_garde):
        code = catalogue.get(nom, nom)
        anomalies.append(
            "« %s » est inscrite dans `SansGardeAssumee` alors qu'elle garde bien "
            "quelque chose. La liste ment : à retirer." % code)

    for code in sorted(litteraux):
        anomalies.append(
            "« %s » est écrite en chaîne littérale dans %s. Le code est recopié à "
            "la main : une faute de frappe compile, et la garde refuse alors TOUT "
            "LE MONDE sans que rien ne le signale. `MerchantCapabilities.%s` dit "
            "la même chose et le compilateur la tient."
            % (code, ", ".join(sorted(set(litteraux[code]))), par_code[code]))

    print()
    print("  %d permission(s) au catalogue, %d gardée(s), %d sans garde assumée."
          % (len(connues), len(connues & employees), len(sans_garde & assumees)))

    if sans_garde & assumees:
        print()
        print("  ── Sans garde, et assumées comme telles")
        for nom in sorted(sans_garde & assumees):
            print("       ⓘ " + catalogue[nom])

    print()
    for message in anomalies:
        print("  ❌ " + message)

    print("%d anomalie(s) de permission." % len(anomalies))
    return 1 if anomalies else 0


if __name__ == "__main__":
    sys.exit(main())
