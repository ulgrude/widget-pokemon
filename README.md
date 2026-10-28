# Poké-widget

Un petit widget qui fait apparaître un Pokémon toutes les 30 minutes. Capture-le avec tes Poké Balls, remplis ton Pokédex et partage la même seed avec tes amis pour voir les mêmes Pokémon au même moment.

Il existe en **deux versions**, avec quelques différences selon la plateforme :

- 🖥️ **Version bureau (Windows)** : une application qui s'installe en un clic et affiche le widget sur ton bureau.
- 🌐 **Version navigateur** : un *userscript* à installer avec Tampermonkey ou Violentmonkey, qui affiche le widget sur les pages web de ton choix (nouvel onglet, Discord, etc.).

## Quelle version choisir ?

| | 🖥️ Bureau (Windows) | 🌐 Navigateur (userscript) |
|---|---|---|
| Où s'affiche le widget | Sur le bureau, sous tes fenêtres | Sur les pages web de ta liste |
| Installation | Un installateur à télécharger | Une extension (Tampermonkey…) + le script |
| Mises à jour | Installation en un clic depuis le widget | Via ton gestionnaire de userscripts |
| Sauvegarde | Fichier chiffré (`.pokesave`) | Stockage du gestionnaire de userscripts |
| Systèmes | Windows 10 / 11 (64 bits) | Tout navigateur compatible |

## Fonctionnalités

- **Un nouveau Pokémon toutes les 30 minutes**, parmi les 1025 espèces.
- **Seed partagée** : toutes les personnes qui utilisent la même seed voient la même espèce au même moment, y compris entre la version bureau et la version navigateur.
- **Chromatiques personnalisés** : chaque apparition a 1 chance sur 64 d'être shiny ✨, déterminée par l'identifiant unique de la progression. Les apparitions chromatiques diffèrent donc d'un joueur à l'autre.
- **Trois types de balls**, avec un pourcentage de réussite affiché en direct :
  | Ball | Bonus de capture |
  |---|---|
  | Poké Ball | ×1 |
  | Super Ball | ×1,5 |
  | Hyper Ball | ×2 |
- **Gain de balls** :
  - 20 Poké Balls au départ, puis +1 Poké Ball par heure (maximum 100 par type).
  - Chaque heure, chaque capture de ton Pokédex a 5 % de chance de te rapporter une ball bonus. Le type dépend du taux de capture de l'espèce : les Pokémon difficiles à attraper donnent de meilleures balls.
  - Le temps compte même quand tu n'utilises pas le widget : les balls se rattrapent dès qu'il se relance.
- **Pokédex** 📖 avec recherche par nom ou numéro (insensible aux accents).
- **Mode réduit** : le widget se replie en simple Poké Ball. Elle se balance quand un nouveau Pokémon apparaît et brille en doré quand c'est un chromatique.
- **Noms en français** (récupérés via PokéAPI).

## Comment jouer

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

---

# Version bureau (Windows)

## Installation

1. Ouvre la [page des versions](https://github.com/ulgrude/widget-pokemon/releases/latest).
2. Télécharge **`PokeWidget-win-Setup.exe`** et lance-le. Il s'installe dans ton dossier utilisateur, **sans droits administrateur**, et crée des raccourcis (menu Démarrer et bureau).
3. Le widget se lance et apparaît en bas à droite de ton écran.

> ⚠️ **Avertissement Windows SmartScreen** : l'application n'est pas signée avec un certificat payant, donc Windows peut afficher « Windows a protégé votre ordinateur » au premier lancement. Clique sur **Informations complémentaires**, puis sur **Exécuter quand même**. Le code est ouvert dans ce dépôt, tu peux le consulter ou le compiler toi-même (voir plus bas).

**Version portable** : tu peux dézipper le fichier `PokeWidget-win-Portable.zip` n'importe où et lancer `PokeWidget.exe` sans rien installer. Les mises à jour sont alors manuelles : télécharge la nouvelle version depuis la page des versions.

Pour **désinstaller**, utilise « Applications installées » dans les paramètres de Windows. Ta progression est conservée (voir « Données et vie privée » plus bas).

## Utilisation

- Le widget se place **au-dessus des icônes du bureau et sous tes autres fenêtres**. Il n'apparaît pas dans la barre des tâches ni dans Alt+Tab.
- **Win+D** (afficher le bureau) réduit le widget, et il revient dès qu'une fenêtre s'ouvre.
- **Clic droit** sur le widget : réduire / agrandir, Pokédex, réglages, aide, mode d'affichage, position, quitter.
- **Icône dans la zone de notification** (près de l'horloge) : clic gauche pour afficher le widget, clic droit pour le menu.
- Une seule instance tourne à la fois.

## Réglages

Ouvre-les avec le bouton ⚙️ ou depuis le menu de l'icône de la zone de notification.

- **Seed** : change-la pour jouer avec tes amis. Même seed = mêmes Pokémon aux mêmes moments. La seed par défaut est `youtube.com/@golbfr` parce que j'ai bien le droit de faire ma pub, héhé.
- **Position** : haut gauche, haut droite, bas gauche ou bas droite.
- **Affichage** :
  - *Sur le bureau* : le widget reste sous tes fenêtres.
  - *Toujours au-dessus* : le widget reste visible par-dessus tout le reste.
- **Lancer le widget au démarrage de Windows** : recommandé, pour que tes balls se rattrapent et que tu ne rates aucun Pokémon.
- **Mises à jour** : vérification automatique une fois par jour (désactivable), ou manuelle avec le bouton « Vérifier maintenant ».
- **Sauvegarde** : export et import de ta progression (voir ci-dessous).

## Mises à jour

L'application vérifie une fois par jour s'il existe une nouvelle version sur GitHub. Si c'est le cas :

- une notification Windows s'affiche (une seule fois par version) ;
- un lien **« ⬆ Mise à jour disponible »** apparaît en bas du widget, ainsi qu'une entrée dans le menu de l'icône de la zone de notification.

Un clic sur ce lien propose d'**installer la mise à jour et de redémarrer** le widget. Ta progression est conservée. Si l'application n'a pas été installée avec l'installateur (version portable, compilation locale), le lien ouvre simplement la page de téléchargement.

## Sauvegarde

- Ta progression et son identifiant de joueur sont enregistrés dans un **fichier chiffré** (`state.dat`) : il ne peut pas être modifié à la main. Si le fichier est altéré, il est mis de côté et une nouvelle partie démarre.
- **Exporter** crée un fichier `.pokesave` (balls, Pokédex, identifiant de joueur, seed, position). **Importer** le restaure.

## Données et vie privée

- Ta progression est stockée **localement** :
  - `%AppData%\PokeWidget\` : progression (`state.dat`), réglages et cache des noms d'espèces ;
  - `%LocalAppData%\PokeWidgetCache\` : cache des sprites, que tu peux supprimer sans rien perdre.
- Rien n'est envoyé à un serveur tiers, et il n'y a aucune télémétrie.
- L'application contacte uniquement :
  - [PokéAPI](https://pokeapi.co/) pour les noms et taux de capture ;
  - `raw.githubusercontent.com` pour les sprites ([PokeAPI/sprites](https://github.com/PokeAPI/sprites)) ;
  - `api.github.com` et `github.com` pour vérifier et télécharger les mises à jour (vérification désactivable dans les réglages).
- Les sprites sont mis en cache sur ton disque (64 Mo maximum).
- La désinstallation ne supprime pas `%AppData%\PokeWidget\` : ta progression est conservée si tu réinstalles. Supprime ce dossier pour repartir de zéro.

---

# Version navigateur (userscript)

> Il s'agit d'un **userscript** : il s'installe via une extension de navigateur comme Tampermonkey ou Violentmonkey.

## Installation

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

- `https://golb.fr/blog/poke-widget`
- `https://discord.com/channels/*`

Tu peux modifier cette liste dans les réglages (voir ci-dessous).

## Utilisation

Les règles sont celles décrites plus haut (« Comment jouer »). Le Pokédex, les réglages et l'aide sont aussi accessibles depuis le **menu de l'extension** (Tampermonkey / Violentmonkey), même sur une page où le widget n'est pas affiché.

## Réglages

- **Seed** : change-la pour jouer avec tes amis. La seed par défaut du userscript est `youtube.com/@golbfr` parce que j'ai bien le droit de faire ma pub, héhé.
- **Position** : haut gauche, haut droite, bas gauche ou bas droite.
- **Pages où le widget apparaît** : liste d'URL avec `*` comme joker (ex. `https://discord.com/channels/*`).
- **Sauvegarde** : exporte ou importe ta progression au format `.json`. Pense à exporter avant de vider les données du navigateur ou de réinstaller l'extension.
- **Mises à jour** : bouton pour vérifier manuellement la dernière version.

## Mises à jour

Le script vérifie automatiquement une fois par jour s'il existe une nouvelle version. Si c'est le cas, un lien **« Mise à jour disponible »** apparaît dans le widget : clique dessus et ton gestionnaire de userscripts te proposera de mettre à jour. Tu peux aussi laisser ton gestionnaire gérer les mises à jour lui-même.

## Données et vie privée

- Ta progression est stockée **localement** dans ton gestionnaire de userscripts (`GM_setValue`). Rien n'est envoyé à un serveur tiers.
- Le script contacte uniquement :
  - [PokéAPI](https://pokeapi.co/) pour les noms et taux de capture ;
  - `raw.githubusercontent.com` pour les sprites ([PokeAPI/sprites](https://github.com/PokeAPI/sprites)) et la vérification des mises à jour.
- Les sprites sont mis en cache localement (environ 4 Mo maximum) pour accélérer l'affichage.
- Le script est chargé sur toutes les pages (pour faire avancer le compteur de balls), mais **n'affiche le widget que sur les pages de ta liste**.
- Il synchronise ta progression entre tous les onglets et sites où il tourne.

---

## Passer d'une plateforme à l'autre

1. Dans les réglages de l'ancienne plateforme : ⚙️ → **Exporter (.pokesave)**.
2. Dans la nouvelle plateforme ouvre ⚙️ → **Importer…** et choisis ce fichier. Tes balls, ton Pokédex, ta seed et ta position sont restaurés.

La progression des deux versions est ensuite **indépendante** : elle n'est pas synchronisée.

## Pour les développeurs

La version bureau est dans le dossier [`pokewidget/`](pokewidget/) : une application WPF (.NET) avec une bibliothèque de logique testée (`PokeWidget.Core`).

```
cd pokewidget
dotnet test                              # tests de la logique (seed, capture, sauvegarde, mises à jour…)
dotnet run --project PokeWidget.App      # lance l'application
```

La version de .NET se règle dans `pokewidget/Directory.Build.props`.

### Publier une version

Le numéro de version suit `X.Y.Z`, et le tag GitHub est `vX.Y.Z`. Deux possibilités :

- **Automatique** : pousse un tag (`git tag v1.1.0 && git push origin v1.1.0`) ; le workflow `.github/workflows/release.yml` teste, empaquette avec [Velopack](https://velopack.io/) et publie la release.
- **En local** : `powershell -ExecutionPolicy Bypass -File pokewidget/build/release.ps1 -Version 1.1.0` construit l'installateur dans `pokewidget/Releases/`. Ajoute `-Upload` (et un jeton GitHub dans la variable `GITHUB_TOKEN`) pour publier la release.

Le script accepte aussi `-FrameworkDependent` pour un paquet plus léger, dont l'installateur télécharge le runtime .NET s'il manque.

## Crédits

- Données : [PokéAPI](https://pokeapi.co/)
- Sprites : [PokeAPI/sprites](https://github.com/PokeAPI/sprites)
- Installateur et mises à jour : [Velopack](https://velopack.io/)
- GIF animés dans l'application : [XamlAnimatedGif](https://github.com/XamlAnimatedGif/XamlAnimatedGif)
- Pokémon et tous les noms associés sont des marques de Nintendo, Game Freak et The Pokémon Company. Ce projet est un projet de fan, sans lien officiel avec eux.
