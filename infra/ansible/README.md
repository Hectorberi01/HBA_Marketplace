# Ansible — k3s sur les VMs OVH

**CE CODE N'A JAMAIS ÉTÉ EXÉCUTÉ.** Sa syntaxe YAML est vérifiée
(`scripts/check-infra.py`, lancé par `scripts/check-all.sh`), son comportement ne
l'est pas : cela demande des machines réelles. À relire avant le premier
passage, pas à appliquer de confiance.

## Ce que ça fait

| Rôle | Sur | Quoi |
|---|---|---|
| `commun` | tous | SSH sans mot de passe, nftables, swap coupé, sysctl, horloge |
| `k3s-serveur` | `serveurs` | plan de contrôle, jeton, kubeconfig rapatrié |
| `k3s-agent` | `agents` | enrôlement des nœuds de charge |

Le playbook s'arrête sur un cluster **vide**. Les opérateurs (ingress-nginx,
cert-manager, CloudNativePG, Strimzi) et les charges (`k8s/overlays/`) viennent
après — voir `docs/DEPLOIEMENT.md`.

## Séquence

```bash
cd infra/ansible

# 1. Les IP viennent de Terraform, jamais d'un relevé à la main.
cd ../terraform/environments/staging && terraform output -json noeuds && cd -
cp inventory/staging.yml.example inventory/staging.yml   # puis reporter

# 2. Collecter les empreintes SSH une fois (host_key_checking reste à True).
ssh-keyscan -H <ip1> <ip2> >> ~/.ssh/known_hosts

# 3. À blanc, puis pour de vrai.
ansible-playbook -i inventory/staging.yml playbooks/cluster.yml --check
ansible-playbook -i inventory/staging.yml playbooks/cluster.yml
```

**`--check` ment sur un cluster neuf.** Beaucoup de tâches dépendent de l'état
laissé par la précédente ; en mode simulation, l'installation de k3s n'a pas lieu,
donc le jeton n'existe pas, donc les tâches agents échouent. Un `--check` rouge sur
une machine vierge n'est pas une alerte. C'est sur un cluster **déjà installé**
qu'il devient informatif : il dit alors ce qui a dérivé depuis le dernier passage.

## Ce que ce dossier ne couvre pas

- **Le plan de contrôle n'est pas redondé** — un seul serveur k3s, même en
  production à trois nœuds. Écart connu au §24, encadré dans
  `roles/k3s-serveur/tasks/main.yml`.
- **Pas de bastion ni de VPN.** Le §19 les demande. En l'état, SSH est ouvert sur
  l'interface publique des nœuds, par clé uniquement.
- **Pas de rotation du jeton k3s** ni des clés SSH.
