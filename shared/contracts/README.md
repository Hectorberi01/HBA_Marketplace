# Contrats publics

DTO, événements d'intégration, interfaces exposées.

C'est l'héritier direct des `*.Contracts` du monolithe, qui existent déjà pour chacun des 29 modules.

**Un contrat est un engagement, pas une classe.** Le modifier casse des services qu'on ne redéploie pas en même temps — d'où l'obligation d'ajouter plutôt que de renommer, et de versionner plutôt que de supprimer.
