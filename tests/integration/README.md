# Tests d'intégration

Un service contre ses vraies dépendances : base, broker, stockage objet — en conteneurs jetables.

C'est le niveau qui remplace ce qu'un test unitaire garantissait gratuitement dans le monolithe : qu'un événement publié est bien reçu.

---

## Où ils vivent

**Pas dans ce dossier.** Ils vivent à côté des autres suites, sous
`tests/HBA.<Service>.IntegrationTests/`, parce que c'est là que la solution les
trouve et que `dotnet test` les découvre. Ce README reste ici pour la raison
d'être ; le code est avec ses voisins.

Premier service couvert : **`tests/HBA.Catalog.IntegrationTests/`**.

## Comment on les lance

```
make test               # les tests rapides — Docker n'est pas requis
make test-integration   # ceux-ci — un Docker en marche est requis
```

**La séparation n'est pas cosmétique.** Sans Docker, une fixture
Testcontainers échoue au démarrage et emporte toute la suite avec elle, y compris
les six cents tests rapides qui n'ont besoin de rien. Le risque n'est pas
l'échec : c'est qu'on cesse de lancer `make test` parce qu'il ne passe jamais — et
une suite qu'on ne lance plus ne protège plus rien.

Le filtre porte sur le trait `Docker`, pas sur le nom du projet : une première
version excluait tout ce qui contenait « IntegrationTests », ce qui écartait
aussi les tests de la passerelle, qui ne démarrent aucun conteneur.

## Ce que le premier lot couvre

| Test | Ce qu'il attrape, et que rien d'autre n'attrape |
|---|---|
| Démarrage sur base neuve | Le **départ à froid** des migrations, jamais rejoué jusqu'ici. `check-migrations.py` le simule en lisant les fichiers ; ici il s'exécute vraiment. |
| Forme de l'enveloppe §25 | Un `Results.Ok` oublié à la migration du lot 6 : il compile, rend une réponse d'apparence correcte, et le client lit des champs nuls. |
| Ancien préfixe en 404 | Que la coquille de dépréciation reste à la **passerelle**. La remonter dans le service donnerait deux endroits qui servent la même surface. |
| Rôle vendeur | La même règle que la suite sans base, mais avec TOUT monté — un filtre ajouté plus tard peut changer l'ordre du pipeline. |
| `/swagger/v1/swagger.json` sans jeton | L'ordre du pipeline : placée après `UseAuthorization`, la page rendrait 401 avant d'avoir pu servir le bouton qui permet de s'authentifier. |
| Inbox §19.5 | Qu'un événement publié est **consommé**, et qu'un rejeu **ne l'est pas deux fois**. Les gestionnaires du catalogue étant naturellement idempotents, la garde n'est observable que par la ligne qu'elle écrit. |

## Ce qui reste à couvrir

- Le parcours §28 complet en écriture (créer → soumettre → approuver → publier).
  Il demande de substituer `ISellerModuleApi`, que la garde d'appartenance
  interroge avant de toucher la base.
- Le refus **404 sur la fiche d'autrui** — deux vendeurs en base. C'est le trou
  que `OfferAndVariantGuardTests` note explicitement comme hors de sa portée.
- Les autres services. La fixture du catalogue est le gabarit : elle ne contient
  rien qui lui soit propre, hormis le nom du service et ses adresses de voisins.
