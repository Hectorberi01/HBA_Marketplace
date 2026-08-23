# Brique tierce : installee par Helm, pas reecrite en Kustomize.
#
# Un operateur Kafka (Strimzi) ou un chart communautaire apporte StatefulSets,
# sondes, rolling restart et gestion des topics. Les recopier ici serait reecrire
# le travail de leurs auteurs, et le maintenir a chaque montee de version.
#
# Ce qui viendra dans ce dossier : les Topic/KafkaUser propres a HBA, qui eux ne
# sont pas generiques (§9 : replication 3, min.insync.replicas 2, DLQ par famille).
