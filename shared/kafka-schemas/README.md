# Schémas d'événements — CE DOSSIER EST VIDE, ET IL DOIT LE RESTER

**Ne créez rien ici.** Ce qu'il annonçait existe désormais, ailleurs, et à trois
endroits qui se tiennent. En écrire un quatrième ici les ferait diverger — c'est
précisément le défaut qu'ISSUE-001 a coûté au dépôt, quand trois endroits
nommaient les sujets Kafka et qu'aucun ne se parlait.

## Où vit chacune des trois promesses de ce README

| Ce que ce dossier annonçait | Où c'est, aujourd'hui |
|---|---|
| « le format de chaque événement publié » | `docs/contrats-evenements.json` — instantané versionné des **140 événements**, comparé à chaque exécution par le contrôle `event-contracts` |
| « il leur faudra un nom stable et indépendant du langage » | l'attribut `[HbaEvent(domaine, entité, action, Version)]` et `HbaTopics` (D31) — un seul endroit dérive le nom du sujet, pour le producteur comme pour le consommateur |
| « on ajoute, on ne retire pas ; on versionne, on ne renomme pas » | la **règle additive D32**, tenue par `check-event-contracts.py` : retirer ou renommer un champ fait échouer le contrôle |

## Pourquoi le texte d'origine avait raison, et pourquoi il ne suffisait pas

Il disait :

> **UN ÉVÉNEMENT EST LU PAR DES SERVICES QU'ON NE REDÉPLOIE PAS ENSEMBLE.** Retirer
> un champ, ou en changer le sens, casse silencieusement un consommateur qu'on n'a
> pas touché.

C'est exact, et c'est toujours la règle. Mais un dossier de schémas que personne
n'a l'obligation de remplir ne protège de rien : pendant les mois où il est resté
vide, les contrats ont vécu dans le nom des classes C#, et `EventVersion` était
**codé en dur à 1** sans qu'aucun consommateur ne le lise.

Ce qui protège n'est pas le fichier de schéma : c'est le CONTRÔLE qui compare
l'existant à un instantané et refuse la régression. C'est ce qui a été fait.

## Ce dossier n'a pas été supprimé, et c'est délibéré

Le supprimer laisserait la question ouverte : le prochain qui cherche « où sont
les schémas d'événements ? » ne trouverait rien et en recréerait. Ce README est
le panneau qui l'envoie au bon endroit.
