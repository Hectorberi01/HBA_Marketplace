# `common/` — ce que plusieurs domaines appellent

| Service | Rôle | Pourquoi ici |
|---|---|---|
| `identity-service` | Authentification, JWT, rôles | Tout le monde s'authentifie |
| `user-service` | Profils, adresses, KYC | Un client achète ET commande à manger |
| `financial-service` | Paiements, portefeuille, règlements, commissions | Le diagramme place *Payment* et *Wallet* en commun |
| `communication-service` | Notifications push, SMS, e-mail, messagerie | Appelé par les quatre domaines |
| `media-service` | Dépôt de fichiers, images, URL signées | Photos produit ET photos de plats |
| `commerce-service` | Panier, promotions, coupons, fidélité | Le panier porte marchandise ET repas |
| `order-service` | Commandes, retours, litiges | Une ligne porte sa nature : expédier ou cuisiner |
| `engagement-service` | Avis, recommandations, signaux | Les avis portent sur produits ET restaurants |

**`common/` n'est pas un fourre-tout.** Le critère est vérifiable : au moins deux
domaines appellent le service. Un service qui n'en sert qu'un doit rejoindre ce
domaine, même si son nom sonne technique — sinon `common/` finit par contenir tout
ce qu'on n'a pas su classer, et le groupement ne dit plus rien.
