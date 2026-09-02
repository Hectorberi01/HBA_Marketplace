#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Rend `docs/api-front.html` a partir de `docs/schemas-routes-api.json`.

POURQUOI UN GENERATEUR PLUTOT QU'UNE PAGE ECRITE A LA MAIN.

Une documentation d'API tenue a la main diverge du code des la premiere
semaine, et une doc d'API fausse coute PLUS CHER qu'une doc absente : le front
la croit, code contre elle, et decouvre l'ecart en integration. Ici, tout ce que
la page affiche vient de l'analyse du depot ; regenerer la page apres un
changement de contrat est une commande, pas une relecture.

Ce que la page ne pretend pas etre : un contrat opposable. Elle decrit ce que le
code declare aujourd'hui, pas ce que le back s'engage a tenir demain, et elle ne
remplace pas OpenAPI pour la generation de clients.

Usage :
    python3 scripts/schema-routes-api.py   # produit le JSON
    python3 scripts/doc-routes-front.py    # produit la page
"""

import io
import json
import os
import sys
from html import escape

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ENTREE = os.path.join(RACINE, "docs", "schemas-routes-api.json")
SORTIE = os.path.join(RACINE, "docs", "api-front.html")

# Regroupement par produit : le front n'est pas organise par microservice, il
# est organise par application. Un developpeur de l'application livreur ne veut
# pas parcourir 60 routes de catalogue pour trouver les siennes.
FAMILLES = [
    ("Passerelle BFF", ["api-gateway"]),
    ("Identité et comptes", ["identity-service", "user-service"]),
    ("Catalogue et vendeurs", ["catalog-service", "seller-service",
                              "inventory-service", "promotion-service",
                              "review-service", "media-service"]),
    ("Panier et commandes", ["cart-service", "order-service",
                            "return-refund-service"]),
    ("Paiement", ["payment-service"]),
    ("Livraison", ["delivery-service", "delivery-pricing-service",
                   "driver-service"]),
    ("Food", ["restaurant-service", "food-cart-service",
              "food-order-service"]),
    ("Notifications", ["notification-service"]),
]


def famille_de(service):
    for nom, services in FAMILLES:
        if service in services:
            return nom
    return "Autres"


def json_html(valeur):
    """Serialise un exemple en JSON colore, sans bibliotheque.

    La coloration se fait a la serialisation et non par une passe d'expressions
    rationnelles sur le texte rendu : une chaine contenant `{` ou `:` casserait
    la seconde approche, et il y en a (les URL d'exemple).
    """
    morceaux = []

    def ecrire(v, indent):
        pad = "  " * indent
        if isinstance(v, dict):
            if not v:
                morceaux.append("{}")
                return
            morceaux.append("{\n")
            cles = list(v.keys())
            for i, k in enumerate(cles):
                morceaux.append(pad + "  ")
                morceaux.append('<span class="jk">"%s"</span>: ' % escape(k))
                ecrire(v[k], indent + 1)
                morceaux.append(",\n" if i < len(cles) - 1 else "\n")
            morceaux.append(pad + "}")
        elif isinstance(v, list):
            if not v:
                morceaux.append("[]")
                return
            morceaux.append("[\n")
            for i, e in enumerate(v):
                morceaux.append(pad + "  ")
                ecrire(e, indent + 1)
                morceaux.append(",\n" if i < len(v) - 1 else "\n")
            morceaux.append(pad + "]")
        elif isinstance(v, str):
            morceaux.append('<span class="js">"%s"</span>' % escape(v))
        elif isinstance(v, bool):
            morceaux.append('<span class="jb">%s</span>'
                            % ("true" if v else "false"))
        elif v is None:
            morceaux.append('<span class="jn">null</span>')
        else:
            morceaux.append('<span class="jnum">%s</span>' % v)

    ecrire(valeur, 0)
    return "".join(morceaux)


def bloc_route(r, base):
    methode = r["methode"]
    chemin = r["chemin"] or ""
    acces = r.get("acces") or "jeton"

    marques = []
    if r.get("enveloppe") == "BffEnvelope":
        marques.append('<span class="tag tag-bff">BFF</span>')
    elif r.get("source") == "controleur":
        marques.append('<span class="tag">passerelle</span>')
    if r.get("idempotent"):
        marques.append('<span class="tag">Idempotency-Key</span>')
    if r.get("limiteur"):
        marques.append('<span class="tag">limite de debit</span>')
    for nom in r.get("types_ambigus") or []:
        marques.append('<span class="tag tag-alerte">%s ambigu</span>'
                       % escape(nom))

    corps = []

    alias = r.get("alias_publics") or []
    if alias:
        corps.append(
            '<div class="volet"><h4>Alias publics</h4>'
            '<p class="note">La passerelle sert aussi cette route sous %s. '
            'Le chemin ci-dessus est la forme versionnée, la seule sur '
            'laquelle s’appuyer pour du code neuf.</p></div>'
            % ", ".join('<code>%s</code>' % escape(a) for a in alias))

    req = r.get("corps_requete")
    if req is not None:
        corps.append(
            '<div class="volet"><h4>Corps de la requête '
            '<span class="tname">%s</span></h4><pre class="json">%s</pre></div>'
            % (escape(r.get("type_requete") or ""), json_html(req)))

    rep = r.get("corps_reponse")
    if rep is not None:
        titre = "Réponse 200"
        env = r.get("enveloppe")
        if env == "BffEnvelope":
            titre += " — enveloppe BFF"
        elif env == "aucune":
            titre += " — sans enveloppe"
        corps.append(
            '<div class="volet"><h4>%s <span class="tname">%s</span></h4>'
            '<pre class="json">%s</pre></div>'
            % (titre, escape(r.get("type_reponse") or ""), json_html(rep)))
    else:
        corps.append(
            '<div class="volet vide"><h4>Aucun corps de réponse</h4>'
            '<p class="note">Le code ne déclare pas de type de retour pour '
            'cette route : lire le code de statut. En cas de succès, '
            '<code>data</code> vaut <code>null</code>.</p></div>')

    return (
        '<details class="route" data-q="%(q)s" data-m="%(m)s" '
        'data-s="%(svc)s" data-a="%(acces)s">'
        '<summary>'
        '<span class="verbe v-%(mlow)s">%(m)s</span>'
        '<span class="url"><span class="hote">%(base)s</span>%(chemin)s</span>'
        '<span class="marques">%(marques)s</span>'
        '<span class="acces a-%(acces)s">%(acces_txt)s</span>'
        '</summary>'
        '<div class="corps">'
        '<div class="meta-route">'
        '<span><b>Service</b> %(svc)s</span>'
        '<span><b>Gestionnaire</b> <code>%(handler)s</code></span>'
        '%(passerelle)s'
        '%(interne)s'
        '</div>'
        '%(volets)s'
        '<p class="fichier"><code>%(fichier)s</code></p>'
        '</div></details>'
    ) % {
        "q": escape("%s %s %s %s" % (methode, chemin, r.get("service") or "",
                                     r.get("gestionnaire") or "")).lower(),
        "m": methode,
        "mlow": methode.lower(),
        "svc": escape(r.get("service") or ""),
        "acces": "anonyme" if acces == "anonyme" else "jeton",
        "acces_txt": escape("public" if acces == "anonyme"
                            else acces.replace("jeton + rôle ", "rôle ")),
        "base": escape(base),
        "chemin": escape(chemin),
        "marques": "".join(marques),
        "handler": escape(r.get("gestionnaire") or ""),
        "passerelle": ('<span><b>Filtre passerelle</b> %s</span>'
                       % escape(r["politique_passerelle"])
                       if r.get("politique_passerelle") else ""),
        "interne": ('<span><b>Chemin interne</b> <code>%s</code></span>'
                    % escape(r["chemin_interne"])
                    if r.get("chemin_interne")
                    and r["chemin_interne"] != chemin else ""),
        "volets": "".join(corps),
        "fichier": escape(r.get("fichier") or ""),
    }


CSS = """
:root{
  --ground:#E7ECEC; --surface:#FFFFFF; --ink:#101819; --ink-soft:#4B5A5C;
  --rule:#C3CCCC; --rule-soft:#DCE3E3;
  --dedans:#12595E; --dedans-bg:#DCEAEA;
  --dehors:#8A5A15; --dehors-bg:#F0E5D2;
  --absent:#9B2F28; --absent-bg:#F3DEDC;
  --shadow:0 1px 0 rgba(16,24,25,.05), 0 8px 24px -18px rgba(16,24,25,.5);
}
@media (prefers-color-scheme: dark){
  :root:not([data-theme="light"]){
    --ground:#0D1416; --surface:#151E20; --ink:#E2EAEA; --ink-soft:#93A5A7;
    --rule:#2C3A3D; --rule-soft:#222E31;
    --dedans:#63BCBD; --dedans-bg:#122B2D;
    --dehors:#D5A253; --dehors-bg:#2C2417;
    --absent:#E2786F; --absent-bg:#301C1A;
    --shadow:0 1px 0 rgba(0,0,0,.3), 0 10px 28px -20px rgba(0,0,0,.9);
  }
}
:root[data-theme="dark"]{
  --ground:#0D1416; --surface:#151E20; --ink:#E2EAEA; --ink-soft:#93A5A7;
  --rule:#2C3A3D; --rule-soft:#222E31;
  --dedans:#63BCBD; --dedans-bg:#122B2D;
  --dehors:#D5A253; --dehors-bg:#2C2417;
  --absent:#E2786F; --absent-bg:#301C1A;
}
*{box-sizing:border-box}
body{
  margin:0; background:var(--ground); color:var(--ink);
  font-family:"Source Serif 4",Georgia,serif; font-size:16px; line-height:1.6;
  -webkit-font-smoothing:antialiased;
}
.page{max-width:1120px;margin:0 auto;padding:44px 20px 120px}
h1,h2,h3,h4,.eyebrow,.chip,.verbe,.tag,.acces,th,label,button,input,select{
  font-family:Chivo,"Helvetica Neue",Arial,sans-serif}
code,pre,.url,.tname{font-family:"IBM Plex Mono",ui-monospace,Menlo,monospace}
h1{font-size:clamp(28px,4.4vw,46px);line-height:1.05;font-weight:900;
   letter-spacing:-.025em;margin:0 0 12px;text-wrap:balance}
h2{font-size:20px;font-weight:700;letter-spacing:-.01em;margin:0 0 8px}
h3{font-size:13px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;
   color:var(--ink-soft);margin:34px 0 10px}
h4{font-size:12px;font-weight:700;letter-spacing:.05em;text-transform:uppercase;
   color:var(--ink-soft);margin:0 0 8px}
p{margin:0 0 12px;max-width:70ch}
a{color:var(--dedans)}
.eyebrow{font-size:11px;font-weight:700;letter-spacing:.16em;
  text-transform:uppercase;color:var(--dehors);margin:0 0 10px}
.chapeau{font-size:17px;color:var(--ink-soft);max-width:72ch;margin:0 0 26px}

.encadre{background:var(--surface);border:1px solid var(--rule);
  border-radius:10px;padding:18px 20px;margin:0 0 16px;box-shadow:var(--shadow)}
.encadre p,.encadre h2{max-width:none}
.duo{display:grid;gap:16px;grid-template-columns:1fr}
@media (min-width:900px){
  .duo{grid-template-columns:repeat(auto-fit,minmax(320px,1fr));
       align-items:start}
  .duo>.encadre{margin:0}}
.encadre.alerte{border-left:4px solid var(--dehors)}
.encadre p:last-child{margin-bottom:0}
.grille{display:grid;gap:14px;
  grid-template-columns:repeat(auto-fit,minmax(188px,1fr))}
.compteur{background:var(--surface);border:1px solid var(--rule);
  border-radius:10px;padding:14px 16px}
.compteur b{display:block;font-family:Chivo,sans-serif;font-size:30px;
  font-weight:900;line-height:1.1;letter-spacing:-.02em;color:var(--dedans)}
.compteur span{font-size:13px;color:var(--ink-soft)}

.barre{position:sticky;top:0;z-index:5;background:var(--ground);
  padding:14px 0 12px;border-bottom:1px solid var(--rule);margin:0 0 8px;
  display:flex;gap:10px;flex-wrap:wrap;align-items:center}
.barre input,.barre select{font-size:14px;padding:9px 12px;border-radius:8px;
  border:1px solid var(--rule);background:var(--surface);color:var(--ink)}
.barre input{flex:1 1 260px;min-width:200px}
.compte{font-size:13px;color:var(--ink-soft);margin-left:auto}

details.route{background:var(--surface);border:1px solid var(--rule-soft);
  border-radius:9px;margin:0 0 6px;overflow:hidden}
details.route[open]{border-color:var(--rule);box-shadow:var(--shadow)}
details.route summary{list-style:none;cursor:pointer;padding:11px 14px;
  display:flex;gap:11px;align-items:center;flex-wrap:wrap}
details.route summary::-webkit-details-marker{display:none}
details.route summary:hover{background:var(--dedans-bg)}
.verbe{font-size:11px;font-weight:900;letter-spacing:.05em;padding:3px 8px;
  border-radius:5px;min-width:62px;text-align:center;color:#fff}
.v-get{background:#12595E}.v-post{background:#8A5A15}
.v-put{background:#3F5A80}.v-patch{background:#5B4A7A}
.v-delete{background:#9B2F28}
.url{font-size:13.5px;flex:1 1 340px;word-break:break-all}
.hote{color:var(--ink-soft)}
.acces{font-size:11px;font-weight:700;padding:2px 8px;border-radius:20px;
  border:1px solid var(--rule)}
.a-anonyme{color:var(--dehors);background:var(--dehors-bg);border-color:transparent}
.a-jeton{color:var(--dedans);background:var(--dedans-bg);border-color:transparent}
.tag{font-size:10px;font-weight:700;letter-spacing:.04em;padding:2px 7px;
  border-radius:4px;background:var(--rule-soft);color:var(--ink-soft);
  margin-right:4px}
.tag-bff{background:var(--dehors-bg);color:var(--dehors)}
.tag-alerte{background:var(--absent-bg);color:var(--absent)}
.corps{padding:2px 14px 16px;border-top:1px solid var(--rule-soft)}
.meta-route{display:flex;gap:18px;flex-wrap:wrap;font-size:13px;
  color:var(--ink-soft);margin:12px 0 14px}
.meta-route b{font-family:Chivo,sans-serif;font-size:11px;letter-spacing:.05em;
  text-transform:uppercase;margin-right:5px}
.volet{margin:0 0 14px}
.volet.vide h4{color:var(--dehors)}
.note{font-size:13.5px;color:var(--ink-soft);margin:0}
.tname{font-family:"IBM Plex Mono",monospace;font-size:11px;
  text-transform:none;letter-spacing:0;color:var(--dedans)}
pre.json{background:var(--ground);border:1px solid var(--rule-soft);
  border-radius:7px;padding:12px 14px;margin:0;overflow-x:auto;
  font-size:12.5px;line-height:1.55;white-space:pre}
.jk{color:var(--dedans);font-weight:600}
.js{color:var(--dehors)}
.jnum{color:#3F5A80}
.jb{color:#5B4A7A;font-weight:600}
.jn{color:var(--ink-soft);font-style:italic}
@media (prefers-color-scheme: dark){
  :root:not([data-theme="light"]) .jnum{color:#8FB0DA}
  :root:not([data-theme="light"]) .jb{color:#B49BD8}
}
.fichier{font-size:11.5px;color:var(--ink-soft);margin:10px 0 0}
.fichier code{font-size:11.5px}
.famille{margin:30px 0 0}
.famille>h2{display:flex;align-items:baseline;gap:10px}
.famille>h2 span{font-family:"IBM Plex Mono",monospace;font-size:12px;
  font-weight:400;color:var(--ink-soft)}
table{border-collapse:collapse;width:100%;font-size:13.5px;margin:0 0 10px}
th,td{text-align:left;padding:7px 10px;border-bottom:1px solid var(--rule-soft)}
th{font-size:11px;letter-spacing:.05em;text-transform:uppercase;
   color:var(--ink-soft)}
td code{font-size:12.5px}
.pied{margin-top:56px;padding-top:18px;border-top:1px solid var(--rule);
  font-size:13px;color:var(--ink-soft)}
"""

JS = """
(function(){
  var q=document.getElementById('q'),
      fm=document.getElementById('fm'),
      fs=document.getElementById('fs'),
      fa=document.getElementById('fa'),
      compte=document.getElementById('compte'),
      routes=Array.prototype.slice.call(
        document.querySelectorAll('details.route')),
      familles=Array.prototype.slice.call(
        document.querySelectorAll('.famille'));

  function filtrer(){
    var t=q.value.trim().toLowerCase(),
        m=fm.value, s=fs.value, a=fa.value, n=0;
    routes.forEach(function(r){
      var ok = (!t || r.dataset.q.indexOf(t)>=0)
            && (!m || r.dataset.m===m)
            && (!s || r.dataset.s===s)
            && (!a || r.dataset.a===a);
      r.hidden = !ok;
      if(ok) n++;
    });
    familles.forEach(function(f){
      f.hidden = !f.querySelector('details.route:not([hidden])');
    });
    compte.textContent = n + ' route' + (n>1?'s':'') + ' affich\u00e9e'
                       + (n>1?'s':'');
  }
  [q,fm,fs,fa].forEach(function(e){
    e.addEventListener('input',filtrer); e.addEventListener('change',filtrer);
  });
  document.getElementById('tout').addEventListener('click',function(){
    var ouvrir = this.dataset.etat !== 'ouvert';
    routes.forEach(function(r){ if(!r.hidden) r.open = ouvrir; });
    this.dataset.etat = ouvrir ? 'ouvert' : 'ferme';
    this.textContent = ouvrir ? 'Tout replier' : 'Tout d\u00e9plier';
  });
  filtrer();
})();
"""


def main():
    if not os.path.exists(ENTREE):
        print("introuvable : %s — lancer d'abord scripts/schema-routes-api.py"
              % os.path.relpath(ENTREE, RACINE), file=sys.stderr)
        return 1

    d = json.load(io.open(ENTREE, encoding="utf-8"))
    base = d.get("base_publique") or ""
    routes = d["routes"]

    total = len(routes)
    avec = sum(1 for r in routes if r.get("corps_reponse") is not None)
    sans = total - avec
    publiques = sum(1 for r in routes if r.get("acces") == "anonyme")
    avec_alias = sum(1 for r in routes if r.get("alias_publics"))
    services = sorted({r["service"] for r in routes})

    groupes = {}
    for r in routes:
        groupes.setdefault(famille_de(r["service"]), []).append(r)

    ordre = [nom for nom, _ in FAMILLES if nom in groupes]
    if "Autres" in groupes:
        ordre.append("Autres")

    sections = []
    for nom in ordre:
        lot = sorted(groupes[nom], key=lambda r: (r["service"], r["chemin"],
                                                  r["methode"]))
        blocs = "".join(bloc_route(r, base) for r in lot)
        sections.append(
            '<section class="famille"><h2>%s <span>%d routes</span></h2>%s'
            '</section>' % (escape(nom), len(lot), blocs))

    options_services = "".join(
        '<option value="%s">%s</option>' % (escape(s), escape(s))
        for s in services)

    non_resolus = d.get("types_non_resolus") or []
    liste_nr = "".join("<li><code>%s</code></li>" % escape(n)
                       for n in non_resolus)

    html = """<title>Routes API HBA Express</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Chivo:wght@400;600;700;900&family=IBM+Plex+Mono:wght@400;500;600&family=Source+Serif+4:opsz,wght@8..60,400;8..60,600&display=swap">
<style>%(css)s</style>
<div class="page">

<p class="eyebrow">HBA Express — contrat HTTP</p>
<h1>Toutes les routes, et le JSON qu’elles rendent</h1>
<p class="chapeau">Cette page est <b>générée depuis le code</b> :
chaque chemin, chaque type de retour et chaque exemple JSON vient de l’analyse
du dépôt, pas d’une saisie manuelle. Elle sert à coder le front sans lire le
C#.</p>

<div class="grille">
  <div class="compteur"><b>%(total)d</b><span>routes exposées publiquement</span></div>
  <div class="compteur"><b>%(avec)d</b><span>avec un corps de réponse typé</span></div>
  <div class="compteur"><b>%(sans)d</b><span>sans corps déclaré</span></div>
  <div class="compteur"><b>%(publiques)d</b><span>accessibles sans jeton</span></div>
  <div class="compteur"><b>%(alias)d</b><span>ont un alias public hérité</span></div>
</div>

<h3>Ce qu’il faut savoir avant le premier appel</h3>

<div class="duo">
<div class="encadre">
  <h2>Base</h2>
  <p>Toutes les URL de cette page sont complètes :
  <code>%(base)s</code> y est déjà préfixé. Il n’y a qu’une seule entrée
  publique — la passerelle. Aucun service n’est joignable directement.</p>
</div>

<div class="encadre">
  <h2>Pagination : deux formes, elles aussi</h2>
  <p>Les routes de service paginent en <code>{items, total, page, pageSize,
  facets}</code>. Les routes BFF paginent en <code>{items, page, pageSize,
  totalCount}</code>, où <code>totalCount</code> peut valoir
  <code>null</code> — les services amont ne rendent pas de total, et le
  calculer côté passerelle coûterait une requête de comptage par page.
  <code>null</code> signifie « inconnu » : afficher un bouton « voir plus »
  plutôt qu’un nombre de pages.</p>
</div>
</div>

<div class="encadre alerte">
  <h2>Il y a DEUX enveloppes, et elles ne se ressemblent pas</h2>
  <p>Les routes de service rendent <code>ApiEnvelope</code> :
  <code>success</code>, <code>data</code>, <code>error</code>,
  <code>meta</code>. Les routes de la passerelle marquées <b>BFF</b> rendent
  <code>BffEnvelope</code> : <code>data</code> et <code>warnings</code>,
  sans <code>success</code> ni <code>meta</code>.</p>
  <p>Un client qui teste <code>success</code> sur une réponse BFF lit
  <code>undefined</code> et conclut à l’échec sur une réponse valide. Chaque
  bloc de cette page affiche l’enveloppe réellement émise par la route
  concernée : lire celle qui est montrée, pas celle d’à côté.</p>
  <p><code>warnings</code> est <b>vide, jamais nul</b> : quand une dépendance
  est dégradée, le BFF rend quand même l’écran et dépose un code dans ce
  tableau. Il est donc sûr d’itérer dessus sans test de nullité.</p>
</div>

<div class="duo">
<div class="encadre">
  <h2>Accès : c’est la pastille qui engage</h2>
  <p>La pastille à droite de chaque route dit ce que le <b>service</b> exige :
  <code>public</code>, <code>jeton</code>, ou un rôle précis
  (<code>rôle Seller</code>, <code>rôle Admin</code>…). C’est cette exigence
  qui décide si l’appel passe.</p>
  <p>Le <b>filtre passerelle</b> affiché dans le détail est autre chose : un
  premier tri, plus grossier, appliqué avant le routage.
  <code>anonymous</code> à cet endroit ne veut pas dire que la route est
  ouverte — il veut dire que la passerelle laisse passer et que le service
  tranche. Lire la pastille, pas le filtre.</p>
</div>

<div class="encadre">
  <h2>Nommage et types</h2>
  <table>
    <tr><th>Sujet</th><th>Règle</th></tr>
    <tr><td>Casse des champs</td><td><code>camelCase</code> — la plateforme
      n’installe aucune politique explicite, elle hérite donc du défaut web
      d’ASP.NET</td></tr>
    <tr><td><code>Guid</code></td><td>chaîne UUID</td></tr>
    <tr><td><code>DateTime</code> / <code>DateTimeOffset</code></td>
      <td>chaîne ISO 8601 en UTC</td></tr>
    <tr><td><code>decimal</code></td><td>nombre JSON, jamais une chaîne</td></tr>
    <tr><td>Énumérations</td><td>rendues en chaîne</td></tr>
    <tr><td>Champs optionnels</td><td>présents avec la valeur
      <code>null</code></td></tr>
  </table>
  <p class="note">Les valeurs des exemples sont des <b>marqueurs de type</b>
  (<code>"texte"</code>, <code>0</code>, un UUID fixe), pas des données
  réelles. Ce qui compte est le nom des champs et leur forme.</p>
</div>

<div class="encadre">
  <h2>Ce que cette page ne sait pas</h2>
  <p>%(sans)d routes ne déclarent aucun type de retour dans le code : elles
  sont listées, avec la mention « aucun corps ». Cela ne veut pas dire qu’elles
  ne rendent rien — cela veut dire que le code ne le dit pas, et il faut
  demander au back.</p>
  %(bloc_nr)s
  <p>Les codes d’erreur ne sont pas documentés ici : le champ
  <code>error</code> de l’enveloppe les porte, mais aucune route ne déclare
  la liste de ceux qu’elle peut émettre. Cette page ne l’inventera pas.</p>
</div>
</div>

<h3>Les routes</h3>

<div class="barre">
  <input id="q" type="search" placeholder="Chercher un chemin, un service, un gestionnaire…">
  <select id="fm">
    <option value="">Toutes méthodes</option>
    <option>GET</option><option>POST</option><option>PUT</option>
    <option>PATCH</option><option>DELETE</option>
  </select>
  <select id="fs"><option value="">Tous services</option>%(services)s</select>
  <select id="fa">
    <option value="">Tous accès</option>
    <option value="anonyme">Sans jeton</option>
    <option value="jeton">Jeton requis</option>
  </select>
  <button id="tout" type="button" data-etat="ferme">Tout déplier</button>
  <span class="compte" id="compte"></span>
</div>

%(sections)s

<p class="pied">Page générée par <code>scripts/doc-routes-front.py</code> à
partir de <code>docs/schemas-routes-api.json</code>. Pour la mettre à jour
après un changement de contrat :
<code>python3 scripts/lister-routes-api.py &amp;&amp;
python3 scripts/schema-routes-api.py &amp;&amp;
python3 scripts/doc-routes-front.py</code></p>

</div>
<script>%(js)s</script>
""" % {
        "css": CSS,
        "js": JS,
        "total": total,
        "avec": avec,
        "sans": sans,
        "publiques": publiques,
        "alias": avec_alias,
        "base": escape(base),
        "services": options_services,
        "sections": "".join(sections),
        "bloc_nr": ("<p>Types que l’analyseur n’a pas su résoudre — ils "
                    "apparaissent tels quels dans les exemples plutôt que "
                    "d’être inventés :</p><ul>%s</ul>" % liste_nr)
                   if liste_nr else "",
    }

    io.open(SORTIE, "w", encoding="utf-8").write(html)
    print("%d routes, %d avec corps, %d sans" % (total, avec, sans))
    print("ecrit : %s (%.0f Ko)"
          % (os.path.relpath(SORTIE, RACINE), len(html) / 1024.0))
    return 0


if __name__ == "__main__":
    sys.exit(main())
