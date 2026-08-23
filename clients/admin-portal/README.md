# Back-office — application d'administration

Client lourd **Avalonia 11 / .NET 9**, style neumorphique, dossier
`HBA.Admin.Desktop`. Il parle à la passerelle HBA, et à rien d'autre.

## Démarrer

```bash
export HBA_ADMIN_GATEWAY_URL=http://localhost:8080     # pile locale
dotnet run --project clients/admin-portal/HBA.Admin.Desktop
```

**Sans adresse, l'application refuse de démarrer.** Il n'y a **aucune valeur
par défaut**, et c'est délibéré : les applications Flutter du dépôt se replient
sur une adresse de staging pour ne jamais toucher la production par accident. Le
raisonnement s'inverse ici — un back-office qui viserait la staging en silence
donnerait à un administrateur l'illusion d'avoir approuvé un dossier. Il
l'apprendrait quand le vendeur rappellerait.

`appsettings.json` (clé `gateway`) sert de repli au poste de travail ;
`HBA_ADMIN_GATEWAY_URL` prime. **HTTP en clair est refusé vers un hôte distant** —
toléré sur la boucle locale seulement.

## Ce qui existe (lot A2)

| Écran | État |
|---|---|
| Connexion + second facteur | branché sur `POST /api/v1/auth/login` |
| Accueil — files d'attente | branché sur `GET /api/v1/bff/admin/queues` |

Le socle porte aussi ce que les écrans suivants utiliseront sans le réécrire :
rafraîchissement de jeton **avant** expiration, élévation de session
(`POST /api/v1/auth/reauthenticate`), et traduction des codes HTTP en phrases
lisibles.

## Trois décisions à connaître avant de toucher au code

### Aucun jeton n'est écrit sur le disque

Les consoles web et mobiles persistent leur jeton de rafraîchissement. Ici, non —
pour trois raisons, détaillées dans `Services/SessionAdmin.cs` :

1. **ce que le jeton ouvre** : une centaine de points d'entrée d'administration,
   dont les versements et la suspension de comptes ;
2. **il n'existe pas de coffre portable en .NET** : `ProtectedData` est Windows
   seulement, et « chiffrer » avec une clé rangée à côté du fichier chiffré ne
   protège de rien tout en en donnant l'apparence — cette apparence-là est pire
   que l'absence ;
3. **le coût est faible** : la session dure tant que l'application est ouverte.

### L'élévation avant geste irréversible

Le serveur distingue « connecté ce matin » de « a saisi son mot de passe il y a
moins de cinq minutes » : `StepUpAuthentication` vérifie `auth_time` et `amr`.
`SessionAdmin.ElevationValide` reproduit ce calcul côté client **avec trente
secondes de marge**, pour redemander un peu trop tôt plutôt que de faire refuser
un geste déjà cliqué.

`ClientApiAdmin.EleverAsync` pose la **nouvelle** paire de jetons rendue par le
serveur. Garder l'ancienne ferait refuser le geste suivant alors que le mot de
passe vient d'être saisi.

### La teinte n'est pas décorative

Les règles neumorphiques — ombres jumelles, source de lumière en haut-gauche,
rayon de 14 px — sont **exactement** celles de
`clients/seller-portal/Web/src/app/globals.css`, valeurs recopiées et non
approximées. Seule la teinte d'accent change : **indigo ardoise** ici, vert de
marque `#087A59` là-bas.

Un administrateur travaille avec la console vendeur ouverte à côté, pour voir ce
que voit le vendeur dont il traite le dossier. Deux écrans de même teinte, c'est
une suspension prononcée depuis la mauvaise fenêtre.

Le **rouge** est réservé aux gestes irréversibles, jamais aux erreurs de saisie :
s'il sert aussi à « e-mail invalide », il cesse d'être un signal au moment précis
où il devrait arrêter la main.

## Le projet est dans `HBA.sln`

**Conséquence : `dotnet build HBA.sln` restaure désormais Avalonia.** C'est le
prix de la seule alternative — laisser le projet hors solution, donc hors
intégration continue, donc cassable sans que rien ne le dise. Douze projets de
contrats sont déjà dans ce cas ; ce n'est pas un état à étendre.

## Ce qui reste (lots A3 et suivants)

Les quatre domaines retenus, dans l'ordre convenu :

1. **Gouvernance vendeurs & boutiques** — KYB, activation, suspension (~10 routes)
2. **Modération catalogue** — produits, marques, catégories, attributs (~25 routes)
3. **Finance & retours** — lots de règlement, versements, arbitrage (~9 routes)
4. **Exploitation livraison & food** — livreurs, tarification, restaurants (~15 routes)

Tous sont **déjà relayés** par la passerelle : ces lots sont du travail d'écran,
pas d'extraction. Chacun ajoutera son geste d'élévation sur les actions
destructrices.
