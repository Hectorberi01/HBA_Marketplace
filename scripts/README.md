# Outillage

Scripts de développement et de migration.

## Build local

Utiliser `./scripts/build-hba.sh` pour vérifier la solution HBA. Le script force
`/m:1` parce que le SDK .NET 9.0.315 peut rester bloqué pendant la résolution des
références projet quand `dotnet build HBA.sln` utilise le parallélisme par défaut.

## Pile locale

`./scripts/dev-up.sh` construit les quatorze images **une par une**, puis démarre.

`docker compose up --build` les construit en parallèle : quatorze SDK .NET qui
restaurent et compilent en même temps dépassent la mémoire allouée à Docker sur
une machine de développement. Le noyau tue alors un processus et BuildKit rend
`ResourceExhausted: cannot allocate memory` — une erreur qui ne désigne aucun
fichier, tombe sur un service différent à chaque essai, et fait chercher un
problème de code là où il n'y en a pas.

Construire séquentiellement ramène le pic de mémoire à celui d'une seule image.

- `--fresh` supprime d'abord les volumes. Nécessaire dès qu'on touche à
  `postgres/001-create-databases.sql` : `docker-entrypoint-initdb.d` est ignoré
  si le volume contient déjà des données.
- `--build-only` s'arrête après la construction.

Les quatorze Dockerfiles passent par ailleurs `/m:1 --disable-build-servers
-p:UseSharedCompilation=false` : sans ces brides, MSBuild lance un processus de
compilation par cœur et laisse un serveur Roslyn résident.

À prévoir : génération des clients à partir des `.proto` et des schémas Kafka,
amorçage des bases locales, extraction assistée d'un module vers un service.

**Le script le plus utile n'est pas un générateur, c'est un vérificateur** : celui
qui détecte une jointure SQL entre deux schémas. Tant que la base est partagée, rien
n'empêche physiquement un module de lire la table d'un autre — et c'est ce qui rend
une extraction impossible le jour venu.
