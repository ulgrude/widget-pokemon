// ==UserScript==
// @name         Widget Pokémon
// @namespace    https://github.com/ulgrude
// @version      1.5.1
// @description  Un Pokémon seedé en commun, capturable aux Poké/Super/Hyper Balls, sur les pages de ton choix.
// @author       Ulgrude
// @updateURL    https://raw.githubusercontent.com/ulgrude/widget-pokemon/main/widget-pokemon.user.js
// @downloadURL  https://raw.githubusercontent.com/ulgrude/widget-pokemon/main/widget-pokemon.user.js
// @match        *://*/*
// @match        https://golb.fr/blog/poke-widget
// @connect      pokeapi.co
// @connect      raw.githubusercontent.com
// @run-at       document-idle
// @noframes
// @grant        GM_getValue
// @grant        GM_setValue
// @grant        GM_deleteValue
// @grant        GM_listValues
// @grant        GM_addValueChangeListener
// @grant        GM_xmlhttpRequest
// @grant        GM_addStyle
// @grant        GM_registerMenuCommand
// ==/UserScript==

(() => {
  'use strict';

  /* ========================================================================
     1. Réglages fixes (gameplay) — tout ce qui n'est pas seed/position/URLs
     ===================================================================== */
  const CFG = {
    DEFAULT_SEED: 'youtube.com/@golbfr',
    SLOT_MS: 30 * 60 * 1000, // un nouveau Pokémon toutes les 30 min
    BALL_MS: 60 * 60 * 1000, // une Poké Ball par heure
    MAX_POKEMON_ID: 1025,
    SHINY_RATE: 1 / 64,
    START_BALLS: 20,
    BALL_CAP: 100,
    BONUS_CHANCE: 0.05,
    SPRITE_CACHE_CHARS: 4 * 1024 * 1024, // budget du cache de sprites (≈ 4 Mo de data: URI)
    MISS_TTL_MS: 30 * 24 * 3600 * 1000,  // un sprite en 404 n'est pas redemandé pendant 30 jours
    SCRIPT_URL: 'https://raw.githubusercontent.com/ulgrude/widget-pokemon/main/widget-pokemon.user.js',
    UPDATE_EVERY_MS: 24 * 3600 * 1000,   // vérification automatique au plus une fois par jour
  };

  const BALLS = {
    poke: { label: 'Poké Ball', bonus: 1 },
    super: { label: 'Super Ball', bonus: 1.5 },
    hyper: { label: 'Hyper Ball', bonus: 2 },
  };

  const tierFor = (rate) => (rate >= 120 ? 'poke' : rate > 45 ? 'super' : 'hyper');

  const SPRITES = 'https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/';

  const POSITIONS = {
    'top-left': 'Haut gauche',
    'top-right': 'Haut droite',
    'bottom-left': 'Bas gauche',
    'bottom-right': 'Bas droite',
  };

  const DEFAULT_URLS = [
    'https://golb.fr/blog/poke-widget',
    'https://discord.com/channels/*',
  ];

  const createPlayerId = () => {
    if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();
    const bytes = crypto.getRandomValues(new Uint8Array(16));
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
  };
  const isPlayerId = (id) => typeof id === 'string'
    && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id);

  /* ========================================================================
     2. Stockage (GM_*) — partagé entre tous les sites où le script tourne,
        et synchronisé entre onglets ouverts en même temps
     ===================================================================== */
  const K_STATE = 'poke_state_v1';
  const K_CONFIG = 'poke_config_v1'; // { seed, position, urls }
  const K_SPECIES = 'poke_species_v1';
  const K_UI = 'poke_ui_v1';         // { collapsed }
  const K_UPDATE = 'poke_update_v1'; // { checkedAt, latest }
  const K_SPR_IDX = 'poke_sprites_idx_v1';
  const SPR_PREFIX = 'poke_spr:';

  const freshState = () => ({
    playerId: createPlayerId(),
    balls: { poke: CFG.START_BALLS, super: 0, hyper: 0 },
    lastTick: Date.now(),
    lastCaptureSlot: null,
    seenSlot: null, // dernier slot "vu" à l'écran (sert à l'animation de l'icône réduite)
    missedCount: 0,
    missedSlot: null,
    dex: {}, // id -> { name, rate, count, shiny, mystery }
    shop: {
      ballCapUpgrades: 0,
      missCatchRateUpgrades: 0,
      shinyBoostUpgrades: 0,
      spentDoublons: 0,
      spentShinyDoublons: 0,
    },
  });

  const maxBallCap = () => CFG.BALL_CAP + (((state.shop && state.shop.ballCapUpgrades) || 0) * 10);
  const getShinyRate = () => CFG.SHINY_RATE * (((state.shop && state.shop.shinyBoostUpgrades) || 0) > 0 ? 1.25 : 1.0);
  const getEffectiveRate = (baseRate, slot) => {
    if (((state.shop && state.shop.missCatchRateUpgrades) || 0) > 0 && state.missedSlot === slot) {
      return baseRate + ((state.missedCount || 0) * 0.02);
    }
    return baseRate;
  };

  function getEarnedDoublons() {
    let normalDupes = 0;
    let shinyDupes = 0;
    for (const e of Object.values(state.dex)) {
      const normalCount = Math.max(0, (e.count || 0) - (e.shiny || 0));
      if (normalCount > 1) normalDupes += (normalCount - 1);
      if ((e.shiny || 0) > 1) shinyDupes += ((e.shiny || 0) - 1);
    }
    return { normalDupes, shinyDupes };
  }

  function getAvailableDoublons() {
    const { normalDupes, shinyDupes } = getEarnedDoublons();
    const spent = (state.shop && state.shop.spentDoublons) || 0;
    const spentShiny = (state.shop && state.shop.spentShinyDoublons) || 0;
    return {
      available: Math.max(0, normalDupes - spent),
      availableShiny: Math.max(0, shinyDupes - spentShiny),
    };
  }

  const defaultConfig = () => ({
    seed: CFG.DEFAULT_SEED,
    position: 'bottom-right',
    urls: [...DEFAULT_URLS],
  });

  function loadConfig() {
    const c = GM_getValue(K_CONFIG, null);
    if (c && Array.isArray(c.urls) && typeof c.seed === 'string') return c;
    const d = defaultConfig();
    GM_setValue(K_CONFIG, d);
    return d;
  }

  function saveConfig(c) { GM_setValue(K_CONFIG, c); }

  let config = loadConfig();
  const storedState = GM_getValue(K_STATE, null);
  let state = storedState || freshState();
  const playerIdNeedsSave = !isPlayerId(state.playerId);
  if (playerIdNeedsSave) state.playerId = createPlayerId();
  const saveState = () => GM_setValue(K_STATE, state);
  if (!storedState || playerIdNeedsSave) saveState();

  let ui = Object.assign({ collapsed: false }, GM_getValue(K_UI, null) || {});
  const saveUi = () => GM_setValue(K_UI, ui);

  let speciesCache = GM_getValue(K_SPECIES, {});
  const saveSpeciesCache = () => GM_setValue(K_SPECIES, speciesCache);

  // Resynchronise si un autre onglet modifie l'état, la config ou l'UI en même temps
  GM_addValueChangeListener(K_STATE, (name, oldV, newV, remote) => {
    if (remote) {
      state = newV || freshState();
      if (!isPlayerId(state.playerId)) { state.playerId = createPlayerId(); saveState(); }
      render();
    }
  });
  GM_addValueChangeListener(K_CONFIG, (name, oldV, newV, remote) => {
    if (!remote) return;
    config = newV || defaultConfig();
    applyPosition();
    evaluateMount();
    current = null; loadingSlot = null;
    render(); loadSpawn();
  });
  GM_addValueChangeListener(K_UI, (name, oldV, newV, remote) => {
    if (!remote) return;
    ui = Object.assign({ collapsed: false }, newV || {});
    applyPosition();
    render();
  });

  /* ========================================================================
     3. Filtrage par URL (glob simple avec *)
     ===================================================================== */
  function patternToRegExp(pattern) {
    const esc = pattern.trim().replace(/[.+^${}()|[\]\\]/g, '\\$&');
    return new RegExp('^' + esc.replace(/\*/g, '.*') + '$');
  }
  function urlMatchesConfig() {
    const href = location.href;
    return config.urls.some((p) => {
      if (!p || !p.trim()) return false;
      try { return patternToRegExp(p).test(href); } catch (e) { return false; }
    });
  }

  /* ========================================================================
     4. RNG seedé (identique pour tout le monde avec la même seed)
     ===================================================================== */
  function hashStr(str) {
    let h1 = 1779033703, h2 = 3144134277, h3 = 1013904242, h4 = 2773480762;
    for (let i = 0; i < str.length; i++) {
      const k = str.charCodeAt(i);
      h1 = h2 ^ Math.imul(h1 ^ k, 597399067);
      h2 = h3 ^ Math.imul(h2 ^ k, 2869860233);
      h3 = h4 ^ Math.imul(h3 ^ k, 951274213);
      h4 = h1 ^ Math.imul(h4 ^ k, 2716044179);
    }
    h1 = Math.imul(h3 ^ (h1 >>> 18), 597399067);
    h2 = Math.imul(h4 ^ (h2 >>> 22), 2869860233);
    h3 = Math.imul(h1 ^ (h3 >>> 17), 951274213);
    h4 = Math.imul(h2 ^ (h4 >>> 19), 2716044179);
    return (h1 ^ h2 ^ h3 ^ h4) >>> 0;
  }
  function mulberry32(a) {
    return () => {
      a |= 0; a = (a + 0x6D2B79F5) | 0;
      let t = Math.imul(a ^ (a >>> 15), 1 | a);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }
  const currentSlot = () => Math.floor(Date.now() / CFG.SLOT_MS);
  function spawnFor(slot) {
    const slotKey = config.seed + ':' + slot;
    const pokemonRng = mulberry32(hashStr(slotKey));
    const id = 1 + Math.floor(pokemonRng() * CFG.MAX_POKEMON_ID);
    const shinyRng = mulberry32(hashStr(slotKey + ':' + state.playerId));
    const shiny = shinyRng() < getShinyRate();
    return { slot, id, shiny };
  }

  /* ========================================================================
     5. Capture
     ===================================================================== */
  function catchProb(rate, bonus) {
    const a = Math.min(255, (rate * bonus) / 3);
    if (a >= 255) return 1;
    const b = 1048560 / Math.sqrt(Math.sqrt(16711680 / a));
    return Math.pow(b / 65536, 4);
  }

  const addBall = (type, n = 1) => { state.balls[type] = Math.min(maxBallCap(), state.balls[type] + n); };

  function tickBalls() {
    const hours = Math.floor((Date.now() - state.lastTick) / CFG.BALL_MS);
    if (hours <= 0) return false;
    state.lastTick += hours * CFG.BALL_MS;

    const trials = { poke: 0, super: 0, hyper: 0 };
    for (const e of Object.values(state.dex)) trials[tierFor(e.rate)] += e.count;

    const cap = maxBallCap();
    for (let h = 0; h < hours; h++) {
      addBall('poke', 1);
      for (const t of Object.keys(trials)) {
        for (let i = 0; i < trials[t]; i++) {
          if (Math.random() < CFG.BONUS_CHANCE) addBall(t, 1);
        }
      }
      if (Object.values(state.balls).every((n) => n >= cap)) break;
    }
    saveState();
    return true;
  }
  // Tourne dès le chargement du script, sur N'IMPORTE QUELLE page : les
  // balls continuent donc à s'accumuler même si on ne visite jamais les
  // pages où le widget s'affiche.
  tickBalls();

  /* ========================================================================
     6. Appels réseau via GM_xmlhttpRequest (passe par-dessus la CSP des
        sites comme Discord, contrairement à fetch())
     ===================================================================== */
  function gmGet(url, responseType) {
    return new Promise((resolve, reject) => {
      GM_xmlhttpRequest({
        method: 'GET', url, responseType, timeout: 10000,
        onload: (res) => (res.status >= 200 && res.status < 300 ? resolve(res) : reject(res)),
        onerror: reject,
        ontimeout: reject,
      });
    });
  }

  async function getSpecies(id) {
    if (speciesCache[id]) return speciesCache[id];
    const res = await gmGet(`https://pokeapi.co/api/v2/pokemon-species/${id}`, 'json');
    const data = res.response;
    const fr = data.names.find((n) => n.language.name === 'fr');
    const en = data.names.find((n) => n.language.name === 'en');
    const entry = { name: (fr || en || { name: data.name }).name, rate: data.capture_rate };
    speciesCache[id] = entry;
    saveSpeciesCache();
    return entry;
  }

  function spriteChain(id, shiny) {
    const s = shiny ? 'shiny/' : '';
    return [
      `${SPRITES}versions/generation-v/black-white/animated/${s}${id}.gif`,
      `${SPRITES}${s}${id}.png`,
      `${SPRITES}other/official-artwork/${s}${id}.png`,
    ];
  }

  /* ---- Cache persistant des sprites (GM_setValue) ----------------------
     Une clé par sprite + un index { clé: {t: dernier usage, n: taille} }.
     GM_* est partagé entre tous les sites (contrairement à IndexedDB qui
     serait cloisonné par origine : discord.com ≠ newtab).
     Éviction LRU quand le budget est dépassé, donc les sprites les plus
     utilisés restent. Les 404 (ex. GIF animé absent au-delà de la gen 5)
     sont mémorisés pour ne pas être redemandés à chaque chargement. */
  let sprIdx = GM_getValue(K_SPR_IDX, null) || {};
  let sprDirty = false, sprTimer = null;
  const sprKey = (url) => SPR_PREFIX + (url.startsWith(SPRITES) ? url.slice(SPRITES.length) : url);

  function sprFlush() {
    clearTimeout(sprTimer); sprTimer = null;
    if (!sprDirty) return;
    sprDirty = false;
    GM_setValue(K_SPR_IDX, sprIdx);
  }
  function sprSchedule() {
    sprDirty = true;
    if (!sprTimer) sprTimer = setTimeout(sprFlush, 2000);
  }
  window.addEventListener('pagehide', sprFlush);

  // string = trouvé ; null = sprite connu comme absent (404) ; undefined = inconnu
  function sprGet(url) {
    const k = sprKey(url), m = sprIdx[k];
    if (!m) return undefined;
    if (m.miss) {
      if (Date.now() - m.miss < CFG.MISS_TTL_MS) return null;
      delete sprIdx[k]; sprSchedule();
      return undefined;
    }
    const v = GM_getValue(k, null);
    if (typeof v !== 'string') { delete sprIdx[k]; sprSchedule(); return undefined; }
    m.t = Date.now(); sprSchedule();
    return v;
  }
  function sprEvict() {
    let total = 0;
    for (const m of Object.values(sprIdx)) total += m.n || 0;
    if (total <= CFG.SPRITE_CACHE_CHARS) return;
    const oldest = Object.entries(sprIdx).filter(([, m]) => !m.miss).sort((a, b) => a[1].t - b[1].t);
    for (const [k, m] of oldest) {
      if (total <= CFG.SPRITE_CACHE_CHARS) break;
      GM_deleteValue(k);
      total -= m.n || 0;
      delete sprIdx[k];
    }
  }
  function sprPut(url, dataUrl) {
    if (dataUrl.length > CFG.SPRITE_CACHE_CHARS / 4) return; // trop gros pour valoir le coup
    const k = sprKey(url);
    GM_setValue(k, dataUrl);
    sprIdx[k] = { t: Date.now(), n: dataUrl.length };
    sprEvict();
    sprSchedule();
  }
  function sprMiss(url) {
    sprIdx[sprKey(url)] = { t: Date.now(), n: 0, miss: Date.now() };
    sprSchedule();
  }
  // Supprime les données orphelines (index écrasé par un autre onglet, etc.)
  function sprPrune() {
    try {
      const idx = Object.assign({}, GM_getValue(K_SPR_IDX, null) || {}, sprIdx);
      for (const k of GM_listValues()) {
        if (k.startsWith(SPR_PREFIX) && !idx[k]) GM_deleteValue(k);
      }
    } catch (e) { /* non bloquant */ }
  }
  setTimeout(sprPrune, 5000);

  // Récupère l'image en binaire et la transforme en data: URI : les CSP
  // "img-src" restrictives (Discord) bloquent souvent les domaines externes
  // mais laissent presque toujours passer les data: URI.
  const dataUrlCache = {};
  const pendingSprites = {};

  function blobToDataUrl(blob) {
    return new Promise((resolve, reject) => {
      const r = new FileReader();
      r.onload = () => resolve(r.result);
      r.onerror = reject;
      r.readAsDataURL(blob);
    });
  }

  function spriteDataUrl(url) {
    if (dataUrlCache[url]) return Promise.resolve(dataUrlCache[url]);
    const stored = sprGet(url);
    if (typeof stored === 'string') { dataUrlCache[url] = stored; return Promise.resolve(stored); }
    if (stored === null) return Promise.reject(new Error('sprite absent (404 mémorisé)'));
    if (pendingSprites[url]) return pendingSprites[url];
    return (pendingSprites[url] = (async () => {
      try {
        const res = await gmGet(url, 'blob');
        const dataUrl = await blobToDataUrl(res.response);
        dataUrlCache[url] = dataUrl;
        sprPut(url, dataUrl);
        return dataUrl;
      } catch (e) {
        if (e && e.status === 404) sprMiss(url);
        throw e;
      } finally {
        delete pendingSprites[url];
      }
    })());
  }

  async function setSprite(imgEl, id, shiny) {
    const chain = spriteChain(id, shiny);
    imgEl.classList.remove('pk-artwork');
    for (let i = 0; i < chain.length; i++) {
      try {
        const url = await spriteDataUrl(chain[i]);
        if (i === 2) imgEl.classList.add('pk-artwork');
        imgEl.src = url;
        imgEl.hidden = false;
        return;
      } catch (e) { /* on tente le format suivant */ }
    }
  }

  /* ========================================================================
     6b. Mises à jour — compare la @version installée à celle du fichier
         GitHub. Un userscript ne peut pas se réécrire lui-même : on affiche
         un lien vers le fichier .user.js, et le gestionnaire (Tampermonkey,
         Violentmonkey…) propose l'installation.
     ===================================================================== */
  const INSTALLED_VERSION = (typeof GM_info !== 'undefined' && GM_info.script && GM_info.script.version) || '0';
  const updateConfigured = () => !CFG.SCRIPT_URL.includes('/REPO/');
  let updateInfo = Object.assign({ checkedAt: 0, latest: null }, GM_getValue(K_UPDATE, null) || {});

  function cmpVersions(a, b) {
    const pa = String(a).split('.').map((n) => parseInt(n, 10) || 0);
    const pb = String(b).split('.').map((n) => parseInt(n, 10) || 0);
    for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
      const d = (pa[i] || 0) - (pb[i] || 0);
      if (d) return d;
    }
    return 0;
  }
  const updateAvailable = () => !!(updateInfo.latest && cmpVersions(updateInfo.latest, INSTALLED_VERSION) > 0);

  async function checkForUpdate({ force = false } = {}) {
    if (!updateConfigured()) throw new Error("l'URL GitHub n'est pas renseignée dans le script (CFG.SCRIPT_URL)");
    if (!force && Date.now() - updateInfo.checkedAt < CFG.UPDATE_EVERY_MS) return updateInfo;
    // on "réserve" la vérification tout de suite pour que les autres onglets ne la refassent pas
    const prev = updateInfo.checkedAt;
    updateInfo.checkedAt = Date.now();
    GM_setValue(K_UPDATE, updateInfo);
    try {
      const res = await gmGet(`${CFG.SCRIPT_URL}?_=${Date.now()}`, 'text'); // ?_= évite le cache de GitHub
      const m = /\/\/\s*@version\s+(\S+)/.exec((res.responseText || '').slice(0, 4000));
      if (!m) throw new Error('@version introuvable dans le fichier distant');
      updateInfo.latest = m[1];
      GM_setValue(K_UPDATE, updateInfo);
      return updateInfo;
    } catch (e) {
      // échec : nouvelle tentative automatique dans 1 h
      updateInfo.checkedAt = force ? prev : Date.now() - CFG.UPDATE_EVERY_MS + 3600 * 1000;
      GM_setValue(K_UPDATE, updateInfo);
      throw e;
    }
  }

  GM_addValueChangeListener(K_UPDATE, (name, oldV, newV, remote) => {
    if (!remote) return;
    updateInfo = Object.assign({ checkedAt: 0, latest: null }, newV || {});
    refreshUpdateUi();
  });

  function refreshUpdateUi() {
    if (!el || !el.update) return;
    const on = updateAvailable();
    el.update.hidden = !on;
    if (on) {
      el.update.href = CFG.SCRIPT_URL;
      el.update.textContent = `⬆ Mise à jour ${updateInfo.latest} disponible`;
    }
  }

  /* ========================================================================
     7. Styles
     ===================================================================== */
  GM_addStyle(`
#pokeWidget{position:fixed;width:232px;padding:10px 12px 12px;box-sizing:border-box;z-index:2147483000;
  color:#f2f4f5;font:13px/1.35 system-ui,-apple-system,"Segoe UI",sans-serif;
  background:rgba(24,28,32,.88);border:1px solid rgba(255,255,255,.12);border-radius:14px;
  backdrop-filter:blur(8px);-webkit-backdrop-filter:blur(8px);box-shadow:0 6px 24px rgba(0,0,0,.35);text-align:center}
#pokeWidget.pk-pos-top-left{top:16px;left:16px}
#pokeWidget.pk-pos-top-right{top:16px;right:16px}
#pokeWidget.pk-pos-bottom-left{bottom:16px;left:16px}
#pokeWidget.pk-pos-bottom-right{bottom:16px;right:16px}
#pokeWidget button{font:inherit;color:inherit;cursor:pointer}
#pokeWidget button:disabled{cursor:not-allowed}
#pokeWidget button:focus-visible,.pk-overlay button:focus-visible,.pk-overlay input:focus-visible,.pk-overlay select:focus-visible{outline:2px solid #ffcb05;outline-offset:2px}
.pk-head{display:flex;justify-content:space-between;align-items:center;gap:8px}
.pk-title{font-weight:700;letter-spacing:.02em;min-width:0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.pk-tools{display:flex;gap:2px;flex:0 0 auto}
.pk-icon{background:none;border:0;border-radius:6px;padding:3px 5px;line-height:1}
.pk-icon:hover{background:rgba(255,255,255,.12)}
.pk-stage{position:relative;height:128px;margin:6px 0 2px;display:flex;align-items:center;justify-content:center;border-radius:10px;
  background:radial-gradient(ellipse at 50% 85%,rgba(255,255,255,.14),transparent 65%)}
.pk-stage.pk-shiny{background:radial-gradient(ellipse at 50% 85%,rgba(255,203,5,.32),transparent 65%)}
.pk-sprite{max-height:112px;max-width:112px;height:auto;width:auto;image-rendering:pixelated;transform-origin:50% 100%;transition:opacity .25s,filter .25s}
.pk-sprite[hidden]{display:none}
.pk-sprite.pk-artwork{image-rendering:auto}
.pk-stage.pk-caught .pk-sprite{opacity:.22;filter:grayscale(1)}
.pk-shake .pk-sprite{animation:pk-shake .7s ease-in-out}
@keyframes pk-shake{0%,100%{transform:rotate(0)}15%{transform:rotate(-14deg)}35%{transform:rotate(12deg)}55%{transform:rotate(-9deg)}75%{transform:rotate(6deg)}}
.pk-msg{position:absolute;left:0;right:0;bottom:2px;min-height:1em;font-weight:600;text-shadow:0 1px 4px rgba(0,0,0,.8)}
.pk-name{font-size:15px;font-weight:700;display:flex;align-items:center;justify-content:center;gap:6px}
.pk-name-ball{display:none;width:18px!important;height:18px!important;border-width:1.5px!important;flex-shrink:0}
.pk-name-ball::after{width:4px;height:4px;border-width:1.5px}
.pk-meta{opacity:.65;font-size:12px;min-height:1.35em}
.pk-balls{display:flex;gap:6px;margin:8px 0 6px}
.pk-ballbtn{flex:1;display:flex;flex-direction:column;align-items:center;gap:1px;padding:6px 2px 4px;
  background:rgba(255,255,255,.07);border:1px solid rgba(255,255,255,.1);border-radius:10px;transition:background .15s,transform .1s}
.pk-ballbtn:hover:not(:disabled){background:rgba(255,255,255,.16)}
.pk-ballbtn:active:not(:disabled){transform:scale(.95)}
.pk-ballbtn:disabled{opacity:.4}
.pk-ballbtn .pk-count{font-size:14px}
.pk-ballbtn .pk-pct{opacity:.65;font-size:11px;min-height:1.2em}
.pk-ball{position:relative;display:inline-block;width:24px;height:24px;box-sizing:border-box;border:2px solid #15181b;border-radius:50%;
  background:linear-gradient(var(--pk-top) 0 44%,#15181b 44% 56%,#f4f4f4 56%)}
.pk-ball::after{content:"";position:absolute;top:50%;left:50%;width:8px;height:8px;box-sizing:border-box;transform:translate(-50%,-50%);
  border:2px solid #15181b;border-radius:50%;background:#f4f4f4}
.pk-poke{--pk-top:#e53935}.pk-super{--pk-top:#1e88e5}.pk-hyper{--pk-top:#f9c62b}
.pk-status{font-size:12px;opacity:.75}
#pokeWidget .pk-update{display:block;margin-top:4px;font-size:12px;font-weight:600;color:#ffcb05;text-decoration:underline}
#pokeWidget .pk-update[hidden]{display:none}

/* --- Mode réduit : simple icône de Poké Ball --- */
.pk-mini{display:none}
#pokeWidget.pk-min{width:auto;padding:0;background:none;border:0;box-shadow:none;backdrop-filter:none;-webkit-backdrop-filter:none}
#pokeWidget.pk-min .pk-full{display:none}
#pokeWidget.pk-min .pk-mini{display:flex}
#pokeWidget .pk-mini{position:relative;width:42px;height:42px;padding:0;align-items:center;justify-content:center;
  background:rgba(24,28,32,.88);border:1px solid rgba(255,255,255,.18);border-radius:50%;
  box-shadow:0 4px 16px rgba(0,0,0,.35);opacity:.8;transition:opacity .15s,transform .15s}
#pokeWidget .pk-mini:hover{opacity:1;transform:scale(1.08)}
.pk-mini .pk-ball{width:26px;height:26px}
.pk-mini.pk-new,.pk-mini.pk-shine{opacity:1}
.pk-mini.pk-new::after,.pk-mini.pk-shine::after{content:"";position:absolute;top:-2px;right:-2px;width:11px;height:11px;box-sizing:border-box;
  border-radius:50%;background:#ff5252;border:2px solid #1e2327}
.pk-mini.pk-shine{border-color:#ffcb05}
.pk-mini.pk-shine::after{background:#ffcb05}
.pk-mini.pk-new{animation:pk-wobble 2.6s ease-in-out infinite}
.pk-mini.pk-shine{animation:pk-glow 1.6s ease-in-out infinite}
.pk-mini.pk-new.pk-shine{animation:pk-wobble 2.6s ease-in-out infinite,pk-glow 1.6s ease-in-out infinite}
@keyframes pk-wobble{0%,55%,100%{transform:rotate(0)}62%{transform:rotate(-18deg)}70%{transform:rotate(16deg)}78%{transform:rotate(-11deg)}86%{transform:rotate(6deg)}}
@keyframes pk-glow{0%,100%{box-shadow:0 0 6px 1px rgba(255,203,5,.5),0 4px 16px rgba(0,0,0,.35)}50%{box-shadow:0 0 18px 6px rgba(255,203,5,.95),0 4px 16px rgba(0,0,0,.35)}}

.pk-overlay{position:fixed;inset:0;z-index:2147483001;display:flex;align-items:center;justify-content:center;padding:16px;background:rgba(0,0,0,.6)}
.pk-panel{width:min(760px,100%);max-height:85vh;overflow:auto;padding:16px;box-sizing:border-box;color:#f2f4f5;
  font:14px/1.4 system-ui,-apple-system,"Segoe UI",sans-serif;background:#1e2327;border:1px solid rgba(255,255,255,.12);border-radius:14px}
.pk-panel-head{display:flex;justify-content:space-between;align-items:center;gap:12px;margin-bottom:12px}
.pk-panel-head h2{margin:0;font-size:16px}
.pk-panel .pk-icon{font-size:16px;color:inherit;cursor:pointer}
.pk-empty{opacity:.7;margin:8px 0}
.pk-help h3{margin:14px 0 4px;font-size:14px;color:#ffcb05}
.pk-help p{margin:0 0 6px}
.pk-help ul{margin:0 0 6px;padding-left:20px}
.pk-help li{margin:2px 0}
.pk-help .pk-ball{display:inline-block;vertical-align:-6px;margin-right:4px}
.pk-help small{opacity:.65}
.pk-search{width:100%;box-sizing:border-box;margin:0 0 12px;padding:8px 10px;border-radius:8px;border:1px solid rgba(255,255,255,.18);
  background:rgba(255,255,255,.06);color:inherit;font:inherit}
.pk-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(104px,1fr));gap:8px}
.pk-card{padding:8px 4px;text-align:center;background:rgba(255,255,255,.06);border-radius:10px}
.pk-card[hidden]{display:none}
.pk-card img{width:72px;height:72px;image-rendering:pixelated}
.pk-card-id{opacity:.5;font-size:11px}
.pk-card-name{font-weight:600;font-size:13px}
.pk-card-count{opacity:.7;font-size:12px}
.pk-field{display:block;margin:0 0 16px;text-align:left}
.pk-field>span{display:block;font-weight:600;margin-bottom:6px}
.pk-field input[type=text]{width:100%;box-sizing:border-box;padding:7px 9px;border-radius:8px;border:1px solid rgba(255,255,255,.18);
  background:rgba(255,255,255,.06);color:inherit;font:inherit}
.pk-hint{display:block;margin-top:5px;opacity:.6;font-size:12px}
.pk-posgrid{display:grid;grid-template-columns:1fr 1fr;gap:8px;max-width:280px}
.pk-posbtn{padding:10px 6px;border-radius:8px;border:1px solid rgba(255,255,255,.18);background:rgba(255,255,255,.06);color:inherit;font:inherit;cursor:pointer}
.pk-posbtn.pk-active{border-color:#ffcb05;background:rgba(255,203,5,.16)}
.pk-urlrow{display:flex;gap:6px;margin-bottom:6px}
.pk-urlrow input{flex:1}
.pk-urlrow button{flex:0 0 auto;padding:0 10px;border-radius:8px;border:1px solid rgba(255,255,255,.18);background:rgba(255,255,255,.06);color:inherit;cursor:pointer}
.pk-urlrow button:hover{background:rgba(255,80,80,.25)}
.pk-addurl{margin-top:2px;padding:7px 12px;border-radius:8px;border:1px dashed rgba(255,255,255,.3);background:transparent;color:inherit;cursor:pointer}
.pk-addurl:hover{background:rgba(255,255,255,.08)}
.pk-row{display:flex;flex-wrap:wrap;gap:8px}
.pk-panel-actions{display:flex;justify-content:flex-end;gap:8px;margin-top:4px;padding-top:12px;border-top:1px solid rgba(255,255,255,.1)}
.pk-btn{padding:8px 16px;border-radius:8px;border:1px solid rgba(255,255,255,.18);background:rgba(255,255,255,.06);color:inherit;font:inherit;font-weight:600;cursor:pointer}
.pk-btn.pk-primary{background:#ffcb05;border-color:#ffcb05;color:#15181b}
.pk-btn:hover:not(:disabled){filter:brightness(1.1)}
.pk-btn:disabled{opacity:.45;cursor:not-allowed}
.pk-shop-balance{background:rgba(255,255,255,.05);border:1px solid rgba(255,255,255,.12);border-radius:10px;padding:12px;margin-bottom:14px}
.pk-badge{display:inline-block;padding:3px 8px;border-radius:6px;font-size:12px;font-weight:600}
.pk-badge-gold{background:rgba(255,203,5,.16);border:1px solid rgba(255,203,5,.4);color:#ffcb05}
.pk-badge-blue{background:rgba(79,195,247,.16);border:1px solid rgba(79,195,247,.4);color:#4fc3f7}
.pk-shop-list{display:flex;flex-direction:column;gap:10px}
.pk-shop-card{display:flex;justify-content:space-between;align-items:center;gap:12px;background:rgba(255,255,255,.04);border:1px solid rgba(255,255,255,.08);border-radius:10px;padding:12px}
.pk-shop-card-info{flex:1;text-align:left}
.pk-shop-card-title{font-weight:700;font-size:14px;margin-bottom:2px}
.pk-shop-card-desc{font-size:12px;opacity:.7;margin-bottom:4px}
.pk-shop-card-cost{font-size:12px;font-weight:600}
.pk-shop-panel{position:relative;min-height:360px}
.pk-reveal-overlay{position:absolute;inset:0;background:rgba(14,17,20,.94);border-radius:14px;display:flex;flex-direction:column;align-items:center;justify-content:center;padding:20px;z-index:20;text-align:center}
.pk-reveal-ball{position:relative;display:inline-block;width:72px;height:72px;box-sizing:border-box;border:3px solid #15181b;border-radius:50%;
  background:linear-gradient(var(--pk-top) 0 43.8%,#15181b 43.8% 56.2%,#f4f4f4 56.2%);margin:12px 0 20px}
.pk-reveal-ball::before{content:"";position:absolute;top:50%;left:50%;width:24px;height:24px;box-sizing:border-box;transform:translate(-50%,-50%);
  border-radius:50%;background:#15181b}
.pk-reveal-ball::after{content:"";position:absolute;top:50%;left:50%;width:17px;height:17px;box-sizing:border-box;transform:translate(-50%,-50%);
  border-radius:50%;background:#f4f4f4;z-index:1}
.pk-reveal-wobble{display:inline-block;transform-origin:50% 50%;animation:pk-ball-wobble 1.3s ease-in-out}
@keyframes pk-ball-wobble{0%,100%{transform:rotate(0)}15%{transform:rotate(-22deg)}30%{transform:rotate(20deg)}45%{transform:rotate(-18deg)}60%{transform:rotate(16deg)}75%{transform:rotate(-10deg)}90%{transform:rotate(6deg)}}
@keyframes pk-reveal-pop{0%{transform:scale(.7);opacity:0}100%{transform:scale(1);opacity:1}}
.pk-reveal-result{animation:pk-reveal-pop .4s ease-out;display:flex;flex-direction:column;align-items:center}
.pk-reveal-stage{width:120px;height:120px;display:flex;align-items:center;justify-content:center;border-radius:12px;background:rgba(255,255,255,.08);margin-bottom:10px}
.pk-reveal-stage.pk-shiny{background:radial-gradient(ellipse at 50% 85%,rgba(255,203,5,.32),transparent 65%)}
.pk-reveal-sprite{max-width:96px;max-height:96px;image-rendering:pixelated}
@media (prefers-reduced-motion:reduce){.pk-shake .pk-sprite{animation:none}.pk-mini.pk-new,.pk-mini.pk-shine{animation:none}.pk-reveal-wobble{animation:none}}
@media (max-width:520px){#pokeWidget{width:208px}#pokeWidget.pk-pos-top-left,#pokeWidget.pk-pos-bottom-left{left:8px}
  #pokeWidget.pk-pos-top-right,#pokeWidget.pk-pos-bottom-right{right:8px}
  #pokeWidget.pk-pos-top-left,#pokeWidget.pk-pos-top-right{top:8px}#pokeWidget.pk-pos-bottom-left,#pokeWidget.pk-pos-bottom-right{bottom:8px}}
`);

  /* ========================================================================
     8. Widget — création / destruction selon la liste d'URL
     ===================================================================== */
  let root = null, el = null, current = null, loadingSlot = null, lastFail = 0, busy = false, msgTimer = null;

  function buildWidget() {
    root = document.createElement('div');
    root.id = 'pokeWidget';
    root.innerHTML = `
      <button class="pk-mini" data-act="expand" title="Afficher le widget Pokémon" aria-label="Afficher le widget Pokémon">
        <span class="pk-ball pk-poke"></span>
      </button>
      <div class="pk-full">
        <div class="pk-head">
          <span class="pk-title" title="Poké-widget">Poké-widget</span>
          <span class="pk-tools">
            <button class="pk-icon" data-act="help" title="Comment jouer ?" aria-label="Comment jouer ?">?</button>
            <button class="pk-icon" data-act="dex" title="Pokédex" aria-label="Pokédex">📖</button>
            <button class="pk-icon" data-act="shop" title="Boutique" aria-label="Boutique">🏪</button>
            <button class="pk-icon" data-act="settings" title="Réglages" aria-label="Réglages">⚙️</button>
            <button class="pk-icon" data-act="min" title="Réduire" aria-label="Réduire">−</button>
          </span>
        </div>
        <div class="pk-stage">
          <img class="pk-sprite" alt="" hidden>
          <div class="pk-msg" aria-live="polite"></div>
        </div>
        <div class="pk-name">
          <span class="pk-ball pk-poke pk-name-ball"></span>
          <span class="pk-name-text">Chargement…</span>
        </div>
        <div class="pk-meta"></div>
        <div class="pk-balls">
          ${Object.entries(BALLS).map(([k, b]) => `
            <button class="pk-ballbtn" data-ball="${k}" title="${b.label}">
              <span class="pk-ball pk-${k}"></span>
              <b class="pk-count">0</b>
              <small class="pk-pct"></small>
            </button>`).join('')}
        </div>
        <div class="pk-status"></div>
        <a class="pk-update" href="#" target="_blank" rel="noopener" hidden></a>
      </div>`;
    document.body.appendChild(root);

    const $ = (sel) => root.querySelector(sel);
    el = {
      sprite: $('.pk-sprite'), msg: $('.pk-msg'), name: $('.pk-name-text'), nameBall: $('.pk-name-ball'),
      meta: $('.pk-meta'), status: $('.pk-status'), stage: $('.pk-stage'), mini: $('.pk-mini'), update: $('.pk-update'),
      btns: Object.fromEntries(Object.keys(BALLS).map((k) => [k, $(`[data-ball="${k}"]`)])),
    };

    root.addEventListener('click', (ev) => {
      const ball = ev.target.closest('[data-ball]');
      if (ball) return throwBall(ball.dataset.ball);
      const act = ev.target.closest('[data-act]');
      if (!act) return;
      if (act.dataset.act === 'help') openHelp();
      if (act.dataset.act === 'dex') openDex();
      if (act.dataset.act === 'shop') openShop();
      if (act.dataset.act === 'settings') openSettings();
      if (act.dataset.act === 'min') setCollapsed(true);
      if (act.dataset.act === 'expand') setCollapsed(false);
    });

    applyPosition();
  }

  function destroyWidget() {
    if (root) root.remove();
    root = null; el = null;
  }

  function applyPosition() {
    if (!root) return;
    root.className = 'pk-pos-' + config.position + (ui.collapsed ? ' pk-min' : '');
  }

  function setCollapsed(v) {
    ui.collapsed = !!v;
    saveUi();
    applyPosition();
    markSeen();
    render();
  }

  // Monte ou démonte le widget selon que l'URL courante matche la config.
  function evaluateMount() {
    const should = urlMatchesConfig();
    if (should && !root) { buildWidget(); render(); loadSpawn(); }
    else if (!should && root) { destroyWidget(); }
  }

  /* ------------------------------ Affichage ------------------------------ */
  const fmt = (ms) => {
    const s = Math.max(0, Math.ceil(ms / 1000));
    return `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`;
  };
  function say(text) {
    if (!el) return;
    el.msg.textContent = text;
    clearTimeout(msgTimer);
    msgTimer = setTimeout(() => { if (el) el.msg.textContent = ''; }, 3500);
  }
  const isCaught = () => current && state.lastCaptureSlot === current.slot;

  // Le slot courant est considéré "vu" dès que le widget est affiché en grand
  // dans un onglet visible. Tant que ce n'est pas le cas, l'icône réduite
  // signale le nouveau Pokémon.
  function markSeen() {
    if (!root || ui.collapsed || document.hidden) return;
    const slot = currentSlot();
    if (state.seenSlot !== slot) { state.seenSlot = slot; saveState(); }
  }

  // L'état de l'icône se déduit de la seed seule (pas besoin de PokéAPI).
  function updateMini() {
    if (!el) return;
    const slot = currentSlot();
    const caught = state.lastCaptureSlot === slot;
    const shine = !caught && spawnFor(slot).shiny;
    const isNew = !caught && state.seenSlot !== slot;
    el.mini.classList.toggle('pk-new', isNew);
    el.mini.classList.toggle('pk-shine', shine);
    const label = shine ? 'Un Pokémon chromatique est là !'
      : isNew ? 'Un nouveau Pokémon est apparu'
        : 'Afficher le widget Pokémon';
    el.mini.title = label;
    el.mini.setAttribute('aria-label', label);
  }

  function render() {
    if (!root || !el) return;
    const effRate = current ? getEffectiveRate(current.rate, current.slot) : 45;
    for (const [k, b] of Object.entries(BALLS)) {
      const btn = el.btns[k];
      btn.querySelector('.pk-count').textContent = state.balls[k];
      const pct = current ? catchProb(effRate, b.bonus) * 100 : null;
      btn.querySelector('.pk-pct').textContent = pct === null ? '' : pct < 1 ? '<1 %' : `${Math.round(pct)} %`;
      btn.disabled = busy || !current || isCaught() || state.balls[k] <= 0;
    }
    if (!current) {
      if (el.nameBall) el.nameBall.style.display = 'none';
      el.name.textContent = lastFail ? 'PokéAPI injoignable' : 'Chargement…';
      el.meta.textContent = '';
      el.sprite.hidden = true;
    } else {
      const inDex = !!(state.dex[current.id]);
      if (el.nameBall) el.nameBall.style.display = inDex ? 'inline-block' : 'none';
      el.name.textContent = (current.shiny ? '✨ ' : '') + current.name;
      const bonusRate = effRate > current.rate ? ` (+${((state.missedCount || 0) * 0.02).toFixed(2)})` : '';
      el.meta.textContent = `Taux de capture : ${current.rate}${bonusRate}`;
      el.stage.classList.toggle('pk-caught', !!isCaught());
      el.stage.classList.toggle('pk-shiny', current.shiny);
    }
    renderStatus();
    updateMini();
    refreshUpdateUi();
  }
  function renderStatus() {
    if (!el) return;
    const left = (currentSlot() + 1) * CFG.SLOT_MS - Date.now();
    if (!current) { el.status.textContent = lastFail ? 'Nouvelle tentative…' : ''; return; }
    el.status.textContent = isCaught()
      ? `Capturé. Prochain Pokémon dans ${fmt(left)}`
      : `Il reste ${fmt(left)} pour l'attraper`;
  }

  async function loadSpawn() {
    const slot = currentSlot();
    if (loadingSlot === slot) return;
    loadingSlot = slot;
    const sp = spawnFor(slot);
    try {
      const info = await getSpecies(sp.id);
      if (currentSlot() !== slot) { loadingSlot = null; return; }
      current = { ...sp, ...info };
      lastFail = 0;
      if (el) await setSprite(el.sprite, current.id, current.shiny);
    } catch (e) {
      lastFail = Date.now();
      current = null;
      loadingSlot = null;
    }
    render();
  }

  function throwBall(type) {
    if (busy || !current || isCaught() || state.balls[type] <= 0) return;
    const target = current;
    busy = true;
    state.balls[type]--;
    saveState();
    el.stage.classList.add('pk-shake');
    render();
    setTimeout(() => {
      if (el) el.stage.classList.remove('pk-shake');
      busy = false;
      if (state.missedSlot !== target.slot) {
        state.missedSlot = target.slot;
        state.missedCount = 0;
      }
      const effRate = getEffectiveRate(target.rate, target.slot);
      if (Math.random() < catchProb(effRate, BALLS[type].bonus)) {
        state.missedCount = 0;
        const e = state.dex[target.id] || (state.dex[target.id] = { name: target.name, rate: target.rate, count: 0, shiny: 0, mystery: 0 });
        e.count++;
        if (target.shiny) e.shiny++;
        state.lastCaptureSlot = target.slot;
        say(target.shiny ? '✨ Chromatique capturé !' : `${target.name} capturé !`);
      } else {
        state.missedCount = (state.missedCount || 0) + 1;
        say('Raté, il est toujours là.');
      }
      saveState();
      render();
    }, 700);
  }

  /* -------------------------------- Pokédex ------------------------------- */
  // minuscules + sans accents, pour que "pikachu", "Évoli" et "evoli" matchent
  const norm = (s) => String(s).normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim();

  const GENERATIONS = [
    [1, 151],
    [152, 251],
    [252, 386],
    [387, 493],
    [494, 649],
    [650, 721],
    [722, 809],
    [810, 905],
    [906, 1025],
  ];

  function matchesRange(id, raw) {
    const parts = raw.split('&').map((s) => s.trim()).filter(Boolean);
    if (!parts.length) return false;
    for (const part of parts) {
      const m = part.match(/^(<=|>=|<|>)\s*(\d+)$/);
      if (!m) return false;
      const op = m[1];
      const val = parseInt(m[2], 10);
      if (op === '<' && !(id < val)) return false;
      if (op === '<=' && !(id <= val)) return false;
      if (op === '>' && !(id > val)) return false;
      if (op === '>=' && !(id >= val)) return false;
    }
    return true;
  }

  function matchesSingle(id, name, hasShiny, hasMystery, hasDuplicate, q) {
    if (!q) return true;
    if (q === 'shiny' || q === 'chromatique') return hasShiny;
    if (q === 'mystere' || q === 'mystère') return hasMystery;
    if (q === 'doublon' || q === 'doublons') return hasDuplicate;

    const genM = q.match(/^gen(\d+)$/);
    if (genM) {
      const g = parseInt(genM[1], 10);
      if (g < 1 || g > GENERATIONS.length) return false;
      const [from, to] = GENERATIONS[g - 1];
      return id >= from && id <= to;
    }

    const exact = q.startsWith('#');
    const qn = exact ? q.slice(1) : q;
    if (!qn) return true;
    if (/^\d+$/.test(qn)) {
      return exact ? String(id) === qn : String(id).startsWith(qn);
    }
    return name.includes(q);
  }

  function matchesDexQuery(id, name, hasShiny, hasMystery, hasDuplicate, query) {
    const raw = norm(query);
    if (!raw) return true;
    if (raw.includes(',')) {
      return raw.split(',').map((s) => s.trim()).filter(Boolean).some((term) => matchesSingle(id, name, hasShiny, hasMystery, hasDuplicate, term));
    }
    if (/[<>&]/.test(raw)) {
      return matchesRange(id, raw);
    }
    return matchesSingle(id, name, hasShiny, hasMystery, hasDuplicate, raw);
  }

  function openDex() {
    const ids = Object.keys(state.dex).map(Number).sort((a, b) => a - b);
    const total = ids.reduce((n, id) => n + state.dex[id].count, 0);

    const overlay = document.createElement('div');
    overlay.className = 'pk-overlay';
    const box = document.createElement('div');
    box.className = 'pk-panel';
    box.innerHTML = `
      <div class="pk-panel-head">
        <h2>Pokédex : ${ids.length} / ${CFG.MAX_POKEMON_ID} espèces, ${total} captures</h2>
        <button class="pk-icon" data-close aria-label="Fermer">✕</button>
      </div>`;

    const search = document.createElement('input');
    search.type = 'search';
    search.className = 'pk-search';
    search.placeholder = 'Rechercher (ex. pika, #25, doublon, mystère, shiny, gen1, <30)';
    search.setAttribute('aria-label', 'Rechercher dans le Pokédex');
    if (ids.length) box.appendChild(search);

    const noRes = document.createElement('p');
    noRes.className = 'pk-empty';
    noRes.textContent = 'Aucun Pokémon ne correspond à cette recherche.';
    noRes.hidden = true;

    const grid = document.createElement('div');
    grid.className = 'pk-grid';
    if (!ids.length) {
      const p = document.createElement('p');
      p.className = 'pk-empty';
      p.textContent = 'Aucun Pokémon capturé pour le moment. Lance une ball sur celui qui est apparu.';
      box.appendChild(p);
    }

    // Les sprites ne sont chargés que quand la carte devient visible
    const io = new IntersectionObserver((entries) => {
      for (const en of entries) {
        if (!en.isIntersecting) continue;
        io.unobserve(en.target);
        const img = en.target;
        spriteDataUrl(img.dataset.src).then((u) => { img.src = u; }).catch(() => { });
      }
    }, { root: box, rootMargin: '120px' });

    const cards = [];
    for (const id of ids) {
      const e = state.dex[id];
      const card = document.createElement('div');
      card.className = 'pk-card';
      const img = document.createElement('img');
      img.alt = e.name;
      img.dataset.src = `${SPRITES}${e.shiny ? 'shiny/' : ''}${id}.png`;
      const num = document.createElement('div');
      num.className = 'pk-card-id'; num.textContent = `#${id}`;
      const n = document.createElement('div');
      n.className = 'pk-card-name'; n.textContent = e.name;
      const c = document.createElement('div');
      c.className = 'pk-card-count';
      c.textContent = `×${e.count}` + (e.shiny ? `  ✨×${e.shiny}` : '') + (e.mystery ? `  🌀×${e.mystery}` : '');
      card.append(img, num, n, c);
      grid.appendChild(card);
      const normalCount = Math.max(0, (e.count || 0) - (e.shiny || 0));
      const hasDuplicate = normalCount > 1 || (e.shiny || 0) > 1;
      cards.push({ card, id, name: norm(e.name), shiny: (e.shiny || 0) > 0, mystery: (e.mystery || 0) > 0, duplicate: hasDuplicate });
    }
    box.append(noRes, grid);
    overlay.appendChild(box);
    document.body.appendChild(overlay);
    cards.forEach(({ card }) => io.observe(card.querySelector('img')));

    function applyFilter() {
      let shown = 0;
      for (const c of cards) {
        const ok = matchesDexQuery(c.id, c.name, c.shiny, c.mystery, c.duplicate, search.value);
        c.card.hidden = !ok;
        if (ok) shown++;
      }
      noRes.hidden = shown > 0 || !cards.length;
    }
    search.addEventListener('input', applyFilter);
    if (ids.length) search.focus();

    const closeIt = () => { io.disconnect(); overlay.remove(); document.removeEventListener('keydown', onKey); };
    const onKey = (ev) => { if (ev.key === 'Escape') closeIt(); };
    overlay.addEventListener('click', (ev) => { if (ev.target === overlay || ev.target.closest('[data-close]')) closeIt(); });
    document.addEventListener('keydown', onKey);
  }

  /* -------------------------------- Boutique ------------------------------- */
  function openShop() {
    const overlay = document.createElement('div');
    overlay.className = 'pk-overlay';
    const box = document.createElement('div');
    box.className = 'pk-panel pk-shop-panel';
    box.style.maxWidth = '520px';

    box.innerHTML = `
      <div class="pk-panel-head">
        <h2>Boutique Pokémon</h2>
        <button class="pk-icon" data-close aria-label="Fermer">✕</button>
      </div>
      <p class="pk-hint" style="margin:0 0 14px">Échange tes doublons contre des améliorations ou un Pokémon mystère.</p>
      
      <div class="pk-shop-balance">
        <div style="font-weight:600;font-size:13px;margin-bottom:6px">Tes doublons disponibles :</div>
        <div style="display:flex;gap:10px;flex-wrap:wrap">
          <span class="pk-badge pk-badge-gold"><b id="shopDoublons">0</b> doublon(s)</span>
          <span class="pk-badge pk-badge-blue">✨ <b id="shopShinyDoublons">0</b> doublon(s) chromatique(s)</span>
        </div>
        <small class="pk-hint" style="margin-top:8px">Gagne 1 doublon pour chaque exemplaire en double d'une espèce (le 1er normal et le 1er shiny sont la base). Tes captures restent intactes dans ton Pokédex.</small>
      </div>

      <div class="pk-shop-list">
        <div class="pk-shop-card">
          <div class="pk-shop-card-info">
            <div class="pk-shop-card-title">🌀 Pokémon mystère</div>
            <div class="pk-shop-card-desc">Donne un Pokémon aléatoire qui peut être chromatique. Achat illimité.</div>
            <div class="pk-shop-card-cost" style="color:#ffcb05">Coût : 10 doublons</div>
          </div>
          <button class="pk-btn pk-primary" id="btnShopMystery">Acheter</button>
        </div>

        <div class="pk-shop-card">
          <div class="pk-shop-card-info">
            <div class="pk-shop-card-title">⚪ Capacité des Balls (+10)</div>
            <div class="pk-shop-card-desc" id="shopBallCapDesc">+10 à la capacité maximale de chaque type de ball (max 150).</div>
            <div class="pk-shop-card-cost" id="shopBallCapCost" style="color:#ffcb05">Coût : 5 doublons • 0/5 achats</div>
          </div>
          <button class="pk-btn pk-primary" id="btnShopBallCap">Acheter</button>
        </div>

        <div class="pk-shop-card">
          <div class="pk-shop-card-info">
            <div class="pk-shop-card-title">🎯 Persévérance (+0.02)</div>
            <div class="pk-shop-card-desc">+0.02 au taux de capture de l'espèce par Poké Ball manquée sur le Pokémon en cours.</div>
            <div class="pk-shop-card-cost" id="shopMissRateCost" style="color:#ffcb05">Coût : 100 doublons • 0/1 achat</div>
          </div>
          <button class="pk-btn pk-primary" id="btnShopMissRate">Acheter</button>
        </div>

        <div class="pk-shop-card">
          <div class="pk-shop-card-info">
            <div class="pk-shop-card-title">✨ Charme chromatique (+25 %)</div>
            <div class="pk-shop-card-desc">+25 % de chance d'apparition de chromatiques (sauvages et mystères).</div>
            <div class="pk-shop-card-cost" id="shopShinyBoostCost" style="color:#4fc3f7">Coût : 1 doublon chromatique • 0/1 achat</div>
          </div>
          <button class="pk-btn pk-primary" id="btnShopShinyBoost">Acheter</button>
        </div>
      </div>

      <div class="pk-reveal-overlay" id="shopRevealOverlay" style="display:none">
        <div style="font-weight:700;font-size:16px;margin-bottom:16px" id="revealTitle">La Poké Ball s'agite…</div>
        <div id="revealBallStage">
          <span class="pk-ball pk-poke pk-reveal-ball pk-reveal-wobble"></span>
        </div>
        <div class="pk-reveal-result" id="revealResultStage" style="display:none">
          <div class="pk-reveal-stage" id="revealResultBg">
            <img class="pk-reveal-sprite" id="revealSprite" alt="">
          </div>
          <div id="revealNum" style="opacity:.6;font-size:12px"></div>
          <div id="revealName" style="font-weight:700;font-size:18px"></div>
          <div id="revealShiny" style="font-weight:700;font-size:13px;color:#ffcb05;margin:2px 0;display:none">✨ Chromatique ! ✨</div>
          <div id="revealBadges" style="opacity:.8;font-size:13px;margin:4px 0 16px"></div>
          <button class="pk-btn pk-primary" id="btnDismissReveal" style="min-width:140px">Continuer</button>
        </div>
      </div>
    `;

    overlay.appendChild(box);
    document.body.appendChild(overlay);

    const refreshShopUi = () => {
      state.shop = state.shop || { ballCapUpgrades: 0, missCatchRateUpgrades: 0, shinyBoostUpgrades: 0, spentDoublons: 0, spentShinyDoublons: 0 };
      const { available, availableShiny } = getAvailableDoublons();
      box.querySelector('#shopDoublons').textContent = available;
      box.querySelector('#shopShinyDoublons').textContent = availableShiny;

      const btnMystery = box.querySelector('#btnShopMystery');
      btnMystery.disabled = available < 10;

      const btnBallCap = box.querySelector('#btnShopBallCap');
      const capLvl = state.shop.ballCapUpgrades || 0;
      box.querySelector('#shopBallCapDesc').textContent = `+10 à la capacité maximale de chaque type de ball (actuel : ${maxBallCap()}).`;
      box.querySelector('#shopBallCapCost').textContent = capLvl >= 5 ? 'Niveau max atteint (150 balls)' : `Coût : 5 doublons • ${capLvl}/5 achats`;
      btnBallCap.textContent = capLvl >= 5 ? 'Max' : 'Acheter';
      btnBallCap.disabled = capLvl >= 5 || available < 5;

      const btnMiss = box.querySelector('#btnShopMissRate');
      const missLvl = state.shop.missCatchRateUpgrades || 0;
      box.querySelector('#shopMissRateCost').textContent = missLvl >= 1 ? 'Amélioration active' : 'Coût : 100 doublons • 0/1 achat';
      btnMiss.textContent = missLvl >= 1 ? 'Acheté' : 'Acheter';
      btnMiss.disabled = missLvl >= 1 || available < 100;

      const btnShiny = box.querySelector('#btnShopShinyBoost');
      const shinyLvl = state.shop.shinyBoostUpgrades || 0;
      box.querySelector('#shopShinyBoostCost').textContent = shinyLvl >= 1 ? 'Amélioration active' : 'Coût : 1 doublon chromatique • 0/1 achat';
      btnShiny.textContent = shinyLvl >= 1 ? 'Acheté' : 'Acheter';
      btnShiny.disabled = shinyLvl >= 1 || availableShiny < 1;
    };

    refreshShopUi();

    box.querySelector('#btnShopBallCap').addEventListener('click', () => {
      const { available } = getAvailableDoublons();
      if ((state.shop.ballCapUpgrades || 0) < 5 && available >= 5) {
        state.shop.spentDoublons = (state.shop.spentDoublons || 0) + 5;
        state.shop.ballCapUpgrades = (state.shop.ballCapUpgrades || 0) + 1;
        saveState();
        refreshShopUi();
        render();
      }
    });

    box.querySelector('#btnShopMissRate').addEventListener('click', () => {
      const { available } = getAvailableDoublons();
      if ((state.shop.missCatchRateUpgrades || 0) < 1 && available >= 100) {
        state.shop.spentDoublons = (state.shop.spentDoublons || 0) + 100;
        state.shop.missCatchRateUpgrades = 1;
        saveState();
        refreshShopUi();
        render();
      }
    });

    box.querySelector('#btnShopShinyBoost').addEventListener('click', () => {
      const { availableShiny } = getAvailableDoublons();
      if ((state.shop.shinyBoostUpgrades || 0) < 1 && availableShiny >= 1) {
        state.shop.spentShinyDoublons = (state.shop.spentShinyDoublons || 0) + 1;
        state.shop.shinyBoostUpgrades = 1;
        saveState();
        refreshShopUi();
        render();
      }
    });

    let busyMystery = false;
    box.querySelector('#btnShopMystery').addEventListener('click', async () => {
      const { available } = getAvailableDoublons();
      if (busyMystery || available < 10) return;
      busyMystery = true;

      const id = 1 + Math.floor(Math.random() * CFG.MAX_POKEMON_ID);
      const shiny = Math.random() < getShinyRate();

      state.shop.spentDoublons = (state.shop.spentDoublons || 0) + 10;
      saveState();
      refreshShopUi();

      let info;
      try { info = await getSpecies(id); } catch { info = { name: `#${id}`, rate: 45 }; }

      const e = state.dex[id] || (state.dex[id] = { name: info.name, rate: info.rate, count: 0, shiny: 0, mystery: 0 });
      e.count++;
      if (shiny) e.shiny++;
      e.mystery = (e.mystery || 0) + 1;
      saveState();
      render();

      const overlayReveal = box.querySelector('#shopRevealOverlay');
      const ballStage = box.querySelector('#revealBallStage');
      const resultStage = box.querySelector('#revealResultStage');
      const title = box.querySelector('#revealTitle');
      const spriteImg = box.querySelector('#revealSprite');
      const num = box.querySelector('#revealNum');
      const name = box.querySelector('#revealName');
      const shinyLabel = box.querySelector('#revealShiny');
      const badges = box.querySelector('#revealBadges');
      const resultBg = box.querySelector('#revealResultBg');

      box.scrollTop = 0;
      overlayReveal.style.display = 'flex';
      title.textContent = "La Poké Ball s'agite…";
      ballStage.style.display = 'block';
      resultStage.style.display = 'none';

      const ballEl = ballStage.querySelector('.pk-reveal-ball');
      ballEl.classList.remove('pk-reveal-wobble');
      void ballEl.offsetWidth; // re-trigger animation
      ballEl.classList.add('pk-reveal-wobble');

      // Préchargement sprite
      const spriteUrl = `${SPRITES}${shiny ? 'shiny/' : ''}${id}.png`;
      spriteDataUrl(spriteUrl).then((u) => { spriteImg.src = u; }).catch(() => { spriteImg.src = spriteUrl; });

      setTimeout(() => {
        ballStage.style.display = 'none';
        resultStage.style.display = 'flex';
        title.textContent = shiny ? '✨ Un Pokémon chromatique mystère !' : 'Pokémon mystère obtenu !';
        num.textContent = `#${id}`;
        name.textContent = info.name;
        shinyLabel.style.display = shiny ? 'block' : 'none';
        resultBg.className = `pk-reveal-stage ${shiny ? 'pk-shiny' : ''}`;
        badges.textContent = `×${e.count}` + (e.shiny ? `  ✨×${e.shiny}` : '') + `  🌀×${e.mystery}`;
        busyMystery = false;
        refreshShopUi();
      }, 1300);
    });

    box.querySelector('#btnDismissReveal').addEventListener('click', () => {
      box.querySelector('#shopRevealOverlay').style.display = 'none';
      refreshShopUi();
    });

    const closeIt = () => { overlay.remove(); document.removeEventListener('keydown', onKey); };
    const onKey = (ev) => {
      if (ev.key === 'Escape') {
        const rev = box.querySelector('#shopRevealOverlay');
        if (rev && rev.style.display === 'flex' && !busyMystery) {
          rev.style.display = 'none';
          refreshShopUi();
        } else if (!rev || rev.style.display === 'none') {
          closeIt();
        }
      }
    };
    overlay.addEventListener('click', (ev) => {
      if (ev.target === overlay || ev.target.closest('[data-close]')) closeIt();
    });
    document.addEventListener('keydown', onKey);
  }

  /* --------------------------------- Aide --------------------------------- */
  // Les chiffres sont lus dans CFG pour que l'aide reste exacte si on les modifie.
  function openHelp() {
    const slotMin = Math.round(CFG.SLOT_MS / 60000);
    const ballH = Math.round(CFG.BALL_MS / 3600000);
    const bonusPct = Math.round(CFG.BONUS_CHANCE * 100);
    const shinyDen = Math.round(1 / CFG.SHINY_RATE);

    const overlay = document.createElement('div');
    overlay.className = 'pk-overlay';
    const box = document.createElement('div');
    box.className = 'pk-panel pk-help';
    box.innerHTML = `
      <div class="pk-panel-head">
        <h2>Comment jouer ?</h2>
        <button class="pk-icon" data-close aria-label="Fermer">✕</button>
      </div>

      <h3>Le Pokémon du moment</h3>
      <p>Un nouveau Pokémon apparaît toutes les ${slotMin} minutes. Il est tiré à partir de la seed : toutes les personnes qui ont la même seed voient le même Pokémon au même moment. Tu peux changer la seed dans les réglages ⚙️.</p>
      <p>Chaque apparition a 1 chance sur ${shinyDen} d'être chromatique (✨).</p>

      <h3>Capturer</h3>
      <ul>
        <li>Choisis une ball et lance-la : le pourcentage affiché dessous est ta chance de capture.</li>
        <li>Chaque lancer consomme une ball, qu'il réussisse ou non. Si c'est raté, tu peux réessayer tant qu'il te reste des balls et du temps.</li>
        <li>Tu ne peux capturer qu'un Pokémon par apparition. Une fois capturé, il s'estompe jusqu'au suivant.</li>
        <li>Plus le taux de capture du Pokémon est bas, plus il est difficile à attraper.</li>
      </ul>

      <h3>Les balls</h3>
      <ul>
        <li><span class="pk-ball pk-poke"></span><b>Poké Ball</b> : chance de base.</li>
        <li><span class="pk-ball pk-super"></span><b>Super Ball</b> : ×1,5 sur la chance de capture.</li>
        <li><span class="pk-ball pk-hyper"></span><b>Hyper Ball</b> : ×2 sur la chance de capture.</li>
      </ul>
      <p>Tu démarres avec ${CFG.START_BALLS} Poké Balls, et tu ne peux pas dépasser ${CFG.BALL_CAP} balls de chaque type (extensible jusqu'à 150 en boutique).</p>

      <h3>Gagner des balls</h3>
      <ul>
        <li>+1 Poké Ball toutes les ${ballH} heure${ballH > 1 ? 's' : ''}. Le temps compte même si tu n'ouvres pas les pages où le widget s'affiche : les balls se rattrapent dès qu'une page avec le script se charge.</li>
        <li>Bonus : chaque heure, <b>chaque capture de ton Pokédex</b> a ${bonusPct} % de chance de te donner une ball supplémentaire. Capturer deux fois la même espèce compte deux fois.</li>
        <li>Le type de ball offerte dépend du taux de capture de l'espèce : Poké Ball à partir de 120, Super Ball de 46 à 119, Hyper Ball à 45 ou moins. Les Pokémon difficiles à attraper rapportent donc de meilleures balls.</li>
      </ul>

      <h3>Boutique et doublons</h3>
      <p>Chaque Pokémon déjà présent dans ton Pokédex capturé à nouveau te rapporte un doublon (et un doublon chromatique pour chaque shiny en double). Tes captures restent intactes dans ton Pokédex !</p>
      <ul>
        <li><b>Pokémon mystère 🌀</b> (10 doublons) : donne un Pokémon aléatoire (peut être chromatique), avec un badge 🌀 spécifique dans le Pokédex.</li>
        <li><b>Capacité des balls (+10)</b> (5 doublons) : jusqu'à 5 améliorations (maximum 150 balls par type).</li>
        <li><b>Persévérance</b> (100 doublons) : augmente de +0.02 le taux de capture de l'espèce par Poké Ball manquée sur le Pokémon en cours.</li>
        <li><b>Charme chromatique</b> (1 doublon chromatique) : +25 % de chance d'apparition de chromatiques.</li>
      </ul>

      <h3>Pokédex et recherche</h3>
      <p>Le Pokédex 📖 liste tes captures. Tu peux rechercher par nom, par numéro (#25, &lt;30), par génération (gen1), mais aussi par mots-clés : « shiny », « mystère » (Pokémon obtenus en boutique) et « doublon » (Pokémon que tu possèdes en plusieurs exemplaires). Dans les réglages, tu peux exporter ta progression en fichier .pokesave et la réimporter plus tard.</p>

      <h3>Réduire le widget</h3>
      <p>Le bouton − réduit le widget à une Poké Ball. Elle se balance quand un nouveau Pokémon est apparu, et brille en doré quand un chromatique est là.</p>`;
    overlay.appendChild(box);
    document.body.appendChild(overlay);

    const closeIt = () => { overlay.remove(); document.removeEventListener('keydown', onKey); };
    const onKey = (ev) => { if (ev.key === 'Escape') closeIt(); };
    overlay.addEventListener('click', (ev) => { if (ev.target === overlay || ev.target.closest('[data-close]')) closeIt(); });
    document.addEventListener('keydown', onKey);
  }

  /* ---------------------------- Export / Import --------------------------- */
  /* ---------------------------- Export / Import (.pokesave) --------------------------- */

  // Constantes d'obfuscation / chiffrement (identiques à SaveObfuscator.cs)
  const SAVE_MAGIC = new Uint8Array([0x50, 0x4B, 0x57, 0x31]); // "PKW1"
  const NONCE_SIZE = 12;
  const TAG_SIZE = 16;
  const HEADER_SIZE = 4 + NONCE_SIZE + TAG_SIZE;
  const KEY_STRING = "poke-widget/save/v1/ulgrude:7f3a9c1e";

  async function getObfuscationKey() {
    const enc = new TextEncoder();
    const keyBytes = await crypto.subtle.digest('SHA-256', enc.encode(KEY_STRING));
    return crypto.subtle.importKey('raw', keyBytes, { name: 'AES-GCM' }, false, ['encrypt', 'decrypt']);
  }

  // Chiffrement : JSON → GZip → AES-GCM (Format: "PKW1" | Nonce 12B | Tag 16B | Cipher)
  async function protectSave(jsonString) {
    const enc = new TextEncoder();
    const plainBytes = enc.encode(jsonString);

    // 1. Compression GZip avec CompressionStream
    const cs = new CompressionStream('gzip');
    const writer = cs.writable.getWriter();
    writer.write(plainBytes);
    writer.close();
    const compressedBuffer = await new Response(cs.readable).arrayBuffer();
    const compressedBytes = new Uint8Array(compressedBuffer);

    // 2. Chiffrement AES-GCM (tag 128-bit = 16 octets)
    const key = await getObfuscationKey();
    const nonce = crypto.getRandomValues(new Uint8Array(NONCE_SIZE));

    const encryptedBuffer = await crypto.subtle.encrypt(
      { name: 'AES-GCM', iv: nonce, tagLength: 128, additionalData: SAVE_MAGIC },
      key,
      compressedBytes
    );

    // En WebCrypto, le Tag (16B) est automatiquement concaténé à la fin du ciphertext
    const encryptedBytes = new Uint8Array(encryptedBuffer);
    const cipherLength = encryptedBytes.length - TAG_SIZE;
    const cipher = encryptedBytes.subarray(0, cipherLength);
    const tag = encryptedBytes.subarray(cipherLength);

    // 3. Construction du header "PKW1" | Nonce | Tag | Cipher
    const output = new Uint8Array(HEADER_SIZE + cipherLength);
    output.set(SAVE_MAGIC, 0);
    output.set(nonce, 4);
    output.set(tag, 4 + NONCE_SIZE);
    output.set(cipher, HEADER_SIZE);

    return output;
  }

  // Déchiffrement
  async function unprotectSave(dataBytes) {
    if (dataBytes.length < HEADER_SIZE) throw new Error("Fichier de sauvegarde corrompu ou incomplet");

    for (let i = 0; i < SAVE_MAGIC.length; i++) {
      if (dataBytes[i] !== SAVE_MAGIC[i]) throw new Error("Fichier de sauvegarde non reconnu (.pokesave attendu)");
    }

    const nonce = dataBytes.subarray(4, 4 + NONCE_SIZE);
    const tag = dataBytes.subarray(4 + NONCE_SIZE, HEADER_SIZE);
    const cipher = dataBytes.subarray(HEADER_SIZE);

    // Reconstituer le buffer attendu par WebCrypto (Ciphertext + Tag)
    const ciphertextWithTag = new Uint8Array(cipher.length + TAG_SIZE);
    ciphertextWithTag.set(cipher, 0);
    ciphertextWithTag.set(tag, cipher.length);

    const key = await getObfuscationKey();
    let decryptedBuffer;
    try {
      decryptedBuffer = await crypto.subtle.decrypt(
        { name: 'AES-GCM', iv: nonce, tagLength: 128, additionalData: SAVE_MAGIC },
        key,
        ciphertextWithTag
      );
    } catch (e) {
      throw new Error("Sauvegarde modifiée ou corrompue");
    }

    // Décompression GZip
    const ds = new DecompressionStream('gzip');
    const writer = ds.writable.getWriter();
    writer.write(decryptedBuffer);
    writer.close();

    const decompressedBuffer = await new Response(ds.readable).arrayBuffer();
    return new TextDecoder().decode(decompressedBuffer);
  }

  // Export au format .pokesave
  async function exportProgress() {
    const payload = {
      app: 'poke-widget',
      version: 1,
      exportedAt: new Date().toISOString(),
      state,
      config,
    };

    const jsonString = JSON.stringify(payload);
    const protectedBytes = await protectSave(jsonString);

    const blob = new Blob([protectedBytes], { type: 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `pokedex-${new Date().toISOString().slice(0, 10)}.pokesave`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
  }

  // On ne fait jamais confiance au fichier importé : tout est borné/validé.
  function sanitizeState(raw) {
    if (!raw || typeof raw !== 'object' || (!raw.dex && !raw.balls)) throw new Error('fichier non reconnu');
    const int = (v, min, max, def) => {
      const n = Math.floor(Number(v));
      return Number.isFinite(n) ? Math.min(max, Math.max(min, n)) : def;
    };
    const s = freshState();
    if (isPlayerId(raw.playerId)) s.playerId = raw.playerId.toLowerCase();
    if (raw.shop && typeof raw.shop === 'object') {
      s.shop = {
        ballCapUpgrades: int(raw.shop.ballCapUpgrades, 0, 5, 0),
        missCatchRateUpgrades: int(raw.shop.missCatchRateUpgrades, 0, 1, 0),
        shinyBoostUpgrades: int(raw.shop.shinyBoostUpgrades, 0, 1, 0),
        spentDoublons: int(raw.shop.spentDoublons, 0, 1e6, 0),
        spentShinyDoublons: int(raw.shop.spentShinyDoublons, 0, 1e6, 0),
      };
    }
    const cap = CFG.BALL_CAP + (((s.shop && s.shop.ballCapUpgrades) || 0) * 10);
    const b = raw.balls || {};
    s.balls = {
      poke: int(b.poke, 0, cap, 0),
      super: int(b.super, 0, cap, 0),
      hyper: int(b.hyper, 0, cap, 0),
    };
    s.lastTick = Number.isFinite(raw.lastTick) ? Math.min(raw.lastTick, Date.now()) : Date.now();
    s.lastCaptureSlot = Number.isFinite(raw.lastCaptureSlot) ? raw.lastCaptureSlot : null;
    s.seenSlot = Number.isFinite(raw.seenSlot) ? raw.seenSlot : null;
    s.dex = {};
    if (raw.dex && typeof raw.dex === 'object') {
      for (const [k, e] of Object.entries(raw.dex)) {
        const id = Number(k);
        if (!Number.isInteger(id) || id < 1 || id > CFG.MAX_POKEMON_ID || !e || typeof e !== 'object') continue;
        const count = int(e.count, 1, 1e6, 1);
        s.dex[id] = {
          name: String(e.name || `#${id}`).slice(0, 60),
          rate: int(e.rate, 1, 255, 45),
          count,
          shiny: int(e.shiny, 0, count, 0),
          mystery: int(e.mystery, 0, count, 0),
        };
      }
    }
    return s;
  }

  function sanitizeConfig(raw) {
    if (!raw || typeof raw !== 'object') return null;
    return {
      seed: typeof raw.seed === 'string' && raw.seed.trim() ? raw.seed.trim().slice(0, 100) : CFG.DEFAULT_SEED,
      position: Object.keys(POSITIONS).includes(raw.position) ? raw.position : 'bottom-right',
      urls: Array.isArray(raw.urls)
        ? raw.urls.filter((u) => typeof u === 'string' && u.trim()).map((u) => u.trim().slice(0, 300))
        : [...DEFAULT_URLS],
    };
  }

  /* -------------------------------- Réglages ------------------------------- */
  function openSettings() {
    // copie de travail, appliquée seulement au clic sur "Enregistrer"
    const draft = { seed: config.seed, position: config.position, urls: [...config.urls] };

    const overlay = document.createElement('div');
    overlay.className = 'pk-overlay';
    const box = document.createElement('div');
    box.className = 'pk-panel';
    box.innerHTML = `
      <div class="pk-panel-head">
        <h2>Réglages du widget Pokémon</h2>
        <button class="pk-icon" data-close aria-label="Fermer">✕</button>
      </div>

      <label class="pk-field">
        <span>Seed</span>
        <input type="text" id="pkSeed" value="${draft.seed.replace(/"/g, '&quot;')}">
        <small class="pk-hint">Les personnes qui utilisent la même seed voient le même Pokémon au même moment.</small>
      </label>

      <div class="pk-field">
        <span>Position du widget</span>
        <div class="pk-posgrid" id="pkPosGrid">
          ${Object.entries(POSITIONS).map(([k, label]) =>
      `<button type="button" class="pk-posbtn${k === draft.position ? ' pk-active' : ''}" data-pos="${k}">${label}</button>`
    ).join('')}
        </div>
      </div>

      <div class="pk-field">
        <span>Pages où le widget apparaît</span>
        <div id="pkUrlList"></div>
        <button type="button" class="pk-addurl" id="pkAddUrl">+ Ajouter une page</button>
        <small class="pk-hint">Utilise * comme joker, ex. https://discord.com/channels/*.</small>
      </div>

      <div class="pk-field">
        <span>Sauvegarde de la progression</span>
        <div class="pk-row">
          <button type="button" class="pk-btn" id="pkExport">Exporter (.pokesave)</button>
          <button type="button" class="pk-btn" id="pkImport">Importer…</button>
          <!-- Accepter UNIQUEMENT l'extension .pokesave -->
          <input type="file" id="pkImportFile" accept=".pokesave" hidden>
        </div>
        <small class="pk-hint" id="pkBackupMsg">Le fichier chiffré .pokesave contient tes balls, ton Pokédex et tes réglages.</small>
      </div>

      <div class="pk-field">
        <span>Mises à jour</span>
        <div class="pk-row">
          <button type="button" class="pk-btn" id="pkCheckUpdate">Vérifier maintenant</button>
        </div>
        <small class="pk-hint" id="pkUpdateMsg">Version installée : ${INSTALLED_VERSION}. Compare avec la version du fichier sur le GitHub, vérifiée automatiquement une fois par jour.</small>
      </div>

      <div class="pk-field">
        <span>Communauté</span>
        <div class="pk-row">
          <a href="https://discord.gg/6PtyhKa7GA" target="_blank" rel="noopener noreferrer" class="pk-btn" style="text-decoration:none;display:inline-flex;align-items:center;gap:6px;">💬 Rejoindre le Discord</a>
        </div>
      </div>

      <div class="pk-panel-actions">
        <button type="button" class="pk-btn" id="pkCancel">Annuler</button>
        <button type="button" class="pk-btn pk-primary" id="pkSave">Enregistrer</button>
      </div>`;
    overlay.appendChild(box);
    document.body.appendChild(overlay);

    // -- Position --
    box.querySelectorAll('[data-pos]').forEach((btn) => {
      btn.addEventListener('click', () => {
        draft.position = btn.dataset.pos;
        box.querySelectorAll('[data-pos]').forEach((b) => b.classList.toggle('pk-active', b === btn));
      });
    });

    // -- Liste d'URLs --
    const listEl = box.querySelector('#pkUrlList');
    function renderUrlRows() {
      listEl.innerHTML = '';
      draft.urls.forEach((url, i) => {
        const row = document.createElement('div');
        row.className = 'pk-urlrow';
        row.innerHTML = `<input type="text" value="${url.replace(/"/g, '&quot;')}"><button type="button" title="Supprimer">✕</button>`;
        row.querySelector('input').addEventListener('input', (e) => { draft.urls[i] = e.target.value; });
        row.querySelector('button').addEventListener('click', () => { draft.urls.splice(i, 1); renderUrlRows(); });
        listEl.appendChild(row);
      });
    }
    renderUrlRows();
    box.querySelector('#pkAddUrl').addEventListener('click', () => { draft.urls.push(''); renderUrlRows(); });

    // -- Fermeture --
    const closeIt = () => { overlay.remove(); document.removeEventListener('keydown', onKey); };
    const onKey = (ev) => { if (ev.key === 'Escape') closeIt(); };
    overlay.addEventListener('click', (ev) => { if (ev.target === overlay || ev.target.closest('[data-close]')) closeIt(); });
    box.querySelector('#pkCancel').addEventListener('click', closeIt);
    document.addEventListener('keydown', onKey);

    // -- Export / Import --
    const backupMsg = box.querySelector('#pkBackupMsg');
    const fileInput = box.querySelector('#pkImportFile');
    // Exporter
    box.querySelector('#pkExport').addEventListener('click', async () => {
      try {
        await exportProgress();
        backupMsg.textContent = 'Export (.pokesave) lancé : vérifie ton dossier de téléchargements.';
      } catch (e) {
        backupMsg.textContent = 'Export impossible : ' + e.message;
      }
    });

    // Importer
    box.querySelector('#pkImport').addEventListener('click', () => fileInput.click());

    fileInput.addEventListener('change', async () => {
      const file = fileInput.files[0];
      fileInput.value = '';
      if (!file) return;

      try {
        if (file.size > 5 * 1024 * 1024) throw new Error('fichier trop volumineux');
        if (!file.name.endsWith('.pokesave')) throw new Error('seuls les fichiers .pokesave sont autorisés');

        const arrayBuffer = await file.arrayBuffer();
        const dataBytes = new Uint8Array(arrayBuffer);

        // Déchiffrement et décompression
        const unprotectedJson = await unprotectSave(dataBytes);
        const raw = JSON.parse(unprotectedJson);

        if (!raw || typeof raw !== 'object') throw new Error('fichier non reconnu');

        const newState = sanitizeState(raw.state || raw);
        const newConfig = raw.config ? sanitizeConfig(raw.config) : null;
        const species = Object.keys(newState.dex).length;

        const ok = confirm(
          'Remplacer ta progression actuelle par celle du fichier ?\n\n' +
          `• ${species} espèce${species > 1 ? 's' : ''} dans le Pokédex\n` +
          `• Poké ${newState.balls.poke} / Super ${newState.balls.super} / Hyper ${newState.balls.hyper}` +
          (newConfig ? '\n• Réglages (seed, position, pages) restaurés aussi' : '')
        );
        if (!ok) return;

        state = newState;
        saveState();
        if (newConfig) { config = newConfig; saveConfig(config); }
        closeIt();
        applyPosition();
        evaluateMount();
        current = null; loadingSlot = null;
        render();
        loadSpawn();
        say('Progression importée.');
      } catch (e) {
        backupMsg.textContent = 'Import impossible : ' + e.message;
      }
    });

    // -- Mises à jour --
    const updMsg = box.querySelector('#pkUpdateMsg');
    box.querySelector('#pkCheckUpdate').addEventListener('click', async () => {
      updMsg.textContent = 'Vérification…';
      try {
        await checkForUpdate({ force: true });
        refreshUpdateUi();
        if (updateAvailable()) {
          const a = document.createElement('a');
          a.href = CFG.SCRIPT_URL; a.target = '_blank'; a.rel = 'noopener';
          a.textContent = `Installer la version ${updateInfo.latest}`;
          a.style.color = '#ffcb05';
          updMsg.textContent = `Version ${INSTALLED_VERSION} installée, ${updateInfo.latest} disponible. `;
          updMsg.appendChild(a);
        } else {
          updMsg.textContent = `À jour (version ${INSTALLED_VERSION}).`;
        }
      } catch (e) {
        const why = e && e.status === 404 ? 'fichier introuvable (vérifie CFG.SCRIPT_URL)'
          : e && e.status ? `erreur HTTP ${e.status}`
            : (e && e.message) || 'GitHub injoignable';
        updMsg.textContent = 'Vérification impossible : ' + why;
      }
    });

    // -- Enregistrement --
    box.querySelector('#pkSave').addEventListener('click', () => {
      const seedInput = box.querySelector('#pkSeed').value.trim();
      const seedChanged = seedInput !== config.seed && seedInput !== '';

      config = {
        seed: seedInput || CFG.DEFAULT_SEED,
        position: draft.position,
        urls: draft.urls.map((u) => u.trim()).filter(Boolean),
      };
      saveConfig(config);
      closeIt();

      applyPosition();
      evaluateMount();
      if (seedChanged) {
        current = null; loadingSlot = null;
        render();
        loadSpawn();
      } else {
        render();
      }
    });
  }

  /* ========================================================================
     9. Démarrage
     ===================================================================== */
  // Ces panneaux n'ont pas besoin que le widget soit affiché sur la page
  // courante : on peut les ouvrir depuis le menu de l'extension, n'importe où.
  GM_registerMenuCommand('⚙️ Réglages Pokémon', openSettings);
  GM_registerMenuCommand('📖 Pokédex', openDex);
  GM_registerMenuCommand('🏪 Boutique', openShop);
  GM_registerMenuCommand('❓ Comment jouer ?', openHelp);

  evaluateMount();
  // Vérification automatique (au plus une fois par jour, silencieuse en cas d'échec)
  setTimeout(() => { checkForUpdate().then(() => refreshUpdateUi()).catch(() => { }); }, 3000);
  setInterval(() => {
    const ballsChanged = tickBalls();
    if (root) {
      if (!current || current.slot !== currentSlot()) {
        const retryOk = !lastFail || Date.now() - lastFail > 10000;
        if (retryOk) loadSpawn();
      }
      markSeen();
      updateMini();
      if (ballsChanged) render(); else renderStatus();
    }
  }, 1000);
})();
