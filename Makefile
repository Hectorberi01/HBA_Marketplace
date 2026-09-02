# ═════════════════════════════════════════════════════════════════════════════
# HBAExpress — raccourcis de developpement.
#
# Ce fichier ne remplace pas les scripts de `scripts/` : il leur donne un nom
# court et stable. Quand une commande change, elle change dans le script, pas
# dans la memoire de chacun.
# ═════════════════════════════════════════════════════════════════════════════

COMPOSE := docker compose -f docker-compose.dev.yml

.DEFAULT_GOAL := help
.PHONY: help restore build test up down logs ps clean check migrate migrations seed infra k8s-dev k8s-check

help: ## Affiche cette aide
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) \
		| awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-12s\033[0m %s\n", $$1, $$2}'

restore: ## Restaure les paquets NuGet de la solution
	dotnet restore HBA.sln

build: ## Compile toute la solution
	dotnet build HBA.sln --configuration Debug

# LES TESTS EXIGEANT DOCKER SONT EXCLUS D'ICI, ET C'EST DELIBERE.
#
# `HBA.Catalog.IntegrationTests` demarre un vrai PostgreSQL et un vrai Kafka par
# Testcontainers. Sans Docker en marche, ils echouent tous ensemble au demarrage
# de la fixture — et c'est toute la suite qui devient rouge, y compris les 599
# tests rapides qui n'ont besoin de rien.
#
# Le risque n'est pas l'echec : c'est qu'on cesse de lancer `make test` parce
# qu'il ne passe jamais sur un poste sans Docker. Une suite qu'on ne lance plus
# ne protege plus rien.
#
# LE FILTRE PORTE SUR UN TRAIT, PAS SUR LE NOM DU PROJET.
#
# Une premiere version excluait `FullyQualifiedName~IntegrationTests`. Elle
# ecartait AUSSI `HBA.Gateway.IntegrationTests`, qui n'a besoin d'aucun
# conteneur : une trentaine de tests disparaissaient de `make test` sans que le
# compteur ne dise lesquels. Le trait, lui, dit ce qu'il veut dire — « cette
# classe a besoin de Docker » — et n'attrape que celles qui le portent.
#
# Corollaire assume : une nouvelle classe Testcontainers qui oublie le trait
# tourne dans `make test` et echoue bruyamment sur un poste sans Docker. C'est le
# bon sens de l'erreur — l'oubli se signale, au lieu de sauter en silence.
test: ## Execute les tests rapides (sans Docker)
	dotnet test HBA.sln --configuration Debug --no-build \
		--filter "Docker!=true"

test-integration: ## Execute les tests exigeant Docker
	dotnet test HBA.sln --configuration Debug --no-build \
		--filter "Docker=true"

up: ## Demarre l'environnement local complet
	$(COMPOSE) up -d

down: ## Arrete l'environnement local
	$(COMPOSE) down

logs: ## Suit les journaux (make logs S=identity-service)
	$(COMPOSE) logs -f $(S)

ps: ## Etat des conteneurs
	$(COMPOSE) ps

check: ## Lance les controles du depot (DI, migrations, Dockerfiles, gRPC)
	./scripts/check-all.sh

k8s-dev: ## Rend les manifests de l'environnement dev
	kustomize build k8s/overlays/dev

k8s-check: ## Construit les trois overlays et verifie le cahier Infrastructure
	dotnet run --project tools/HBA.Controls --verbosity quiet -- k8s

migrations: ## Genere les migrations EF manquantes (hors ligne, sans base)
	./scripts/db/add-missing-migrations.sh

# CETTE CIBLE NE FAISAIT RIEN, ET ELLE LE FAISAIT EN SILENCE.
#
# Elle appelait `scripts/db/migrate.sh`, qui n'existe pas, puis se rabattait sur
# `dev-up.sh --migrate`, option que le script refuse (code 2). Un `make migrate`
# echouait donc toujours — et l'on en concluait que les migrations avaient un
# probleme, alors qu'il n'y avait pas de commande.
#
# En LOCAL il n'y a rien a lancer : `Database:MigrateOnStartup` n'est vrai qu'en
# Development, les services migrent au demarrage. Hors Development, la migration
# est une etape de release (§15) — voir docs/DEPLOIEMENT.md.
migrate: ## Rappelle comment les migrations s'appliquent selon l'environnement
	@echo "Local        : rien a lancer — les services migrent au demarrage."
	@echo "               Base neuve : ./scripts/dev-up.sh --fresh"
	@echo "dev/staging  : etape de release, pas un effet de bord du demarrage."
	@echo "               Voir docs/DEPLOIEMENT.md, etages 2 et 3."
	@echo "Generer les migrations manquantes : make migrations"

infra: ## Verifie Terraform et Ansible (syntaxe et cablage, sans fournisseur)
	dotnet run --project tools/HBA.Controls --verbosity quiet -- infra

seed: ## Injecte les donnees de demonstration
	./scripts/seed-accounts.sh && ./scripts/seed-stores.sh && ./scripts/seed-catalog-categories.sh

clean: ## Supprime les artefacts de compilation
	find . -type d \( -name bin -o -name obj \) -not -path "*/node_modules/*" -prune -exec rm -rf {} +
