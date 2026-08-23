# Bibliothèques partagées

Utilitaires, extensions, primitives sans métier.

**C'est ici que la dérive commence.** `Result<T>`, `Error`, les primitives d'agrégat viennent de `../../../../src/BuildingBlocks/` et ont leur place. Une règle de calcul de commission n'en a aucune : elle appartient au service financier, et l'y dupliquer signifie que deux services factureront différemment le jour où l'un des deux évoluera.
