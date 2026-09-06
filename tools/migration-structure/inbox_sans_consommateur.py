# -*- coding: utf-8 -*-
"""
HUIT SERVICES ONT RECU UNE INBOX QU'ILS N'UTILISENT PAS — ET QUI LEVE.

CE QUE MON LOT INBOX A FAIT DE TROP. Le generateur a copie l'inbox dans les 24
services, sans demander si le service CONSOMME quelque chose. Huit n'ont aucun
`IIntegrationEventHandler<>` et aucun consommateur : billing, recommendations,
reviews, wishlist, delivery-pricing, driver, inventory, return-refund.

CE QUE CA PRODUIT, ET CE N'EST PAS LATENT :

  1. `InboxMessageConfiguration` mappe `consumer_inbox` dans le modele EF.
     Aucune migration de ces huit services ne cree cette table — verifie.
  2. `OutboxRegistration` y enregistre `AddHostedService<InboxCleanupService>()`.
     Ce service de fond interroge la table A CHAQUE TICK.

Donc huit services journaliseraient `relation "consumer_inbox" does not exist`
en boucle, indefiniment, sans que rien ne tombe — le genre de panne qu'on ne
regarde plus au bout d'une semaine. Et le prochain `dotnet ef migrations add`
creerait huit tables dont personne n'a besoin.

C'EST LE CONTROLE `migrations` QUI L'A VU, ET LUI SEUL. Ni la compilation, ni
les tests, ni mon propre passage statique : tous ne regardent que du code, et
le code est parfaitement correct. Il manquait une table.

CE QUE CE SCRIPT NE FAIT PAS : il ne touche pas aux seize services qui
consomment vraiment. Et il ne cree aucune migration — il retire ce qui n'aurait
pas du etre pose.
"""
import io, os, re, shutil

RAC = os.path.expanduser("~/mnt/HBA")

SANS_CONSOMMATEUR = [
    "common/billing-service",
    "common/recommendation-service",
    "common/review-service",
    "common/wishlist-service",
    "delivery/delivery-pricing-service",
    "delivery/driver-service",
    "marketplace/inventory-service",
    "marketplace/return-refund-service",
]

LISEZMOI = """# `Persistence/Inbox/`

Vide dans **{projet}** : ce service ne consomme AUCUN evenement d'integration.
Verifie — zero `IIntegrationEventHandler<>`, zero consommateur Kafka.

**Ce qui va ici :** `InboxMessage`, sa configuration EF, le depot et la purge —
le jour ou ce service consommera quelque chose. Il faudra alors AUSSI une
migration qui cree `consumer_inbox` : c'est precisement ce qui manquait ici.

**Ou ca vit aujourd'hui :** dans les services qui consomment, chacun dans son
propre `Persistence/Inbox/`. Seul le port `IConsumerInbox` reste au socle, parce
que le dispatcher partage verifie l'idempotence avant CHAQUE gestionnaire.

**POURQUOI CE DOSSIER A ETE VIDE.** Le lot inbox l'avait rempli dans les 24
services sans demander si le service consomme quelque chose. Resultat : une
table mappee que nulle migration ne cree, et un `InboxCleanupService` qui
l'interrogeait a chaque tick. Le controle `migrations` l'a vu ; ni la
compilation ni les tests ne le pouvaient — le code etait correct, c'est la table
qui manquait.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
"""


def main():
    retires = 0
    for service in SANS_CONSOMMATEUR:
        base = os.path.join(RAC, "services", service, "src")
        infra = next(os.path.join(base, d) for d in os.listdir(base)
                     if d.endswith(".Infrastructure"))
        projet = os.path.basename(infra)

        # ── les fichiers de l'inbox
        dossier = os.path.join(infra, "Persistence", "Inbox")
        n = len([f for f in os.listdir(dossier) if f.endswith(".cs")])
        shutil.rmtree(dossier)
        os.makedirs(dossier)
        io.open(os.path.join(dossier, "LISEZMOI.md"), "w", encoding="utf-8").write(
            LISEZMOI.format(projet=projet))
        retires += n

        # ── l'enregistrement du service de fond
        for racine, _, fs in os.walk(infra):
            for f in fs:
                if not f.endswith(".cs"):
                    continue
                p = os.path.join(racine, f)
                s = io.open(p, encoding="utf-8").read()
                if "AddHostedService<InboxCleanupService>" not in s:
                    continue
                neuf = re.sub(
                    r"\n *// LA PURGE DE L'INBOX[^\n]*\n *services\.AddHostedService<InboxCleanupService>\(\);\n",
                    "\n        // PAS DE PURGE D'INBOX : ce service ne consomme rien, il n'a donc\n"
                    "        // pas de table `consumer_inbox`. L'y enregistrer faisait interroger\n"
                    "        // a chaque tick une table qu'aucune migration ne cree.\n",
                    s)
                if neuf == s:
                    neuf = s.replace(
                        "        services.AddHostedService<InboxCleanupService>();\n", "")
                io.open(p, "w", encoding="utf-8").write(neuf)

        print("%-40s -%d fichier(s)" % (projet, n))

    print("\n%d fichier(s) d'inbox retires dans %d services."
          % (retires, len(SANS_CONSOMMATEUR)))


main()
