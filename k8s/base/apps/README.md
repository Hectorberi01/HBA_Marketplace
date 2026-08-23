# Passerelle et BFF (§4, §6).
#
# A ECRIRE. Ils ne suivent pas le gabarit de `../services/_service` pour deux
# raisons : ce sont les seuls a porter un Ingress avec TLS cert-manager, et leur
# dimensionnement suit le trafic PUBLIC (2-3 replicas des le depart, HPA sur le
# RPS) plutot que la charge interne.
#
# Ils portent aussi le label `hba.express/exposition: publique`, seul selecteur
# que la NetworkPolicy `allow-ingress-to-gateway` laisse joindre depuis
# lIngress Controller.
