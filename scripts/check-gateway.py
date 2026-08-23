#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
CINQ ENDROITS À TENIR D'ACCORD POUR QU'UN SERVICE SOIT JOIGNABLE.

CE DÉFAUT S'EST PRODUIT QUATRE FOIS, ET IL NE SE VOIT JAMAIS À LA LECTURE.

Pour qu'une requête publique atteigne un service, il faut :

  1. une ADRESSE dans `appsettings.json` → `Services:<Clé>` ;
  2. une PROPRIÉTÉ dans `ServicesOptions` — sans elle, la variable
     `SERVICES__<CLÉ>` du compose est ingérée et JETÉE EN SILENCE ;
  3. une branche dans `ServicesOptions.Resolve` ;
  4. une entrée dans `ServiceKeys.All` ;
  5. un CLUSTER dans `appsettings.json` → `ReverseProxy:Clusters`, et une ROUTE
     qui le désigne.

Manquer (2), (3) ou (4) donne un cluster SANS destination : `ServiceAddressConfigFilter`
journalise une erreur et la requête tombe en **503**, avec une configuration qui a
l'air complète — l'adresse est bien écrite, au bon endroit, sous le bon nom.
Manquer (5) donne un **404** de passerelle, qui ne ressemble pas non plus à une
panne d'amont.

L'HISTORIQUE
  • « Promotion » : adresse écrite, propriété absente → 503 sur tout le parcours
    promotions ;
  • « FoodCart » et « FoodOrder » : mêmes symptômes sur tout le parcours
    restaurant ;
  • « ReturnRefund », « Drivers », « DeliveryPricing » (lot 7.5) : le compose
    fournissait leurs adresses depuis longtemps, vers du vide. Vingt et une
    routes de return-refund-service, les cinq routes de validation des livreurs
    et l'édition de la grille tarifaire étaient injoignables depuis Internet.

CE QU'IL VÉRIFIE
  a. tout cluster a une adresse dans `Services:` ;
  b. toute route désigne un cluster déclaré ;
  c. tout cluster est atteint par au moins une route — un cluster sans route est
     une destination que rien n'emprunte ;
  d. les cinq endroits portent EXACTEMENT le même jeu de clés ;
  e. toute clé employée est présente dans le configmap Kubernetes.

CE QU'IL NE VÉRIFIE PAS : que le service RÉPONDE, ni que le gabarit d'une route
corresponde à un `MapGroup` réel. Un préfixe mal orthographié — `/api/v1/admin/`
là où le service sert `/api/admin/` — passe ce contrôle et rend 404. C'est
`check-config-and-guards.py` et les tests de routage qui portent cette moitié.
═══════════════════════════════════════════════════════════════════════════════
"""
import json
import os
import re
import sys

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APPSETTINGS = os.path.join(
    RACINE, "apps", "api-gateway", "src", "HBA.Gateway.Api", "appsettings.json")
OPTIONS = os.path.join(
    RACINE, "apps", "api-gateway", "src", "HBA.Gateway.Infrastructure",
    "Configuration", "ServicesOptions.cs")
CONFIGMAP = os.path.join(RACINE, "k8s", "base", "common", "configmap.yaml")

PROPRIETE = re.compile(r"public\s+string\s+(\w+)\s*\{\s*get;\s*init;")
BRANCHE = re.compile(r"ServiceKeys\.(\w+)\s*=>")
CONSTANTE = re.compile(r'public const string (\w+) = "(\w+)";')


def main():
    if not os.path.isfile(APPSETTINGS) or not os.path.isfile(OPTIONS):
        print("· Passerelle introuvable — contrôle sauté.")
        return 0

    with open(APPSETTINGS, encoding="utf-8") as flux:
        config = json.load(flux)

    adresses = {c for c in config.get("Services", {}) if not c.startswith("_")}
    proxy = config.get("ReverseProxy", {})
    clusters = set(proxy.get("Clusters", {}))
    routes = proxy.get("Routes", {})

    with open(OPTIONS, encoding="utf-8", errors="replace") as flux:
        source = flux.read()

    # `Resolve` d'abord : ses branches nomment les clés réellement résolues.
    debut = source.index("public string? Resolve(")
    fin = source.index("_ => null", debut)
    branches = set(BRANCHE.findall(source[debut:fin]))

    # La liste `All`, entre ses crochets.
    debut_all = source.index("public static readonly IReadOnlyList<string> All")
    liste = set(re.findall(r"\b([A-Z]\w+)\b", source[source.index("[", debut_all):source.index("];", debut_all)]))

    proprietes = set(PROPRIETE.findall(source))

    anomalies = []

    for cluster in sorted(clusters):
        if cluster not in adresses:
            anomalies.append("cluster « %s » : aucune adresse dans `Services:` — "
                             "il se chargera SANS destination, donc 503." % cluster)
        if cluster not in proprietes:
            anomalies.append("cluster « %s » : aucune propriété dans `ServicesOptions` — "
                             "la variable SERVICES__%s est ingérée et jetée."
                             % (cluster, cluster.upper()))
        if cluster not in branches:
            anomalies.append("cluster « %s » : aucune branche dans `Resolve` — "
                             "il rendra null." % cluster)
        if cluster not in liste:
            anomalies.append("cluster « %s » : absent de `ServiceKeys.All`." % cluster)

    for adresse in sorted(adresses - clusters):
        anomalies.append("adresse « Services:%s » : aucun cluster ne l'emploie." % adresse)

    designes = set()
    for nom, route in routes.items():
        cible = route.get("ClusterId")
        designes.add(cible)
        if cible not in clusters:
            anomalies.append("route « %s » : désigne le cluster « %s », qui n'existe pas."
                             % (nom, cible))

    for cluster in sorted(clusters - designes):
        anomalies.append("cluster « %s » : aucune route ne le désigne — destination "
                         "que rien n'emprunte." % cluster)

    absentes_k8s = []
    if os.path.isfile(CONFIGMAP):
        with open(CONFIGMAP, encoding="utf-8") as flux:
            texte = flux.read()
        for cluster in sorted(clusters):
            variable = "SERVICES__" + cluster.upper()
            if variable + ":" not in texte:
                absentes_k8s.append(variable)

    for variable in absentes_k8s:
        anomalies.append("k8s/base/common/configmap.yaml : %s absent — `ServicesOptions` "
                         "la déclare `[Required]`, la passerelle ne démarrera pas." % variable)

    print()
    print("  %d cluster(s), %d route(s), %d adresse(s)."
          % (len(clusters), len(routes), len(adresses)))
    print()
    for message in anomalies:
        print("  ❌ " + message)

    print("%d incohérence(s) de passerelle." % len(anomalies))
    return 1 if anomalies else 0


if __name__ == "__main__":
    sys.exit(main())
