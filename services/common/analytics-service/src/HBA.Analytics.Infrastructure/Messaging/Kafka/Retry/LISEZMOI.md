# `Messaging/Kafka/Retry/`

Vide dans **HBA.Analytics.Infrastructure**, et ce dossier restera vide.

**Ce qui va ici :** la politique de rejeu de ce service.

**Pourquoi il n'y en a pas :** `OutboxRetryPolicy` regle la reprise d'un message
qu'on N'ARRIVE PAS A PUBLIER. Ce service ne publie rien.

**Ce qui rejoue quand meme, et qu'il ne faut pas confondre avec ceci :** la
CONSOMMATION. Un gestionnaire qui leve laisse le message non acquitte, et Kafka le
relivre — sans que rien de local ne le decide. C'est le comportement du
consommateur partage, et c'est aussi pourquoi l'inbox de ce service est
indispensable : les trois gestionnaires INCREMENTENT des compteurs, donc un rejeu
sans garde double le chiffre d'affaires d'une journee.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
