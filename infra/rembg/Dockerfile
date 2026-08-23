# ═══════════════════════════════════════════════════════════════════════════════
# SERVEUR DE DÉTOURAGE — construit sur place, et non tiré du registre.
#
# POURQUOI ON NE TIRE PAS `danielgatis/rembg:latest`.
#
# Cette image n'est publiée QUE pour linux/amd64 : sur un Mac Apple Silicon,
# `docker compose up` échoue avec « no matching manifest for linux/arm64/v8 ».
#
# La rustine évidente — `platform: linux/amd64` — fonctionne mais fait tourner
# l'inférence sous émulation QEMU. Or u2net est du calcul matriciel dense : c'est
# précisément la charge que l'émulation dégrade le plus, avec un facteur 5 à 20.
# Un détourage de deux secondes en devient trente, le délai de 120 s se met à
# expirer, et l'échec ressemble à une panne du modèle alors qu'il s'agit de
# l'architecture.
#
# `onnxruntime` publie des roues manylinux aarch64 : construire ici donne donc du
# code NATIF, sur les deux architectures, sans image tierce à faire confiance.
#
# CE SERVEUR N'A NI AUTHENTIFICATION NI LIMITATION DE DÉBIT.
#
# Il ne doit jamais être publié : aucun `ports:` dans compose, aucune route de
# passerelle. Seul catalog-service l'appelle, par le réseau interne. Voir
# l'encadré de `RembgOptions`.
#
# LICENCE DU MODÈLE : u2net est publié en usage NON COMMERCIAL. Acceptable en
# développement ; à trancher avant la mise en ligne — soit un modèle sous licence
# permissive, soit `CloudinaryImageProcessor`, qui est déjà écrit.
# ═══════════════════════════════════════════════════════════════════════════════
FROM python:3.11-slim

# `libgl1` ET `libglib2.0-0` SONT REQUIS, MÊME SANS ÉCRAN.
#
# rembg dépend d'OpenCV, qui se lie à ces bibliothèques au chargement du module —
# pas à l'affichage. Sans elles, l'import échoue sur
# « libGL.so.1: cannot open shared object file » et le serveur ne démarre jamais,
# ce qui est peu évocateur pour un service sans interface graphique.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgl1 libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/*

# `[cli]` apporte le sous-commande `s` (serve) et son serveur HTTP. Sans cet
# extra, `rembg` s'installe en bibliothèque seule et `rembg s` reste inconnu.
RUN pip install --no-cache-dir "rembg[cli]"

# LE MODÈLE N'EST PAS EMBARQUÉ DANS L'IMAGE, À DESSEIN.
#
# Le télécharger ici ajouterait 176 Mo à chaque reconstruction de l'image, y
# compris quand seule une dépendance change. Il est récupéré au premier appel et
# conservé dans le volume `rembg-models` monté sur ce répertoire — d'où la
# lenteur du tout premier détourage, et de lui seul.
ENV U2NET_HOME=/root/.u2net
EXPOSE 7000

CMD ["rembg", "s", "--host", "0.0.0.0", "--port", "7000"]
