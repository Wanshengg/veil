/* ============================================================================
   veil UI — слой представления.
   Логика VPN полностью спрятана за адаптером: window.VEIL_UI.setAdapter(adapter).
   Мок-адаптер ниже можно заменить одной строкой на реальное ядро.
   ========================================================================== */
(() => {
  "use strict";

  /* ── утилиты ─────────────────────────────────────────────────────────── */
  const $ = (sel, root = document) => root.querySelector(sel);
  const esc = (s) =>
    String(s).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

  const state = {
    tab: "home",
    theme: "dark",
    query: "",
    country: "all",
    onlyFav: false,
    snapshot: null,
    loading: false,
    forced: new URLSearchParams(location.search).get("state") || "",
    demo: new URLSearchParams(location.search).has("demo"),
  };

  /* ── иконки (inline SVG, без внешних шрифтов) ────────────────────────── */
  const icon = {
    power: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" aria-hidden="true"><path d="M12 4v7"/><path d="M7.2 6.6a8 8 0 1010.6 0"/></svg>`,
    search: `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="M20 20l-3.6-3.6"/></svg>`,
    star: (on) => `<svg width="18" height="18" viewBox="0 0 24 24" fill="${on ? "currentColor" : "none"}" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round" aria-hidden="true"><path d="M12 3.6l2.6 5.3 5.9.9-4.3 4.1 1 5.8-5.2-2.8-5.2 2.8 1-5.8L3.5 9.8l5.9-.9L12 3.6z"/></svg>`,
    check: `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 12.5l5 5L20 6.5"/></svg>`,
    alert: `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" aria-hidden="true"><path d="M12 8v5"/><circle cx="12" cy="16.6" r="1"/><path d="M10.3 4.4L3.5 17a2 2 0 001.8 3h13.4a2 2 0 001.8-3L13.7 4.4a2 2 0 00-3.4 0z"/></svg>`,
    globe: `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3c2.6 2.6 2.6 15.4 0 18M12 3C9.4 5.6 9.4 18.4 12 21"/></svg>`,
    chevron: `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M9 5l7 7-7 7"/></svg>`,
    arrowDown: `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 4v16m0 0l6-6m-6 6l-6-6"/></svg>`,
    arrowUp: `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 20V4m0 0l6 6m-6-6l-6 6"/></svg>`,
    wifiOff: `<svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" aria-hidden="true"><path d="M2 3l20 18"/><path d="M5 10.5a11 11 0 014.2-2.4M2.5 7A16 16 0 0112 4c2.4 0 4.6.6 6.6 1.7"/><path d="M8.2 14a7 7 0 013.8-1.4"/><circle cx="12" cy="19" r="1.2" fill="currentColor" stroke="none"/></svg>`,
  };

  /* ── адаптер: единственное место, где живёт «ядро» ───────────────────── */
  const mockAdapter = (() => {
    const servers = [
      { id: "de-fra", cc: "DE", country: "Германия", city: "Frankfurt", ping: 24, load: 12, fav: true },
      { id: "nl-ams", cc: "NL", country: "Нидерланды", city: "Amsterdam", ping: 38, load: 34, fav: true },
      { id: "fi-hel", cc: "FI", country: "Финляндия", city: "Helsinki", ping: 45, load: 21, fav: false },
      { id: "se-sto", cc: "SE", country: "Швеция", city: "Stockholm", ping: 52, load: 8, fav: false },
      { id: "pl-waw", cc: "PL", country: "Польша", city: "Warsaw", ping: 61, load: 47, fav: false },
      { id: "kz-ala", cc: "KZ", country: "Казахстан", city: "Almaty", ping: 96, load: 39, fav: false },
      { id: "tr-ist", cc: "TR", country: "Турция", city: "Istanbul", ping: 78, load: 63, fav: false },
      { id: "us-nyc", cc: "US", country: "США", city: "New York", ping: 142, load: 55, fav: false },
    ];

    const snap = {
      state: "disconnected", // disconnected | connecting | connected | error | no-internet
      serverId: "de-fra",
      subscription: { active: true, plan: "Год · Про", until: "12.02.2027", usedGB: 34.8, totalGB: 200, devices: 2, deviceLimit: 5 },
      traffic: { up: 1.24, down: 8.6, uptime: 0 },
      settings: {
        protocol: "VLESS + Reality",
        killSwitch: true,
        split: true,
        autostart: false,
        dns: "Через туннель",
        theme: "dark",
      },
      series: Array.from({ length: 40 }, (_, i) => 26 + Math.round(16 * Math.sin(i / 3.1) + 9 * Math.sin(i / 1.3) + (i % 5))),
      apps: [
        { name: "Google Chrome", path: "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe", on: true },
        { name: "Telegram", path: "C:\\Users\\me\\AppData\\Roaming\\Telegram Desktop\\Telegram.exe", on: true },
      ],
      error: "",
    };

    const listeners = new Set();
    let timer = null;
    const emit = () => listeners.forEach((fn) => fn(structuredClone(snap)));

    const api = {
      subscribe(fn) { listeners.add(fn); fn(structuredClone(snap)); return () => listeners.delete(fn); },
      async connect() {
        if (!snap.subscription.active) { snap.state = "error"; snap.error = "Подписка не активна"; return emit(); }
        snap.state = "connecting"; snap.error = ""; emit();
        await new Promise((r) => setTimeout(r, 1500));
        snap.state = "connected";
        snap.traffic = { up: 0.02, down: 0.14, uptime: 0 };
        clearInterval(timer);
        timer = setInterval(() => {
          if (snap.state !== "connected") return;
          snap.traffic.uptime += 1;
          snap.traffic.down = +(snap.traffic.down + 0.02 + Math.random() * 0.05).toFixed(2);
          snap.traffic.up = +(snap.traffic.up + 0.004 + Math.random() * 0.01).toFixed(2);
          snap.series = snap.series.slice(1).concat([26 + Math.round(18 * Math.sin(Date.now() / 4000) + Math.random() * 10)]);
          emit();
        }, 1000);
        emit();
      },
      async disconnect() {
        clearInterval(timer);
        snap.state = "disconnected"; snap.traffic.uptime = 0; emit();
      },
      selectServer(id) { snap.serverId = id; if (snap.state === "connected") { snap.state = "connecting"; emit(); setTimeout(() => { snap.state = "connected"; emit(); }, 900); } else emit(); },
      toggleFav(id) { const s = servers.find((x) => x.id === id); if (s) s.fav = !s.fav; emit(); },
      setSetting(key, value) { snap.settings[key] = value; emit(); },
      serverList() { return structuredClone(servers); },
      appAdd() { snap.apps.push({ name: "Новое приложение", path: "C:\\demo\\app.exe", on: true }); emit(); },
      appToggle(path) { const a = snap.apps.find((x) => x.path === path); if (a) a.on = !a.on; emit(); },
      appRun(name) { alert("В макете приложение «" + name + "» запускается через прокси"); },
      appDel(path) { snap.apps = snap.apps.filter((x) => x.path !== path); emit(); },
      refresh() { emit(); },
    };
    return api;
  })();

  let adapter = mockAdapter;

  /* ── компоненты ──────────────────────────────────────────────────────── */
  const comp = {
    badge(s) {
      const map = {
        connected: ["connected", "Защищено"],
        connecting: ["connecting", "Подключение"],
        disconnected: ["disconnected", "Отключено"],
        error: ["error", "Ошибка"],
        "no-internet": ["error", "Нет сети"],
      };
      const [st, text] = map[s] || map.disconnected;
      return `<span class="badge" data-state="${st}" role="status" aria-live="polite"><span class="dot" aria-hidden="true"></span>${text}</span>`;
    },

    toggle(on, action, label, disabled = false) {
      return `<button class="toggle" role="switch" aria-checked="${on}" aria-label="${esc(label)}"
        ${disabled ? 'aria-disabled="true" tabindex="-1"' : ""} data-action="${action}"></button>`;
    },

    ping(ms) {
      if (!ms) return `<span class="ping num faint">—</span>`;
      const cls = ms < 50 ? "good" : ms < 100 ? "mid" : "bad";
      return `<span class="ping num ${cls}">${ms} ms</span>`;
    },

    serverRow(s, selected) {
      return `
      <div class="row ${selected ? "selected" : ""}" data-server="${s.id}" role="button" tabindex="0"
           aria-label="${esc(s.country + ", " + s.city + ", " + s.ping + " мс")}">
        <span class="flag" aria-hidden="true">${s.cc}</span>
        <span class="main">
          <span class="name">${esc(s.country)} <span class="faint" style="font-weight:var(--w-regular)">· ${esc(s.city)}</span></span>
          <span class="sub">${s.load < 40 ? "Свободен" : s.load < 70 ? "Умеренная загрузка" : "Высокая загрузка"} · ${s.load}%</span>
        </span>
        <span class="side">
          <span class="loadpct" title="Загрузка сервера">${s.load}%</span>
          ${comp.ping(s.ping)}
          <button class="star" data-fav="${s.id}" aria-pressed="${s.fav}" aria-label="В избранное: ${esc(s.country)}">
            ${icon.star(s.fav)}
          </button>
        </span>
      </div>`;
    },

    sparkline(series) {
      const w = 320, h = 68;
      if (!series || series.length < 2) series = [0, 0];
      const max = Math.max(...series), min = Math.min(...series);
      const norm = (v) => h - ((v - min) / (max - min || 1)) * (h - 10) - 5;
      const pts = series.map((v, i) => [(i / (series.length - 1)) * w, norm(v)]);
      const line = pts.map((p, i) => `${i ? "L" : "M"}${p[0].toFixed(1)},${p[1].toFixed(1)}`).join(" ");
      const area = `${line} L${w},${h} L0,${h} Z`;
      return `<svg class="chart" viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" role="img" aria-label="График скорости">
        <defs><linearGradient id="chartFill" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="var(--accent)" stop-opacity="0.35"/>
          <stop offset="100%" stop-color="var(--accent)" stop-opacity="0"/>
        </linearGradient></defs>
        <path class="area" d="${area}"/>
        <path class="line" d="${line}"/>
        <circle class="dot" cx="${pts[pts.length - 1][0]}" cy="${pts[pts.length - 1][1]}" r="3"/>
      </svg>`;
    },

    empty(title, desc, action = "") {
      return `<div class="empty">
        <div class="art">${icon.globe}</div>
        <div class="t">${esc(title)}</div>
        <div class="d">${esc(desc)}</div>
        ${action}
      </div>`;
    },

    skeletons(n = 4) {
      return `<div class="list">${Array.from({ length: n }, () => `<div class="skeleton row-sk"></div>`).join("")}</div>`;
    },

    fmtClock(sec) {
      const h = String(Math.floor(sec / 3600)).padStart(2, "0");
      const m = String(Math.floor((sec % 3600) / 60)).padStart(2, "0");
      const s = String(sec % 60).padStart(2, "0");
      return `${h}:${m}:${s}`;
    },

    fmtGB(v) { return v.toFixed(2); },
  };

  /* ── экраны ──────────────────────────────────────────────────────────── */
  const views = {
    home(snap, servers) {
      const server = servers.find((x) => x.id === snap.serverId) || servers[0];
      const s = snap.state;
      const connected = s === "connected";
      const connecting = s === "connecting";
      const err = s === "error";
      const noNet = s === "no-internet";
      const subBlocked = !snap.subscription.active;

      const stateTitle = connected ? "Защищено" : connecting ? "Подключаемся…" : err ? "Не удалось подключиться" : noNet ? "Нет интернета" : "Не защищено";
      const stateSub = connected
        ? `Трафик идёт через <b>${esc(server.country)} · ${esc(server.city)}</b>`
        : connecting
          ? "Проверяем TLS-рукопожатие и маршрут"
          : err
            ? esc(snap.error || "Сервер не ответил, попробуй другой")
            : noNet
              ? "Проверь подключение к сети — туннель поднять нельзя"
              : "Один клик — и трафик зашифрован";

      const btnLabel = connected ? "Отключиться" : connecting ? "Отменяем…" : err ? "Повторить подключение" : "Подключиться";
      const btnDisabled = connecting || noNet || subBlocked;

      return `
      <section class="home">
        <div class="hero">
          <div class="donut" data-state="${err ? "error" : connecting ? "connecting" : connected ? "connected" : "idle"}">
            <svg viewBox="0 0 200 200" aria-hidden="true">
              <circle class="ring-base" cx="100" cy="100" r="86" fill="none" stroke-width="1"/>
              <circle class="ring-base" cx="100" cy="100" r="72" fill="none" stroke-width="1" opacity="0.6"/>
              <circle class="ring-pulse" cx="100" cy="100" r="79" fill="none" stroke-width="2.5" stroke-dasharray="120 620" stroke-linecap="round" transform="rotate(-90 100 100)" style="opacity:0"/>
            </svg>
            <button class="core" data-action="toggle" ${btnDisabled ? "disabled" : ""} aria-label="${btnLabel}">
              <span class="glyph" aria-hidden="true">${connected ? icon.check : icon.power}</span>
              <span class="label">${connected ? "вкл" : "выкл"}</span>
            </button>
          </div>

          <div class="hero-status">
            <div class="state">${stateTitle}</div>
            <div class="where">${stateSub}</div>
          </div>

          <div class="hero-actions">
            <button class="btn primary hero" data-action="toggle" ${btnDisabled ? "disabled" : ""}>${btnLabel}</button>
            <button class="btn" data-action="go-servers">Сменить сервер</button>
          </div>
        </div>

        <div class="side-col">
        <div class="grid-2">
          <div class="card flat">
            <div class="caps">Пинг</div>
            <div class="stat"><span class="value num">${server.ping}<span class="unit">ms</span></span></div>
            <div class="faint" style="font-size:var(--fs-12)">${connected ? "стабильно" : "по последнему замеру"}</div>
          </div>
          <div class="card flat">
            <div class="caps">Сессия</div>
            <div class="stat"><span class="value num">${comp.fmtClock(snap.traffic.uptime)}</span></div>
            <div class="faint" style="font-size:var(--fs-12)">${connected ? "соединение активно" : "не запущена"}</div>
          </div>
        </div>

        <div class="card" style="display:flex;align-items:center;gap:var(--s-3)">
          <span class="flag" aria-hidden="true">${server.cc}</span>
          <span class="main" style="display:grid;gap:2px;flex:1;min-width:0">
            <span class="name" style="font-weight:var(--w-strong)">${esc(server.country)} · ${esc(server.city)}</span>
            <span class="sub faint" style="font-size:var(--fs-12)">${connected ? "активный маршрут" : "по умолчанию"} · ${server.load}% загрузки</span>
          </span>
          <button class="btn ghost" style="min-height:38px" data-action="go-servers">Сменить</button>
        </div>

        <div class="card">
          <div class="chart-wrap">
            <div class="chart-head">
              <span class="caps">${snap.seriesUnit === "ms" ? "Пинг за сессию" : "Скорость"}</span>
              <span class="num muted" style="font-size:var(--fs-13)">${snap.seriesUnit === "ms"
                ? (snap.series.length ? "сейчас " + snap.series[snap.series.length - 1] + " мс" : "нет данных")
                : `${icon.arrowDown} ${comp.fmtGB(snap.traffic.down)} · ${icon.arrowUp} ${comp.fmtGB(snap.traffic.up)} МБ/с`}
              </span>
            </div>
            ${comp.sparkline(snap.series.length > 1 ? snap.series : [0, 0])}
          </div>
        </div>

        <div class="quick">
          <button class="item ${snap.settings.split ? "on" : ""}" data-action="quick-split">
            <span class="name">RU напрямую</span>
            <span class="state">${snap.settings.split ? "включено" : "выключено"}</span>
          </button>
          <button class="item ${snap.settings.killSwitch ? "on" : ""}" data-action="quick-kill">
            <span class="name">Kill switch</span>
            <span class="state">${snap.settings.killSwitch ? "включён" : "выключен"}</span>
          </button>
          <button class="item ${snap.settings.autostart ? "on" : ""}" data-action="quick-autostart">
            <span class="name">Автозапуск</span>
            <span class="state">${snap.settings.autostart ? "включён" : "выключен"}</span>
          </button>
        </div>

        </div>

        ${subBlocked ? `<div class="card" style="border-color:var(--warn-soft)">
          <div class="caps" style="color:var(--warn)">Подписка</div>
          <div class="title">Срок действия истёк</div>
          <div class="faint" style="margin:4px 0 12px">Туннель возобновится сразу после продления.</div>
          <button class="btn primary" data-action="go-plan">Продлить</button>
        </div>` : ""}
      </section>`;
    },

    servers(snap, servers) {
      if (state.loading) return `<div class="grid" style="display:grid;gap:var(--s-3)">${comp.skeletons(5)}</div>`;

      const countries = [...new Map(servers.map((s) => [s.cc, s.country])).entries()];
      let list = servers.filter((s) => {
        const q = state.query.trim().toLowerCase();
        const matchQ = !q || `${s.country} ${s.city} ${s.cc}`.toLowerCase().includes(q);
        const matchC = state.country === "all" || s.cc === state.country;
        const matchF = !state.onlyFav || s.fav;
        return matchQ && matchC && matchF;
      });

      const favs = list.filter((s) => s.fav);
      const rest = list.filter((s) => !s.fav);

      const group = (title, arr) =>
        arr.length
          ? `<div class="caps" style="padding:var(--s-3) var(--s-1) var(--s-1)">${title}</div>
             <div class="list">${arr.map((s) => comp.serverRow(s, s.id === snap.serverId)).join("")}</div>`
          : "";

      return `
      <section style="display:grid;gap:var(--s-3)">
        <div class="search">
          ${icon.search}
          <input id="search" type="search" placeholder="Страна или город" value="${esc(state.query)}" aria-label="Поиск сервера">
        </div>
        <div class="chips" role="group" aria-label="Фильтр по странам">
          <button class="chip" aria-pressed="${state.country === "all" && !state.onlyFav}" data-filter="all">Все · ${servers.length}</button>
          <button class="chip" aria-pressed="${state.onlyFav}" data-filter="fav">★ Избранные</button>
          ${countries.map(([cc, name]) => `<button class="chip" aria-pressed="${state.country === cc}" data-filter="${cc}">${cc} · ${esc(name)}</button>`).join("")}
        </div>

        ${list.length === 0
          ? comp.empty("Ничего не найдено", "Измени запрос или сбрось фильтр по стране.", `<button class="btn" data-filter="all">Сбросить фильтры</button>`)
          : `${group("Избранные", favs)}${group(favs.length ? "Все серверы" : "Доступные серверы", rest)}`}
      </section>`;
    },

    plan(snap) {
      const s = snap.subscription;
      const pct = s.totalGB > 0 ? Math.round((s.usedGB / s.totalGB) * 100) : 0;
      const days = s.active ? "89 дней" : "0 дней";
      return `
      <section style="display:grid;gap:var(--s-4)">
        <div class="card plan">
          <div class="plan-top">
            <div>
              <div class="caps">Тариф</div>
              <div class="plan-name">${esc(s.plan)}</div>
              <div class="faint" style="font-size:var(--fs-13)">активна до ${esc(s.until)} · ${days}</div>
            </div>
            ${s.active ? `<span class="badge" data-state="connected"><span class="dot"></span>Активна</span>`
                       : `<span class="badge" data-state="error"><span class="dot"></span>Истекла</span>`}
          </div>

          <div>
            <div style="display:flex;justify-content:space-between;margin-bottom:var(--s-2)">
              <span class="caps">Трафик</span>
              <span class="num muted" style="font-size:var(--fs-13)">${s.totalGB > 0 ? s.usedGB + " / " + s.totalGB + " ГБ" : "без лимита"}</span>
            </div>
            <div class="bar"><i style="width:${pct}%"></i></div>
          </div>

          <div class="kv">
            <div><div class="k">Устройства</div><div class="v num">${s.devices} / ${s.deviceLimit}</div></div>
            <div><div class="k">Осталось</div><div class="v num">${s.totalGB > 0 ? (s.totalGB - s.usedGB).toFixed(1) + " ГБ" : "∞"}</div></div>
            <div><div class="k">Серверов</div><div class="v num">8</div></div>
          </div>

          <div style="display:flex;gap:var(--s-2);flex-wrap:wrap">
            <button class="btn primary" data-action="renew">Продлить на год · 3 490 ₽</button>
            <button class="btn" data-action="toast" data-msg="Код приглашения скопирован">Пригласить друга</button>
          </div>
        </div>

        <div class="card">
          <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:var(--s-3)">
            <span class="caps">Устройства</span>
            <button class="btn ghost" style="min-height:36px" data-action="toast" data-msg="Новое устройство добавлено">Добавить</button>
          </div>
          <div class="list">
            ${[["Windows 11 · этот ПК", "сейчас"], ["Pixel 8 · Android", "2 дня назад"]]
              .map(([name, when]) => `<div class="row" style="min-height:56px">
                <span class="flag" aria-hidden="true">▣</span>
                <span class="main"><span class="name">${esc(name)}</span><span class="sub">последняя активность · ${esc(when)}</span></span>
                <span class="side"><button class="btn ghost" style="min-height:36px" data-action="toast" data-msg="Устройство отключено">Отключить</button></span>
              </div>`).join("")}
          </div>
        </div>
      </section>`;
    },

    apps(snap) {
      const list = snap.apps || [];
      return `
      <section style="display:grid;gap:var(--s-4)">
        <div class="card" style="display:grid;gap:var(--s-3)">
          <div>
            <div class="caps">Выборочный режим</div>
            <div class="title" style="margin-top:2px">Только выбранные приложения</div>
            <div class="faint" style="font-size:var(--fs-13);margin-top:4px">
              Приложение запускается с нашим прокси — остальная система остаётся в обычной сети.
              Работает для браузеров, Telegram и программ, которые уважают прокси.
            </div>
          </div>
          <button class="btn primary" data-action="app-add">＋ Добавить приложение</button>
        </div>

        ${list.length === 0
          ? comp.empty("Список пуст", "Добавь .exe — и запускай его через туннель, не заворачивая всю систему.", `<button class="btn" data-action="app-add">Выбрать .exe</button>`)
          : `<div class="list">
              ${list.map((a) => `
                <div class="row" style="min-height:68px">
                  <span class="flag" aria-hidden="true">${esc((a.name || "?").slice(0, 2).toUpperCase())}</span>
                  <span class="main">
                    <span class="name">${esc(a.name)}</span>
                    <span class="sub">${esc(a.path)}</span>
                  </span>
                  <span class="side">
                    ${comp.toggle(a.on, "app-toggle", "Через туннель: " + a.name)}
                    <button class="btn ghost" style="min-height:38px" data-action="app-run" data-path="${esc(a.path)}" data-name="${esc(a.name)}">Запустить</button>
                    <button class="star" data-app-del="${esc(a.path)}" aria-label="Удалить ${esc(a.name)}">×</button>
                  </span>
                </div>`).join("")}
            </div>`}

        <div class="card" style="border-color:var(--border)">
          <div class="caps">Как это работает</div>
          <div class="faint" style="font-size:var(--fs-13);line-height:1.6;margin-top:6px">
            Chrome, Edge, Яндекс — получают флаг <code>--proxy-server</code>;<br>
            Firefox — отдельный профиль с настройками прокси;<br>
            остальные — переменные <code>HTTP_PROXY</code>/<code>HTTPS_PROXY</code>/<code>ALL_PROXY</code>.<br>
            Игры и программы без поддержки прокси так не завернуть — для них нужен TUN-режим.
          </div>
        </div>
      </section>`;
    },

    settings(snap) {
      const s = snap.settings;
      const row = (title, sub, control) => `
        <div class="setting">
          <div class="label"><span>${esc(title)}</span>${sub ? `<span class="sub">${esc(sub)}</span>` : ""}</div>
          ${control}
        </div>`;

      return `
      <section style="display:grid;gap:var(--s-4)">
        <div class="group">
          <div class="caps group-title">Соединение</div>
          <div class="card" style="padding:0">
            ${row("Протокол", "маскировка под TLS 1.3", `
              <select data-setting="protocol" aria-label="Протокол">
                ${["VLESS + Reality", "VLESS + XHTTP", "Shadowsocks 2022", "VLESS + TLS"].map((p) => `<option ${p === s.protocol ? "selected" : ""}>${p}</option>`).join("")}
              </select>`)}
            ${row("Kill switch", "блокировать трафик при обрыве", comp.toggle(s.killSwitch, "set-kill", "Kill switch"))}
            ${row("Автозапуск", "подключаться при старте системы", comp.toggle(s.autostart, "set-autostart", "Автозапуск"))}
            ${row("DNS", "как резолвить имена", `
              <select data-setting="dns" aria-label="DNS">
                ${["Через туннель", "Системный", "1.1.1.1", "9.9.9.9"].map((d) => `<option ${d === s.dns ? "selected" : ""}>${d}</option>`).join("")}
              </select>`)}
          </div>
        </div>

        <div class="group">
          <div class="caps group-title">Маршрутизация</div>
          <div class="card" style="padding:0">
            ${row("RU-сайты напрямую", "яндекс, госуслуги, банки — без туннеля", comp.toggle(s.split, "set-split", "RU-сайты напрямую"))}
            <div class="setting">
              <div class="label"><span>Список исключений</span><span class="sub">.ru · .рф · 12 доменов · 3 подсети</span></div>
              <button class="btn ghost" style="min-height:36px" data-action="sheet-rules">Изменить</button>
            </div>
          </div>
        </div>

        <div class="group">
          <div class="caps group-title">Интерфейс и данные</div>
          <div class="card" style="padding:0">
            ${row("Тема", "тёмная по умолчанию", `
              <select data-setting="theme" aria-label="Тема">
                <option ${s.theme === "dark" ? "selected" : ""} value="dark">Тёмная</option>
                <option ${s.theme === "light" ? "selected" : ""} value="light">Светлая</option>
                <option ${s.theme === "auto" ? "selected" : ""} value="auto">Как в системе</option>
              </select>`)}
            ${row("Логи", "последние 200 строк", `<button class="btn ghost" style="min-height:36px" data-action="sheet-logs">Открыть</button>`)}
            ${row("Версия", "0.9.0 · ядро 1.27.4", `<span class="faint num" style="font-size:var(--fs-13)">обновлено</span>`)}
          </div>
        </div>
      </section>`;
    },
  };

  /* ── рендер ──────────────────────────────────────────────────────────── */
  function render() {
    const snap = state.snapshot;
    if (!snap) return;
    const servers = adapter.serverList();

    document.documentElement.dataset.theme = state.theme;
    const badgeHost = $("#topBadgeHost");
    if (badgeHost) badgeHost.innerHTML = comp.badge(snap.state);

    const view = $("#view");
    if (state.tab === "home") view.innerHTML = views.home(snap, servers);
    else if (state.tab === "servers") view.innerHTML = views.servers(snap, servers);
    else if (state.tab === "plan") view.innerHTML = views.plan(snap);
    else if (state.tab === "apps") view.innerHTML = views.apps(snap);
    else view.innerHTML = views.settings(snap);

    document.querySelectorAll(".tab").forEach((t) => t.setAttribute("aria-selected", String(t.dataset.tab === state.tab)));
    if (state.tab === "home") decorateHero();
  }

  function decorateHero() {
    const ring = document.querySelector(".donut .ring-pulse");
    if (ring) ring.style.opacity = "";
  }

  /* ── действия ────────────────────────────────────────────────────────── */
  document.addEventListener("click", async (e) => {
    const tab = e.target.closest(".tab");
    if (tab) { state.tab = tab.dataset.tab; render(); return; }

    const filter = e.target.closest("[data-filter]");
    if (filter) {
      const v = filter.dataset.filter;
      if (v === "all") { state.country = "all"; state.onlyFav = false; }
      else if (v === "fav") { state.onlyFav = true; state.country = "all"; }
      else { state.country = v; state.onlyFav = false; }
      render(); return;
    }

    const appDel = e.target.closest("[data-app-del]");
    if (appDel) { e.stopPropagation(); adapter.appDel(appDel.dataset.appDel); return; }

    const appToggle = e.target.closest('[data-action="app-toggle"]');
    if (appToggle) {
      const row = appToggle.closest(".row");
      const path = row && row.querySelector(".sub") ? row.querySelector(".sub").textContent : "";
      adapter.appToggle(path);
      return;
    }

    const fav = e.target.closest("[data-fav]");
    if (fav) { e.stopPropagation(); adapter.toggleFav(fav.dataset.fav); return; }

    const row = e.target.closest("[data-server]");
    if (row) { adapter.selectServer(row.dataset.server); return; }

    const action = e.target.closest("[data-action]");
    if (!action) return;
    const a = action.dataset.action;
    const snap = state.snapshot;

    if (a === "toggle") {
      if (snap.state === "connected") await adapter.disconnect();
      else if (snap.state !== "connecting") await adapter.connect();
    } else if (a === "go-servers") { state.tab = "servers"; render(); }
    else if (a === "go-plan") { state.tab = "plan"; render(); }
    else if (a === "quick-split") adapter.setSetting("split", !snap.settings.split);
    else if (a === "quick-kill") adapter.setSetting("killSwitch", !snap.settings.killSwitch);
    else if (a === "quick-autostart") adapter.setSetting("autostart", !snap.settings.autostart);
    else if (a === "set-kill") adapter.setSetting("killSwitch", !snap.settings.killSwitch);
    else if (a === "set-autostart") adapter.setSetting("autostart", !snap.settings.autostart);
    else if (a === "set-split") adapter.setSetting("split", !snap.settings.split);
    else if (a === "renew") toast("Переходим к оплате…", "ok");
    else if (a === "app-add") adapter.appAdd();
    else if (a === "app-run") adapter.appRun(action.dataset.path);
    else if (a === "sheet-rules") openSheet("Исключения маршрутизации", rulesSheet());
    else if (a === "sheet-logs") openSheet("Логи клиента", logsSheet());
    else if (a === "toast") toast(action.dataset.msg || "Готово", "ok");
    else if (a === "close-sheet") closeSheet();
    else if (a === "theme") toggleTheme();
  });

  document.addEventListener("input", (e) => {
    if (e.target.id === "search") {
      state.query = e.target.value;
      const listHost = e.target.closest("section");
      const snap = state.snapshot;
      const servers = adapter.serverList();
      const html = views.servers(snap, servers);
      const tmp = document.createElement("div");
      tmp.innerHTML = html;
      // заменяем только содержимое после строки поиска, чтобы не терять фокус
      const fresh = tmp.querySelector(".chips").parentElement.innerHTML;
      listHost.innerHTML = fresh;
      const input = $("#search");
      input.focus();
      input.setSelectionRange(input.value.length, input.value.length);
    }
  });

  document.addEventListener("change", (e) => {
    const sel = e.target.closest("[data-setting]");
    if (!sel) return;
    const key = sel.dataset.setting;
    if (key === "theme") { state.theme = sel.value === "auto" ? (matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark") : sel.value; adapter.setSetting("theme", sel.value); }
    adapter.setSetting(key, sel.value);
  });

  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") closeSheet();
    if (e.key === "Enter" || e.key === " ") {
      const row = e.target.closest("[data-server]");
      if (row) { e.preventDefault(); adapter.selectServer(row.dataset.server); }
    }
  });

  /* ── шторки и тосты ──────────────────────────────────────────────────── */
  function openSheet(title, html) {
    const scrim = $("#scrim"), sheet = $("#sheet");
    $("#sheetTitle").textContent = title;
    $("#sheetBody").innerHTML = html + `<button class="btn" style="width:100%;margin-top:var(--s-3)" data-action="close-sheet">Закрыть</button>`;
    scrim.hidden = false; sheet.hidden = false;
    requestAnimationFrame(() => { document.body.dataset.overlay = "1"; });
    sheet.querySelector("button, select, input")?.focus();
  }
  function closeSheet() {
    delete document.body.dataset.overlay;
    setTimeout(() => { $("#scrim").hidden = true; $("#sheet").hidden = true; }, 260);
  }
  $("#scrim").addEventListener("click", closeSheet);

  function rulesSheet() {
    return `<div class="list">
      ${[["Домены", ".ru, .рф, .su — 12 записей"], ["Сайты", "yandex.ru, gosuslugi.ru, sber.ru, ozon.ru"], ["Подсети", "10.0.0.0/8, 192.168.0.0/16, 172.16.0.0/12"]]
        .map(([k, v]) => `<div class="row" style="min-height:56px"><span class="main"><span class="name">${esc(k)}</span><span class="sub">${esc(v)}</span></span><span class="side"><button class="btn ghost" style="min-height:36px" data-action="toast" data-msg="Редактор списка откроется в следующей сборке">Изменить</button></span></div>`).join("")}
    </div>`;
  }

  function logsSheet() {
    const lines = [
      "18:15:02 tls handshake ok (fp=firefox, tls1.3)",
      "18:15:02 tunnel -> www.speedtest.net:443",
      "18:15:04 direct -> www.gosuslugi.ru:443",
      "18:15:07 tunnel -> api.ipify.org:443 (ES, 203.0.113.10)",
      "18:15:11 keepalive ok · 24 ms",
    ];
    return `<pre class="card" style="margin:0;font-family:ui-monospace,Consolas,monospace;font-size:var(--fs-12);color:var(--text-2);white-space:pre-wrap">${esc(lines.join("\n"))}</pre>`;
  }

  function toast(text, kind = "ok") {
    const host = $("#toasts");
    const el = document.createElement("div");
    el.className = "toast";
    el.dataset.kind = kind;
    el.textContent = text;
    host.appendChild(el);
    setTimeout(() => el.remove(), 2600);
  }

  function toggleTheme() {
    state.theme = state.theme === "dark" ? "light" : "dark";
    adapter.setSetting("theme", state.theme);
    render();
  }
  $("#themeBtn").addEventListener("click", toggleTheme);

  /* ── демо-состояния (?demo=1&state=error) ────────────────────────────── */
  function applyForcedState(snap) {
    if (!state.forced) return snap;
    const f = state.forced;
    if (f === "disconnected") snap.state = "disconnected";
    if (f === "connecting") snap.state = "connecting";
    if (f === "connected") snap.state = "connected";
    if (f === "error") { snap.state = "error"; snap.error = "Сервер отклонил TLS-рукопожатие"; }
    if (f === "no-internet") snap.state = "no-internet";
    if (f === "no-sub") snap.subscription.active = false;
    if (f === "expired") { snap.subscription.active = false; snap.subscription.until = "01.11.2026"; snap.subscription.plan = "Год · Про"; }
    if (f === "loading") state.loading = true;
    if (f === "empty") state.forcedEmpty = true;
    return snap;
  }

  /* ── запуск ──────────────────────────────────────────────────────────── */
  function setAdapter(a) {
    adapter = a;
    adapter.subscribe((snap) => {
      state.snapshot = applyForcedState(snap);
      state.theme = state.snapshot.settings.theme === "light" ? "light" : state.theme;
      render();
    });
  }

  window.VEIL_UI = { setAdapter, state, mock: mockAdapter };
  setAdapter(mockAdapter);

  // пустой список серверов для демонстрации состояния
  const emptyAdapter = {
    subscribe(fn) { mockAdapter.subscribe(fn); },
    connect: () => mockAdapter.connect(),
    disconnect: () => mockAdapter.disconnect(),
    selectServer: (id) => mockAdapter.selectServer(id),
    toggleFav: () => {},
    setSetting: (k, v) => mockAdapter.setSetting(k, v),
    serverList: () => (state.forced === "empty" ? [] : mockAdapter.serverList()),
    refresh: () => mockAdapter.refresh(),
  };
  if (new URLSearchParams(location.search).has("demo")) setAdapter(emptyAdapter);
})();
