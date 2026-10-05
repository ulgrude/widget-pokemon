# 🎮 Widget Pokémon

Un petit widget flottant qui fait apparaître un Pokémon sur les pages de ton choix (nouvel onglet, Discord, etc.). Capture-le avec tes Poké Balls, remplis ton Pokédex et partage la même seed avec tes amis pour voir les mêmes Pokémon au même moment.

> Il s'agit d'un **userscript** : il s'installe via une extension de navigateur comme Tampermonkey ou Violentmonkey.

## ✨ Fonctionnalités

- **Un nouveau Pokémon toutes les 30 minutes**, parmi les 1025 espèces.
- **Seed partagée** : toutes les personnes qui utilisent la même seed voient le même Pokémon au même moment.
- **Chromatiques** : chaque apparition a 1 chance sur 64 d'être shiny ✨.
- **Trois types de balls**, avec un pourcentage de réussite affiché en direct :
  | Ball | Bonus de capture |
  |---|---|
  | Poké Ball | ×1 |
  | Super Ball | ×1,5 |
  | Hyper Ball | ×2 |
- **Gain de balls** :
  - 20 Poké Balls au départ, puis +1 Poké Ball par heure (maximum 100 par type).
  - Chaque heure, chaque capture de ton Pokédex a 5 % de chance de te rapporter une ball bonus. Le type dépend du taux de capture de l'espèce : les Pokémon difficiles à attraper donnent de meilleures balls.
  - Les balls s'accumulent même si tu n'ouvres jamais les pages où le widget s'affiche.
- **Pokédex** 📖 avec recherche par nom ou numéro (insensible aux accents).
- **Mode réduit** : le widget peut se replier en simple Poké Ball. Elle se balance quand un nouveau Pokémon apparaît et brille en doré quand c'est un chromatique.
- **Noms en français** (récupérés via PokéAPI).
- **Export / import** de ta progression en fichier `.json` (balls, Pokédex, réglages).
- **Mises à jour** : vérification automatique une fois par jour, avec un lien d'installation quand une nouvelle version est disponible.
- **Synchronisation entre onglets** : ta progression est partagée entre tous les sites où le script tourne.

## 📦 Installation

### 1. Installer un gestionnaire de userscripts

Choisis l'une de ces extensions :

- [Tampermonkey](https://www.tampermonkey.net/) (Chrome, Edge, Firefox, Safari…)
- [Violentmonkey](https://violentmonkey.github.io/) (Chrome, Edge, Firefox)

> Sur les versions récentes de Chrome et Edge, il peut être nécessaire d'activer le mode développeur ou l'option « Autoriser les scripts utilisateur » dans les paramètres de l'extension pour que les userscripts fonctionnent. Consulte la documentation de ton gestionnaire si le script ne se lance pas.

### 2. Installer le script

**👉 [Clique ici pour installer Widget Pokémon](https://raw.githubusercontent.com/ulgrude/widget-pokemon/main/widget-pokemon.user.js)**

Ton gestionnaire de userscripts ouvre une page de confirmation : clique sur **Installer**.

### 3. C'est prêt

Par défaut, le widget s'affiche sur :

- `chrome://newtab/*`
- `https://discord.com/channels/*`

Tu peux modifier cette liste dans les réglages (voir ci-dessous).

## 🕹️ Utilisation

Le widget affiche le Pokémon du moment, son taux de capture et le temps restant pour l'attraper.

1. Clique sur une ball pour la lancer. Le pourcentage affiché dessous est ta chance de capture.
2. Chaque lancer consomme une ball, qu'il réussisse ou non. Si c'est raté, tu peux réessayer tant qu'il te reste des balls et du temps.
3. Un seul Pokémon peut être capturé par apparition. Une fois attrapé, il s'estompe jusqu'au suivant.

| Bouton | Action |
|---|---|
| `?` | Aide : règles du jeu |
| 📖 | Pokédex |
| ⚙️ | Réglages |
| `−` | Réduire le widget en Poké Ball |

Le Pokédex, les réglages et l'aide sont aussi accessibles depuis le **menu de l'extension** (Tampermonkey / Violentmonkey), même sur une page où le widget n'est pas affiché.

## ⚙️ Réglages

- **Seed** : change-la pour jouer avec tes amis. Même seed = mêmes Pokémon aux mêmes moments.
- **Position** : haut gauche, haut droite, bas gauche ou bas droite.
- **Pages où le widget apparaît** : liste d'URL avec `*` comme joker (ex. `https://discord.com/channels/*`).
- **Sauvegarde** : exporte ou importe ta progression au format `.json`. Pense à exporter avant de vider les données du navigateur ou de réinstaller l'extension.
- **Mises à jour** : bouton pour vérifier manuellement la dernière version.

> ⚠️ **À propos de `chrome://newtab/*`** : Chrome bloque les extensions sur ses pages internes. Cette entrée ne fonctionne en général que si ta page de nouvel onglet est une vraie page web (par exemple via une extension qui remplace le nouvel onglet).

## 🔄 Mises à jour

Le script vérifie automatiquement une fois par jour s'il existe une nouvelle version. Si c'est le cas, un lien **« Mise à jour disponible »** apparaît dans le widget : clique dessus et ton gestionnaire de userscripts te proposera de mettre à jour. Tu peux aussi laisser ton gestionnaire gérer les mises à jour lui-même.

## 🔒 Données et vie privée

- Ta progression est stockée **localement** dans ton gestionnaire de userscripts (`GM_setValue`). Rien n'est envoyé à un serveur tiers.
- Le script contacte uniquement :
  - [PokéAPI](https://pokeapi.co/) pour les noms et taux de capture ;
  - `raw.githubusercontent.com` pour les sprites ([PokeAPI/sprites](https://github.com/PokeAPI/sprites)) et la vérification des mises à jour.
- Les sprites sont mis en cache localement (environ 4 Mo maximum) pour accélérer l'affichage.
- Le script est chargé sur toutes les pages (pour faire avancer le compteur de balls), mais **n'affiche le widget que sur les pages de ta liste**.

## 🙏 Crédits

- Données : [PokéAPI](https://pokeapi.co/)
- Sprites : [PokeAPI/sprites](https://github.com/PokeAPI/sprites)
- Pokémon et tous les noms associés sont des marques de Nintendo, Game Freak et The Pokémon Company. Ce projet est un projet de fan, sans lien officiel avec eux.

---

Fait par [Ulgrude](https://github.com/ulgrude)
