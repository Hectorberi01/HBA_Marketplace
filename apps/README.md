# HBA Gateway

## 1. Présentation

`HBA Gateway` constitue le point d'entrée principal des APIs de la plateforme HBA.

La plateforme regroupe plusieurs domaines fonctionnels :

- HBAExpress : marketplace e-commerce ;
- HBA Food : commande et livraison de repas ;
- HBA Delivery : service de livraison ;
- applications vendeurs et restaurants ;
- application livreurs ;
- interfaces d'administration.

Les applications clientes ne doivent pas communiquer directement avec les microservices internes.

Le trafic suit principalement le chemin :

```text
Internet
   │
   ▼
Traefik
   │
   ▼
HBA Gateway
   │
   ├── Identity Service
   ├── User Service
   ├── Merchant Service
   ├── Catalog Service
   ├── Inventory Service
   ├── Commerce Service
   ├── Order Service
   ├── Food Service
   ├── Delivery Service
   ├── Financial Service
   ├── Engagement Service
   ├── Communication Service
   └── Media Service
```

Le Gateway permet ainsi de masquer l'architecture interne de la plateforme aux applications clientes.

---

# 2. Responsabilités

Le Gateway est responsable des fonctionnalités transversales liées à l'exposition des APIs.

Ses principales responsabilités sont :

### Reverse Proxy

Le Gateway route les requêtes vers le microservice approprié.

Le préfixe `/api/{domaine}` est **conservé** jusqu'au service :

```text
GET /api/catalog/products
   │
   ▼
Gateway
   │
   ▼
Catalog Service
   │
   ▼
GET /api/catalog/products
```

> **Corrigé.** Ce paragraphe décrivait auparavant une suppression du préfixe
> (`→ GET /products`). Le code du monolithe — source de tous les services à
> extraire — sert en réalité `/api/catalog/products`, `/api/inventory/items`,
> `/api/food/restaurants`, `/api/orders`, `/api/cart`. Supprimer le préfixe
> aurait imposé de réécrire les routes de chaque service extrait, et de le tester
> avec des URL différentes de celles qu'il expose.

**Deux exceptions**, déclarées explicitement en `Transforms` dans
`appsettings.json` :

| Route publique      | Chemin interne             | Motif                                             |
|---------------------|----------------------------|---------------------------------------------------|
| `/api/auth/*`       | `/api/identity/auth/*`     | le monolithe expose `MapGroup("/api/identity/auth")` |
| `/api/merchants/*`  | `/api/sellers/*`           | le module s'appelle `Sellers`, le produit « marchand » |

Toute nouvelle divergence doit être ajoutée à ce tableau **et** aux `Transforms`.

### Authentification

Le Gateway vérifie les tokens d'accès avant de permettre l'accès aux routes protégées.

Le token est délivré par :

```text
Identity Service
```

Le Gateway ne crée pas lui-même les utilisateurs et ne stocke pas leurs mots de passe.

### Autorisation

Certaines routes peuvent être limitées selon :

- utilisateur ;
- vendeur ;
- restaurant ;
- livreur ;
- administrateur ;
- permissions spécifiques.

### Routage

Le Gateway détermine le service responsable de chaque requête.

### BFF — Backend For Frontend

Le Gateway peut agréger plusieurs services pour produire une réponse adaptée à une application particulière.

Exemple :

```text
GET /api/bff/client/express/home
```

peut récupérer des informations depuis :

```text
Catalog Service
Food Service
Engagement Service
Order Service
```

et retourner une réponse unique à l'application mobile.

### Correlation ID

Chaque requête reçoit un identifiant permettant de suivre son parcours à travers plusieurs services.

Exemple :

```text
X-Correlation-ID: c188fb9e-...
```

Cet identifiant doit être propagé vers les microservices.

### Rate Limiting

Le Gateway protège les APIs contre :

- les abus ;
- les appels excessifs ;
- certaines attaques automatisées.

### Observabilité

Les métriques, traces et logs sont envoyés vers la stack d'observabilité HBA.

---

# 3. Ce que le Gateway ne doit pas faire

Le Gateway ne doit pas devenir un microservice métier géant.

Il ne doit notamment pas gérer directement :

```text
Produits
Commandes
Stocks
Restaurants
Paiements
Livreurs
Wallets
Promotions
```

Ces responsabilités appartiennent aux microservices.

Par exemple, ceci est interdit :

```text
Gateway
   ↓
UPDATE Orders
```

Le Gateway doit appeler :

```text
Gateway
   ↓
Order Service
   ↓
Database Order
```

Le Gateway ne doit donc avoir aucun accès direct aux bases PostgreSQL des microservices.

---

# 4. Architecture

L'architecture réseau cible est :

```text
                    INTERNET
                       │
                       ▼
                 ┌───────────┐
                 │ Traefik   │
                 └─────┬─────┘
                       │
                    HTTPS
                       │
                       ▼
              ┌─────────────────┐
              │   HBA Gateway   │
              │                 │
              │      YARP       │
              │       +         │
              │      BFF        │
              └────────┬────────┘
                       │
                  hba-backend
                       │
      ┌────────────────┼────────────────┐
      │                │                │
      ▼                ▼                ▼
 Identity          Catalog           Order
      │                │                │
      ▼                ▼                ▼
   User            Commerce           Food
      │                │                │
      ▼                ▼                ▼
 Merchant         Inventory         Delivery
      │                │                │
      └────────────────┼────────────────┘
                       │
                       ▼
               Financial / Media
```

---

# 5. Traefik vs Gateway

Traefik et HBA Gateway ont deux responsabilités différentes.

## Traefik

Traefik appartient à l'infrastructure.

Il gère principalement :

```text
HTTP → HTTPS
TLS
Let's Encrypt
Reverse proxy externe
Headers de sécurité
Rate limiting réseau
```

## HBA Gateway

Le Gateway appartient à la couche applicative.

Il gère :

```text
Routing API
JWT
Authorization
BFF
Correlation ID
Transformation de requêtes
Agrégation
Rate limiting applicatif
Observabilité
```

Le flux est donc :

```text
Client
  ↓
Traefik
  ↓
Gateway
  ↓
Microservices
```

---

# 6. Technologies

Le Gateway utilise :

```text
ASP.NET Core
YARP
JWT Bearer Authentication
OpenTelemetry
Docker
Traefik
```

YARP signifie :

```text
Yet Another Reverse Proxy
```

Il est utilisé comme moteur de reverse proxy applicatif.

---

# 7. Structure du projet

```text
gateway/
│
├── traefik/
│   │
│   ├── traefik.yml
│   │
│   ├── dynamic/
│   │   ├── middlewares.yml
│   │   ├── security.yml
│   │   └── routes.yml
│   │
│   └── acme/
│       └── .gitkeep
│
├── bff/
│   │
│   ├── src/
│   │   │
│   │   ├── HBA.Gateway.Api/
│   │   │   ├── Controllers/
│   │   │   ├── Middlewares/
│   │   │   ├── Extensions/
│   │   │   ├── appsettings.json
│   │   │   └── Program.cs
│   │   │
│   │   ├── HBA.Gateway.Application/
│   │   │   ├── Mobile/
│   │   │   ├── Merchant/
│   │   │   ├── Driver/
│   │   │   ├── Admin/
│   │   │   ├── DTOs/
│   │   │   └── Interfaces/
│   │   │
│   │   └── HBA.Gateway.Infrastructure/
│   │       ├── HttpClients/
│   │       ├── Authentication/
│   │       ├── ReverseProxy/
│   │       └── DependencyInjection.cs
│   │
│   ├── tests/
│   ├── Dockerfile
│   └── HBA.Gateway.sln
│
└── README.md
```

---

# 8. Projets .NET

## HBA.Gateway.Api

Point d'entrée HTTP.

Il contient :

- configuration ASP.NET Core ;
- controllers BFF ;
- middlewares ;
- health checks ;
- configuration YARP ;
- authentication ;
- endpoints.

Il ne doit pas contenir de logique métier complexe.

---

## HBA.Gateway.Application

Contient les cas d'utilisation spécifiques au Gateway.

Principalement les agrégations BFF.

Exemple :

```text
Mobile/
   GetHomePage/
      GetHomePageQuery.cs
      GetHomePageHandler.cs
      HomePageDto.cs
```

Le handler peut appeler plusieurs clients internes :

```text
CatalogClient
FoodClient
OrderClient
EngagementClient
```

---

## HBA.Gateway.Infrastructure

Contient les implémentations techniques.

Exemple :

```text
Infrastructure/
├── HttpClients/
│   ├── CatalogClient.cs
│   ├── FoodClient.cs
│   ├── OrderClient.cs
│   └── DeliveryClient.cs
│
├── Authentication/
│
├── ReverseProxy/
│
└── DependencyInjection.cs
```

---

# 9. Routes publiques

Convention générale :

```text
/api/{domain}/...
```

Exemples :

| Route | Destination |
|---|---|
| `/api/auth/*` | Identity Service |
| `/api/users/*` | User Service |
| `/api/merchants/*` | Merchant Service |
| `/api/catalog/*` | Catalog Service |
| `/api/inventory/*` | Inventory Service |
| `/api/cart/*` | Commerce Service |
| `/api/wishlist/*` | Commerce Service |
| `/api/orders/*` | Order Service |
| `/api/food/*` | Food Service |
| `/api/delivery/*` | Delivery Service |
| `/api/payments/*` | Financial Service |
| `/api/wallet/*` | Financial Service |
| `/api/reviews/*` | Engagement Service |
| `/api/notifications/*` | Communication Service |
| `/api/media/*` | Media Service |

---

# 10. Routes BFF

Les routes BFF sont différentes des routes proxy.

Elles sont conçues pour les applications HBA.

```text
/api/bff/client/express/*
/api/bff/client/food/*
/api/bff/merchant/*
/api/bff/restaurant/*
/api/bff/driver/*
/api/bff/admin/*
```

> **Corrigé.** Ce paragraphe indiquait `/api/mobile/*`, `/api/merchant/*`…
> Le segment `bff` sépare l'agrégation du proxy et évite une collision durable :
> `/api/merchant/*` (BFF) et `/api/merchants/*` (proxy vers merchant-service) ne
> se distinguaient que par un « s ».

Par exemple :

```text
GET /api/bff/client/express/home
```

peut déclencher :

```text
                    Gateway
                       │
       ┌───────────────┼────────────────┐
       │               │                │
       ▼               ▼                ▼
    Catalog           Food         Engagement
       │               │                │
       └───────────────┼────────────────┘
                       │
                       ▼
                  HomePageDto
                       │
                       ▼
                 Application
```

L'application mobile effectue donc un seul appel.

---

# 11. Séparation HBAExpress / HBA Food

Le Gateway doit conserver une séparation claire entre les domaines.

Marketplace :

```text
/api/catalog
/api/cart
/api/wishlist
```

Food :

```text
/api/food/restaurants
/api/food/menus
/api/food/orders
```

Les routes BFF peuvent également être séparées :

```text
/api/bff/client/express/home
/api/bff/client/food/home
```

Cela permet à l'application cliente d'afficher deux expériences différentes tout en utilisant le même backend HBA.

---

# 12. Configuration YARP

Exemple :

```json
{
  "ReverseProxy": {
    "Routes": {
      "identity": {
        "ClusterId": "identity",
        "Match": {
          "Path": "/api/auth/{**catch-all}"
        }
      },

      "catalog": {
        "ClusterId": "catalog",
        "Match": {
          "Path": "/api/catalog/{**catch-all}"
        }
      },

      "orders": {
        "ClusterId": "orders",
        "Match": {
          "Path": "/api/orders/{**catch-all}"
        }
      }
    },

    "Clusters": {
      "identity": {
        "Destinations": {
          "primary": {
            "Address": "http://identity-service:8080/"
          }
        }
      },

      "catalog": {
        "Destinations": {
          "primary": {
            "Address": "http://catalog-service:8080/"
          }
        }
      },

      "orders": {
        "Destinations": {
          "primary": {
            "Address": "http://order-service:8080/"
          }
        }
      }
    }
  }
}
```

Les noms Docker sont utilisés comme DNS interne :

```text
identity-service
catalog-service
order-service
food-service
delivery-service
```

Ne jamais utiliser :

```text
localhost
```

pour communiquer avec un autre conteneur.

---

# 13. Authentification

Le flux d'authentification est :

```text
Mobile
   │
   │ POST /api/auth/login
   ▼
Gateway
   │
   ▼
Identity Service
   │
   ▼
JWT + Refresh Token
   │
   ▼
Mobile
```

Puis :

```text
GET /api/orders
Authorization: Bearer eyJ...
```

Le Gateway valide le JWT avant de transmettre la requête.

---

# 14. Routes publiques et protégées

Certaines routes doivent rester publiques.

Exemples :

```text
POST /api/auth/login
POST /api/auth/register

GET /api/catalog/products
GET /api/catalog/categories

GET /api/food/restaurants
GET /api/food/restaurants/{id}/menu
```

D'autres nécessitent une authentification :

```text
GET /api/users/me

POST /api/cart/items

POST /api/orders

GET /api/orders/me

POST /api/delivery/requests

GET /api/wallet
```

Les règles finales d'autorisation restent définies selon les besoins métier de chaque service.

---

# 15. Propagation du token

Lorsqu'une requête arrive avec :

```text
Authorization: Bearer <token>
```

le token doit être transmis au service cible lorsque celui-ci doit connaître l'utilisateur.

Exemple :

```text
Mobile
  │
  │ JWT
  ▼
Gateway
  │
  │ JWT
  ▼
Order Service
```

Le microservice doit également effectuer les contrôles nécessaires sur les opérations sensibles.

Le Gateway n'est donc pas l'unique frontière de sécurité.

---

# 16. Correlation ID

Chaque requête doit disposer d'un :

```text
X-Correlation-ID
```

Si le client n'en fournit pas, le Gateway en génère un.

Exemple :

```text
Mobile
   ↓
Gateway
CorrelationId = ABC123
   ↓
Order
ABC123
   ↓
Financial
ABC123
   ↓
Kafka
ABC123
```

Cela permet de retrouver toute une transaction dans les logs distribués.

---

# 17. Gestion des erreurs

Le Gateway doit retourner des erreurs cohérentes.

Format recommandé :

```json
{
  "type": "https://api.hba-express.com/errors/not-found",
  "title": "Resource not found",
  "status": 404,
  "detail": "The requested resource could not be found.",
  "traceId": "00-...",
  "correlationId": "..."
}
```

Utiliser le format :

```text
ProblemDetails
```

d'ASP.NET Core.

Le Gateway ne doit jamais exposer :

- stack traces ;
- connection strings ;
- mots de passe ;
- secrets ;
- détails internes Docker ;
- exceptions SQL.

---

# 18. Timeouts

Les appels interservices doivent avoir des timeouts.

Exemple :

```text
Gateway
   ↓
Catalog
```

ne doit pas attendre indéfiniment.

Une politique raisonnable peut être :

```text
Lecture simple       2–5 secondes
Agrégation BFF       5–10 secondes
Opération complexe   selon le workflow
```

Les valeurs doivent être ajustées avec les métriques réelles.

---

# 19. Résilience

Les appels HTTP internes doivent être conçus pour gérer :

```text
timeout
service indisponible
erreur réseau
HTTP 5xx
```

Les retries ne doivent pas être utilisés aveuglément.

Une opération :

```text
GET
```

peut généralement être retentée plus facilement qu'une opération :

```text
POST /payments
```

Pour les opérations financières et les créations de commandes, utiliser notamment des mécanismes d'idempotence.

---

# 20. Rate Limiting

Deux niveaux existent.

### Traefik

Protection générale :

```text
Internet
   ↓
Rate Limit Traefik
```

### Gateway

Protection applicative plus précise :

```text
/api/auth/login
/api/auth/otp
/api/search
/api/orders
```

Par exemple, les endpoints OTP/login peuvent avoir des limites beaucoup plus strictes que la consultation du catalogue.

---

# 21. Health Check

Le Gateway expose les trois :

```text
GET /health          processus vivant (conservé : les healthchecks compose l'utilisent)
GET /health/live     processus vivant
GET /health/ready    prêt à recevoir du trafic
```

`ready` ne contacte **aucun microservice**. Faire dépendre l'aptitude de la
passerelle de la santé des services amont transformerait le redémarrage d'un
composant secondaire — les avis, les notifications — en indisponibilité totale
de l'API, l'orchestrateur sortant la passerelle de la rotation. Il vérifie que
la configuration de routage est chargée et que chaque cluster a une destination.

avec :

```text
live
```

pour vérifier le processus,

et :

```text
ready
```

pour vérifier si le Gateway est prêt à accepter du trafic.

---

# 22. Observabilité

Le Gateway est instrumenté avec OpenTelemetry.

Il envoie sa télémétrie vers :

```text
otel-collector:4317
```

Le système d'observabilité HBA utilise :

```text
OpenTelemetry
      │
      ├── Prometheus
      ├── Loki
      └── Grafana
```

Les métriques importantes comprennent :

```text
nombre de requêtes
latence
HTTP 4xx
HTTP 5xx
timeouts
requêtes par route
requêtes par service
```

---

# 23. Docker

Le Gateway est exécuté dans Docker.

Il appartient à deux réseaux :

```text
hba-proxy
hba-backend
```

Architecture :

```text
Traefik
   │
hba-proxy
   │
Gateway
   │
hba-backend
   │
Microservices
```

Les microservices métier ne doivent normalement pas appartenir au réseau `hba-proxy`.

---

# 24. Variables d'environnement

Exemple :

```env
SERVICE_NAME=gateway

ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://+:8080

Services__Identity=http://identity-service:8080
Services__User=http://user-service:8080
Services__Merchant=http://merchant-service:8080
Services__Catalog=http://catalog-service:8080
Services__Inventory=http://inventory-service:8080
Services__Commerce=http://commerce-service:8080
Services__Order=http://order-service:8080
Services__Food=http://food-service:8080
Services__Delivery=http://delivery-service:8080
Services__Financial=http://financial-service:8080
Services__Engagement=http://engagement-service:8080
Services__Communication=http://communication-service:8080
Services__Media=http://media-service:8080

OpenTelemetry__Endpoint=http://otel-collector:4317
```

Les secrets ne doivent jamais être versionnés.

---

# 25. Développement local

Depuis :

```bash
infra/docker
```

démarrer l'infrastructure :

```bash
docker compose up -d
```

Afficher les conteneurs :

```bash
docker compose ps
```

Afficher les logs du Gateway :

```bash
docker compose logs -f gateway
```

Redémarrer uniquement le Gateway :

```bash
docker compose restart gateway
```

---

# 26. Tester le Gateway

Health check :

```bash
curl http://localhost:8080/health
```

En environnement exposé via Traefik :

```bash
curl https://api.hba-express.com/health
```

Tester le catalogue :

```bash
curl https://api.hba-express.com/api/catalog/products
```

Tester une route authentifiée :

```bash
curl \
  -H "Authorization: Bearer <TOKEN>" \
  https://api.hba-express.com/api/orders/me
```

---

# 27. Sécurité

Les règles suivantes sont obligatoires :

1. aucun secret dans Git ;
2. aucun accès direct du Gateway aux bases métier ;
3. HTTPS obligatoire en production ;
4. validation JWT ;
5. rate limiting ;
6. validation des entrées ;
7. headers de sécurité ;
8. logs sans mots de passe/tokens ;
9. timeouts sur les appels HTTP ;
10. propagation du Correlation ID ;
11. permissions contrôlées côté service ;
12. endpoints internes non exposés publiquement.

---

# 28. Communication synchrone et asynchrone

Le Gateway utilise principalement HTTP pour communiquer avec les services.

```text
Gateway
   │ HTTP
   ▼
Catalog
```

Kafka ne doit pas remplacer les appels HTTP lorsque le client attend immédiatement une réponse.

Kafka est principalement utilisé entre microservices pour les événements :

```text
Order Service
     │
     │ order.created
     ▼
    Kafka
     │
     ├── Inventory
     ├── Financial
     ├── Analytics
     └── Communication
```

Le Gateway n'a généralement pas besoin de participer directement à ces workflows événementiels.

---

# 29. Règle d'architecture fondamentale

Les applications clientes connaissent :

```text
api.hba-express.com
```

mais elles ne connaissent jamais :

```text
identity-service:8080
catalog-service:8080
order-service:8080
food-service:8080
```

Ces adresses appartiennent au réseau privé HBA.

Ainsi :

```text
Flutter
React
Web Admin
Merchant App
Driver App
        │
        ▼
 api.hba-express.com
        │
        ▼
     Gateway
        │
        ▼
  Microservices
```

---

# 30. Évolution future

Lorsque la plateforme grandira, plusieurs Gateway/BFF pourront être séparés :

```text
                    Traefik
                       │
          ┌────────────┼─────────────┐
          ▼            ▼             ▼
      Client BFF   Merchant BFF   Driver BFF
          │            │             │
          └────────────┼─────────────┘
                       ▼
                  Microservices
```

Au lancement, un seul Gateway est préférable afin de réduire la complexité opérationnelle.

Les frontières BFF doivent néanmoins être conservées dans le code pour faciliter cette extraction future.

---

# 31. Résumé

Le HBA Gateway constitue la frontière HTTP principale de la plateforme.

```text
                ┌─────────────────────┐
                │       CLIENTS       │
                │                     │
                │ Mobile / Web / Pro  │
                │ Driver / Admin      │
                └──────────┬──────────┘
                           │
                           ▼
                     ┌───────────┐
                     │ Traefik   │
                     │ TLS       │
                     └─────┬─────┘
                           │
                           ▼
                  ┌────────────────┐
                  │  HBA Gateway   │
                  │                │
                  │ YARP           │
                  │ Auth           │
                  │ BFF            │
                  │ Rate Limit     │
                  │ Observability  │
                  └───────┬────────┘
                          │
              ┌───────────┼────────────┐
              │           │            │
              ▼           ▼            ▼
           Commerce      Food       Delivery
              │           │            │
        ┌─────┼─────┐     │       ┌────┼────┐
        ▼     ▼     ▼     ▼       ▼    ▼    ▼
     Catalog Cart Order Restaurant Driver Tracking
```

Le principe central est :

> **Le Gateway expose et orchestre les APIs ; les microservices restent propriétaires de leur logique métier et de leurs données.**
