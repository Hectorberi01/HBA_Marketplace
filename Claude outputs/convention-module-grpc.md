# Module gRPC par service — la convention, et les trois arbitrages

Même principe que Kafka : **le module porte la POLITIQUE du service, le socle partagé porte le TYPE
et le protocole.** Avec la nuance que tu poses : pour gRPC, le contrat est un fichier `.proto`
partagé, et il doit être versionné plus strictement qu'un événement.

Rien n'a été déplacé. Ce document dit ce qui existe, ce que ta forme cible implique concrètement,
et les trois points qui demandent ton arbitrage avant que je génère quoi que ce soit.

---

## 1. Ce qui existe aujourd'hui — les faits, avant l'opinion

### 1.1 Les `.proto` sont DÉJÀ partagés et versionnés

```
shared/proto/<domaine>/v1/<domaine>.proto     22 fichiers
package hba.<domaine>.v1;                     22 paquets, tous cohérents avec le chemin
```

C'est la moitié de ta nuance, et elle est déjà en place. Ce qui manque n'est pas l'emplacement,
c'est la **discipline** — §4.

### 1.2 Un projet d'assemblage par domaine, qui porte quatre choses à la fois

19 des 22 protos sont compilés par un projet `shared/contracts/HBA.<X>.Contracts.Grpc`, avec
`GrpcServices="Both"`. Chacun de ces projets contient, dans un seul assemblage :

| Ce qu'il porte | Qui s'en sert |
|---|---|
| le stub généré (client ET serveur) | les deux côtés |
| le mapping `proto` ↔ `HBA.<X>.Contracts` (records) | les deux côtés |
| **le serveur** `<X>GrpcService : <X>Api.<X>ApiBase` | le service propriétaire, uniquement |
| le client `<X>GrpcClient : I<X>ModuleApi` + `Add<X>GrpcClient()` | les consommateurs, uniquement |

**Conséquence directe :** les 10 services qui consomment `merchant.proto` lient aussi
l'implémentation du serveur de seller-service. Le csproj le dit et l'assume — « le service qui
implémente et ceux qui consomment partagent le même binaire, chacun n'utilisant que la moitié qui le
concerne ». C'est défendable pour les stubs. Ça l'est moins pour le serveur, qui dépend de
`I<X>ModuleApi` et de `Grpc.AspNetCore`.

### 1.3 Deux conventions coexistent déjà pour le serveur

| Où vit le serveur | Services |
|---|---|
| `<Service>.Api/GrpcServices/` | payment (Financial), delivery, delivery-pricing, driver, route — **5** |
| `shared/contracts/HBA.<X>.Contracts.Grpc/` | catalog, cart, food, food-cart, food-order, identity, inventory, media, seller, order, promotion, user — **12** |

17 serveurs au total, tous exposés par `app.MapInternalGrpcService<T>()`. **Ta forme cible tranche
en faveur des 5.**

### 1.4 Les clients sont câblés dans `Api`, pas dans `Infrastructure`

15 services appellent au moins un `Add<X>GrpcClient(...)`, une cinquantaine d'appels en tout.
Sur 44 références de projet vers un `*.Contracts.Grpc`, **40 partent d'un projet `.Api`** et 4
seulement d'un `.Infrastructure` (delivery-core, return-refund ×3).

Les plus gros consommateurs : order-service (7 clients), notification-service (6),
restaurant-service (5), food-order-service (4), cart-service (4), return-refund (4).

### 1.5 Le socle partagé porte déjà interceptions, autorisations et identité interne

`shared/common/HBA.Shared.Hosting/Grpc/` : `AddHbaGrpc`, `MapInternalGrpcService`,
`AutorisationsGrpc`, `IdentiteInterne`, `MontantSurLeFil`, et 4 intercepteurs — disjoncteur client,
identité interne client et serveur, traduction des erreurs serveur.

**Vérifié : les 17 enregistrements de client appellent `AjouterLesInterceptionsInternes()`.** Aucune
exception, aucun client sans disjoncteur ni identité. C'est le point le plus sain de tout le tableau.

### 1.6 Les échéances — CORRECTION D'UNE ERREUR DE CE DOCUMENT

**La première version de ce paragraphe affirmait qu'aucun client gRPC n'avait d'échéance. C'est
faux.** J'avais cherché `Deadline` dans les quelques lignes qui suivent chaque `AddGrpcClient<...>`,
et conclu de son absence là qu'elle n'existait nulle part. Elle est posée un cran plus bas :

```csharp
// InternalCallClientInterceptor, ligne 115
if (options.Deadline is null)
{
    options = options.WithDeadline(DateTime.UtcNow.AddSeconds(5));
}
```

Donc : **5 secondes par défaut, sur les 17 clients**, puisque tous appellent
`AjouterLesInterceptionsInternes()`. L'échéance fournie par l'appelant n'est pas écrasée. Le
commentaire du fichier dit pourquoi elle est là plutôt qu'aux 540 sites d'appel, et il dit aussi
ceci : « valeur de départ, à ajuster sur des mesures ».

**Ce qui manque réellement**, c'est le point d'ajustement. 5 secondes valent aujourd'hui aussi bien
pour un devis de livraison en caisse — où 5 s d'attente est déjà une commande perdue — que pour un
appel à media-service qui peut légitimement durer. Aucun service ne peut dire « pour moi, cet appel-là
c'est 800 ms ».

C'est ce réglage-là qui appartient à `Grpc/Configuration/` dans ta forme cible. La garde centrale, elle,
reste où elle est : c'est elle qui rend l'oubli impossible.

---

## 2. La forme cible, appliquée à HBAExpress

```
services/<famille>/<service>/src/
├── HBA.<X>.Api/
│   └── Grpc/
│       └── Services/
│           └── <X>GrpcService.cs        LE SERVEUR — déplacé depuis shared/contracts
│
└── HBA.<X>.Infrastructure/
    └── Grpc/
        ├── Clients/
        │   └── <Y>GrpcClient.cs         un adaptateur I<Y>ModuleApi par service appelé
        ├── Configuration/
        │   ├── DestinationsGrpc.cs      adresses + ÉCHÉANCES de CE service (§1.6)
        │   └── GardeDeCablage.cs        voir §3.3
        ├── Interceptors/                absent par défaut — voir la ligne de partage
        ├── Mappers/                     voir ARBITRAGE 1
        └── DependencyInjection.cs       AjouterClientsGrpc<X>()
```

### La ligne de partage, dossier par dossier

| Dossier | Ce qu'on y met | Ce qui RESTE dans `HBA.Shared.Hosting` | Pourquoi |
|---|---|---|---|
| `Api/Grpc/Services/` | le serveur, un fichier par service exposé | `MapInternalGrpcService`, `AutorisationsGrpc` | Le serveur est la surface du service ; la façon de le publier et de l'autoriser est identique partout. |
| `Grpc/Clients/` | l'adaptateur `I<Y>ModuleApi` → stub | le stub généré | L'adaptateur est du code de traduction, pas du transport. |
| `Grpc/Configuration/` | destinations, **surcharges d'échéance**, politique de reprise **de ce service** | `HostingOptions.GrpcPort`, `InternalCallOptions`, **l'échéance par défaut de 5 s** | Le port et la clé interne viennent du déploiement, une seule source. Le DÉFAUT reste central — c'est lui qui rend l'oubli impossible ; seule la SURCHARGE descend, parce qu'elle dépend de ce que ce service-ci attend : 800 ms pour un devis en caisse, davantage pour un import. |
| `Grpc/Interceptors/` | **rien par défaut** | disjoncteur, identité interne, traduction des erreurs | Quatre intercepteurs corrects et uniformes. Un cinquième, local, serait une seconde implémentation du même contrat — la panne de la semaine sous un autre nom. Le dossier n'est créé que par le premier service qui a un vrai besoin local. |
| `Grpc/Mappers/` | ARBITRAGE 1 | le mapping unique d'aujourd'hui | — |
| `.proto` | **rien** | `shared/proto/<domaine>/v1/` | C'est le contrat. Il ne descend pas. |

Même forme que le module Kafka, même raison : deux dossiers de l'exemple restent volontairement
vides, et c'est écrit plutôt que laissé deviner.

---

## 3. Ce que le déplacement coûte, et ce qui le paie

### 3.1 Sortir le serveur de l'assemblage partagé — le gain le plus net

Aujourd'hui, `HBA.Merchants.Contracts.Grpc` est référencé par 10 projets, dont 9 n'ont aucun usage du
serveur. Après déplacement, ces 9 ne lient plus que le stub et le client.

Effet concret sur ton objectif : **une équipe peut changer son serveur gRPC sans toucher un assemblage
que 9 autres services compilent.** C'est le même argument que pour Kafka, transposé au lien de
compilation au lieu du lien d'exécution.

### 3.2 Descendre les clients dans `Infrastructure` — un gain de forme, pas de fond

`Api` référence déjà `Infrastructure`. Déplacer le câblage ne change rien à l'exécution ; ça met le
détail d'accès au même endroit que les autres détails d'accès (EF, Redis, Kafka), et ça rend
`Program.cs` lisible : `AjouterClientsGrpc<X>()`, une ligne, comme `AjouterMessagerie<X>()`.

**Ce que ça ne donne pas** : aucune isolation supplémentaire. Qui veut vraiment empêcher `Api`
d'appeler un stub directement doit ajouter un contrôle, pas un dossier.

### 3.3 La garde de câblage n'est PAS le même problème qu'en Kafka

Pour Kafka, un module oublié = silence total, sans erreur. C'est pour ça que `GardeDeCablage` existe.

Pour gRPC, un client oublié = `I<Y>ModuleApi` non résolu = **échec de démarrage bruyant**. Le défaut
se voit tout de suite. **Sauf un cas** : chaque service porte aussi une implémentation LOCALE de son
propre `I<X>ModuleApi` (`Infrastructure/Public/<X>ModuleApi.cs`, 19 fichiers). Dans un hôte composé —
`HBA.Financial.Api` monte payments, wallet et billing — enregistrer à la fois l'implémentation locale
d'un domaine et le client gRPC du même domaine ferait gagner **le dernier enregistré**, en silence.

Vérifié : aucun service n'enregistre aujourd'hui le client gRPC de son propre domaine. La garde à
écrire n'est donc pas « le module a-t-il été appelé » mais **« un `I<X>ModuleApi` a-t-il deux
implémentations dans ce conteneur »**. C'est une garde différente, et elle vaut mieux que celle de
Kafka recopiée.

---

## 4. Les protos : ce qui manque pour parler de « strictement versionné »

Le chemin `v1` et le paquet `hba.<domaine>.v1` sont là. Le reste ne l'est pas.

| Ce qu'exige un contrat strictement versionné | État |
|---|---|
| Un emplacement unique, partagé | ✅ `shared/proto/<domaine>/v1/` |
| Un paquet cohérent avec le chemin | ✅ 22 sur 22 |
| `reserved` sur tout champ ou numéro supprimé | ❌ **3 fichiers sur 22** en portent |
| Un contrôle qui refuse une rupture de compatibilité | ❌ n'existe pas |
| Un chemin défini pour passer en `v2` | ❌ n'existe pas |
| La distinction producteur / consommateur à la compilation | ❌ `GrpcServices="Both"` partout |

**Le trou qui compte est le troisième.** Renuméroter un champ, changer son type, ou réutiliser le
numéro d'un champ supprimé ne casse pas la compilation : ça casse le **décodage**, à l'exécution, chez
l'appelant qui n'a pas été redéployé — et protobuf ne lève pas, il lit des octets mal interprétés.
C'est le même risque que le nommage canonique côté Kafka, en pire : Kafka refuse au moins de
reconnaître un type inconnu ; protobuf, lui, décode sans se plaindre.

Ce qui le fermerait, sans dépendance externe : un contrôle dans `tools/HBA.Controls` qui compare
chaque `.proto` à un **instantané figé** (`shared/proto/.instantane/`) et refuse une suppression, un
changement de type ou une réutilisation de numéro sans passage en `v2`. `buf breaking` fait ça très
bien, mais il faut un binaire de plus dans la CI ; le contrôle maison réutilise ce qui existe déjà.

Le dépôt a déjà `grpc-rpc`, qui vérifie qu'un RPC appelé a bien un corps de serveur — il avait attrapé
`DeliveryApi.LookupQuote` et `OrderApi.ListOrdersBySeller`, deux pannes d'exécution silencieuses. Le
contrôle de compatibilité est le même geste, appliqué aux champs.

---

## 5. Le poids mort trouvé en inventoriant

À trancher comme la pile B du lot 3 — je ne supprime rien sans ton accord.

| Constat | Détail |
|---|---|
| **2 assemblages référencés par personne** | `HBA.Communication.Contracts.Grpc` et `HBA.Engagement.Contracts.Grpc` : ils compilent leur proto, exposent 13 RPC, et **aucun projet ne les référence**. Ni serveur, ni client, ni enregistrement. |
| **3 protos compilés par personne** | `dispatch.proto` (4 RPC), `proof.proto` (2), `tracking.proto` (3) : aucun `<Protobuf Include>` ne les prend. Ce sont des contrats écrits pour un service qui n'existe pas — même famille que `HBA.Shipping.Contracts` côté Kafka. |
| **3 serveurs que personne n'appelle** | `user.proto`, `driver.proto`, `route.proto` : leur projet de contrats n'est référencé que par leur propre `.Api`. Le serveur tourne, le client n'existe nulle part. |

Ordre de grandeur, à confirmer en exécutant `HBA.Controls` (je n'ai pas de SDK .NET ici, donc ces
chiffres viennent d'une lecture statique et pas du contrôle lui-même) : **112 RPC déclarés**, une
douzaine implémentés sans appelant, une quarantaine ni implémentés ni appelés — dont les 13 des deux
assemblages morts et les 9 des trois protos non compilés.

---

## 5 bis. Tes décisions, et ce qu'elles coûtent

| Point | Décision | Conséquence chiffrée |
|---|---|---|
| Mapping | **un par service consommateur** | voir ci-dessous |
| Stubs | un assemblage par domaine, serveur sorti | 19 projets restent 19, et deviennent **du proto pur, sans une ligne de C# écrite à la main** |
| Poids mort | tri annoté, aucune suppression | un document, comme le lot 3 |
| Échéances | défaut central + surcharge par service | le défaut existe déjà (§1.6) ; seule la surcharge est à écrire |

**Le mapping par consommateur, en chiffres.** Il y a 50 paires (service appelant, domaine appelé).
Aujourd'hui il existe **17 traductions** — une par domaine. Après, il y en aura **50**, et la copie
mécanique représente environ **12 500 lignes** contre 2 700 aujourd'hui. Le pire cas est
`order.proto` : 7 consommateurs × ~420 lignes.

Je le dis une fois et je ne reviens pas dessus : c'est le motif exact qui a causé chacune des pannes
de ce mois-ci — deux implémentations d'un même contrat qui divergent. Mais ta décision a une version
défendable, et c'est celle que j'appliquerai : **chaque copie ne garde que les RPC et les champs que
CE service utilise réellement.** Une traduction réduite n'est plus une copie, c'est une lecture
partielle assumée — notification-service n'appelle pas les treize RPC de `merchant.proto`, il en
appelle deux.

Deux garde-fous qui viennent avec, sinon la divergence est invisible :

1. **Un en-tête sur chaque fichier de mapping** nommant le proto, sa version, et les autres services
   qui traduisent le même message. Un lecteur voit qu'il n'est pas seul.
2. **Un contrôle `grpc-mappings`** dans `tools/HBA.Controls` qui inventorie, par message proto,
   combien de traductions indépendantes existent et lesquelles lisent des champs différents. Il ne
   fait pas échouer — il rend visible. C'est le prix de la décision, et il est modeste.

---

## 6. L'ordre des lots

| Lot | Contenu | Pourquoi cet ordre |
|---|---|---|
| **A** | Surcharge d'échéance par service, dans le socle | Petit, contenu, aucun déplacement de fichier. Donne un endroit à ce qui n'en a pas. |
| **B** | Sortir les 17 serveurs vers `<Service>.Api/Grpc/Services/` | Le gain principal. Casse le lien de compilation entre un serveur et ses 9 consommateurs. |
| **C** | Descendre les 50 clients + mappings réduits vers `<Service>.Infrastructure/Grpc/` | Le gros du volume. Ne peut pas précéder B : le mapping du propriétaire doit exister avant que son serveur le cherche. |
| **D** | Les gardes : double implémentation d'`I<X>ModuleApi`, instantané de compatibilité des protos, contrôle `grpc-mappings` | Ce qui empêche la structure de se défaire. À faire après, pour contrôler l'état d'arrivée et non l'état de départ. |
| **E** | Le tri annoté du poids mort (§5) | Aucune urgence, et il demande ton arbitrage métier. |

---

## 7. Ce que cette convention ne couvre pas

- **Elle ne lit pas le fil.** Comme pour Kafka : tout ce qui précède lit du code. Rien ne vérifie
  qu'un client déployé et un serveur déployé s'entendent réellement.
- **Elle ne touche pas à l'authentification interne.** `IdentiteInterne`, la table d'autorisations et
  la clé de signature restent où elles sont, et c'est voulu.
- **Elle ne résout pas la question des échéances**, elle lui donne un point de surcharge. Choisir
  800 ms ou 2 s pour un appel donné est un arbitrage métier, service par service, et il demande des
  mesures que personne n'a prises.
- **Le §1.6 de ce document a d'abord été faux.** Je l'ai laissé visible avec sa correction plutôt que
  réécrit : la méthode qui a produit l'erreur — chercher un réglage à l'endroit où on l'écrirait
  soi-même, et conclure de son absence — est la même que celle qui a produit les 41 échecs de la
  passerelle la semaine dernière.
- **Aucun de ces déplacements n'est éprouvé par un test.** Comme pour la migration Kafka, le filet
  sera la compilation puis les 1055 tests — pas davantage.
