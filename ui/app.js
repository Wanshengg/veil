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
      traffic: { up: 1.24, down: 8.6, uptime: 0 },
      settings: {
        protocol: "VLESS + Reality",
        killSwitch: true,
        split: true,
        autostart: false,
        tun: false,
        dns: "Через туннель",
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
      srvInstall() { snap.srvStatus = "Макет: сервер не ставится"; emit(); },
      srvKeys() { snap.srvStatus = "Макет: ключи не читаются"; emit(); },
      srvIssue() { snap.srvStatus = "Макет: ключ не выдаётся"; emit(); },
      srvRevoke() { snap.srvStatus = "Макет: ключ не отзывается"; emit(); },
      srvConnect() { snap.srvStatus = "Макет: SSH недоступен"; emit(); },
      keyPaste() { snap.keyMsg = "Макет: ключ из буфера не читается"; emit(); },
      keyAdd() { snap.keyMsg = "Макет: ключ не добавляется"; emit(); },
      refresh() { emit(); },
    };
    return api;
  })();

  let adapter = mockAdapter;

  /* ── компоненты ──────────────────────────────────────────────────────── */
  const comp = {
    badge(s) {
      const map = {
        connected: ["connected", "Подключено"],
        connecting: ["connecting", "Подключение"],
        disconnected: ["disconnected", "Не подключено"],
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
      const cls = ms < 80 ? "good" : ms < 160 ? "mid" : "bad";
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

    tile(s, active, connected) {
      const load = s.load > 0 ? (s.load < 40 ? " · свободен" : s.load < 70 ? " · умеренно" : " · загружен") : "";
      return `
      <button class="tile" data-server="${s.id}" aria-pressed="${active}"
              aria-label="${esc(s.country + " · " + s.city + " · " + s.ping + " мс" + (active && connected ? " · подключено" : ""))}">
        <span class="tile-top">
          <span class="flag" aria-hidden="true">${s.cc}</span>
          ${comp.ping(s.ping)}
        </span>
        <span class="tile-name">${active && connected ? `<span class="tile-live" aria-hidden="true"></span>` : ""}${esc(s.country)}</span>
        <span class="tile-sub">${esc(s.city)}${load}</span>
        <span class="star tile-star" data-fav="${s.id}" role="button" aria-pressed="${s.fav}"
              aria-label="В избранное: ${esc(s.country)}">${icon.star(s.fav)}</span>
      </button>`;
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
      const server = servers.find((x) => x.id === snap.serverId) || servers[0] || null;
      const s = snap.state;
      const connected = s === "connected";
      const connecting = s === "connecting";
      const err = s === "error";
      const noNet = s === "no-internet";

      const title = connected ? `Подключено${server ? " · " + esc(server.country) : ""}`
        : connecting ? "Подключаемся…"
        : err ? "Не подключено — ошибка"
        : noNet ? "Нет интернета"
        : "Не подключено";
      const sub = connected && server ? `${esc(server.city)} · ${server.ping} мс · ${comp.fmtClock(snap.traffic.uptime)}`
        : connecting ? "проверяем TLS и маршрут"
        : err ? esc(snap.error || "сервер не ответил, попробуй другой")
        : noNet ? "сначала верни сеть — туннель не поднять"
        : "нажми плитку сервера, чтобы подключиться";

      const tiles = servers.length
        ? servers.map((x) => comp.tile(x, x.id === snap.serverId, connected)).join("")
        : comp.empty("Нет ключей", "Добавь ключ veil:// в буфер и вставь его в приложении.",
            `<button class="btn primary" data-action="add-key">＋ Добавить ключ</button>`);

      return `
      <section class="home">
        <div class="status-strip" data-state="${err ? "error" : connecting ? "connecting" : connected ? "connected" : "idle"}">
          <span class="strip-dot" aria-hidden="true"></span>
          <span class="strip-text">
            <span class="strip-title">${title}</span>
            <span class="strip-sub">${sub}</span>
          </span>
          ${connected ? `<button class="btn ghost strip-btn" data-action="toggle">Отключить</button>` : ""}
        </div>
        <div class="tiles">${tiles}</div>
      </section>`;
    },

    add(snap) {
      const msg = snap.keyMsg || "";
      const err = /нет ключа|не распознан|не поддерживаются|уже добавлен/i.test(msg);
      return `
      <section class="add">
        <div class="add-head">
          <div class="title">Соединение</div>
          <div class="faint">Вставь ключ <b>veil://</b> — он появится на главной плиткой.</div>
        </div>

        <div class="paste-row">
          <input id="keyInput" type="text" placeholder="Вставить ключ" spellcheck="false"
                 aria-label="Ключ подключения" autocomplete="off">
          <button class="btn primary" data-action="key-paste">Вставить</button>
        </div>

        <div class="paste-msg ${err ? "is-error" : msg ? "is-ok" : ""}" role="status" aria-live="polite">${esc(msg)}</div>

        <div class="add-hint">
          <div class="caps">Как это работает</div>
          <div class="faint">
            Скопируй ключ (он начинается с <code>veil://</code>) и нажми «Вставить».
            Можно вставить ключ в поле вручную и нажать Enter — результат тот же.
            Ключи по IP-адресу не принимаются: нужен домен, иначе маскировка под TLS ломается.
          </div>
        </div>
      </section>`;
    },

    server(snap) {
      const keys = snap.srvKeys || [];
      const host = snap.srvHost || "";
      const busy = !!snap.srvBusy;
      const status = snap.srvStatus || "";

      const field = (id, label, ph, val, type) => `
        <label class="field">
          <span class="caps">${esc(label)}</span>
          <input id="${id}" type="${type || "text"}" placeholder="${esc(ph)}" value="${esc(val || "")}"
                 spellcheck="false" autocomplete="off" ${busy ? "disabled" : ""}>
        </label>`;

      const form = `
        <div class="card srv-form">
          <div class="srv-hint faint">
            Заполни доступ по SSH — приложение само поставит veil на сервер,
            выпустит сертификат и добавит ключ на главную.
          </div>
          <div class="srv-grid">
            ${field("srvHost", "Сервер", "203.0.113.10 или veil.example.com", host)}
            ${field("srvPort", "Порт SSH", "22", "22", "number")}
            ${field("srvUser", "Пользователь", "root", snap.srvUser || "root")}
            ${field("srvPass", "Пароль", "пароль root", "", "password")}
            ${field("srvKey", "Или путь к ключу", "C:\\Users\\you\\.ssh\\id_ed25519", "")}
            ${field("srvDomain", "Домен (необязательно)", "veil.example.com — для Let's Encrypt", "")}
          </div>
          <div class="srv-buttons">
            <button class="btn primary" data-action="srv-install" ${busy ? "disabled" : ""}>
              ${busy ? "Устанавливаю…" : "Установить сервер"}
            </button>
            <button class="btn" data-action="srv-connect" ${busy ? "disabled" : ""}>Только подключиться</button>
          </div>
          <div class="srv-note faint">
            Без домена сервер получит самоподписанный сертификат, а клиент будет
            проверять его по отпечатку — шифрование то же, маскировка слабее.
          </div>
        </div>`;

      const keysBlock = host ? `
        <div class="card">
          <div class="srv-head">
            <span class="caps">Ключи на сервере</span>
            <span class="faint" style="font-size:var(--fs-12)">${esc(host)} · ${keys.length}</span>
          </div>
          ${keys.length
            ? `<div class="list">${keys.map((k) => `
                <div class="row" style="min-height:52px" data-key="${esc(k)}">
                  <span class="flag" aria-hidden="true">${esc(k.slice(0, 2).toUpperCase())}</span>
                  <span class="main"><span class="name">${esc(k)}</span><span class="sub">персональный ключ</span></span>
                  <span class="side"><button class="btn ghost" style="min-height:34px" data-action="srv-revoke" data-name="${esc(k)}">Отозвать</button></span>
                </div>`).join("")}</div>`
            : `<div class="faint" style="font-size:var(--fs-13)">пока один — тот, что выдан при установке</div>`}
          <div class="srv-actions">
            <input id="srvNewKey" type="text" placeholder="имя: мама, друг-петя" ${busy ? "disabled" : ""}>
            <button class="btn" data-action="srv-issue" ${busy ? "disabled" : ""}>Выдать ключ</button>
            <button class="btn ghost" data-action="srv-refresh" ${busy ? "disabled" : ""}>Обновить</button>
          </div>
        </div>` : "";

      return `
      <section class="add">
        <div class="add-head">
          <div class="title">Свой сервер</div>
          <div class="faint">Один раз ввести доступ по SSH — и сервер настраивается сам.</div>
        </div>
        ${form}
        ${keysBlock}
        <div class="paste-msg ${/Ошибка/i.test(status) ? "is-error" : status ? "is-ok" : ""}" role="status" aria-live="polite">${esc(status)}</div>
      </section>`;
    },

    settings(snap) {
      const s = snap.settings;
      const row = (title, sub, control) => `
        <div class="setting">
          <div class="label"><span>${esc(title)}</span>${sub ? `<span class="sub">${esc(sub)}</span>` : ""}</div>
          ${control}
        </div>`;
      const apps = snap.apps || [];

      return `
      <section style="display:grid;gap:var(--s-4)">
        <div class="group">
          <div class="caps group-title">Соединение</div>
          <div class="card" style="padding:0">
            ${row("Протокол", "veil · TLS 1.3, маскировка под браузер, fallback на реальный сайт", `<span class="faint num" style="font-size:var(--fs-13)">veil/1</span>`)}
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
            ${row("TUN-режим", s.tun === undefined ? "нужен перезапуск от администратора" : "весь трафик ОС, включая игры и UDP", comp.toggle(!!s.tun, "set-tun", "TUN-режим"))}
            <div class="setting">
              <div class="label"><span>Список исключений</span><span class="sub">.ru · .рф · 7410 российских подсетей</span></div>
              <button class="btn ghost" style="min-height:36px" data-action="sheet-rules">Изменить</button>
            </div>
          </div>
        </div>

        <div class="group">
          <div class="caps group-title">Приложения через туннель</div>
          <div class="card" style="padding:0">
            ${row("Выборочный режим", "только выбранные .exe, остальная система — мимо", `<button class="btn ghost" style="min-height:36px" data-action="app-add">Добавить</button>`)}
            ${apps.length
              ? apps.map((a) => `
                <div class="setting" data-app="${esc(a.path)}">
                  <div class="label"><span>${esc(a.name)}</span><span class="sub" title="${esc(a.path)}">${esc(a.path)}</span></div>
                  <span style="display:flex;align-items:center;gap:var(--s-2)">
                    <button class="btn ghost" style="min-height:36px" data-action="app-run" data-path="${esc(a.path)}" data-name="${esc(a.name)}">Запустить</button>
                    ${comp.toggle(a.on, "app-toggle", "Через туннель: " + a.name)}
                    <button class="star" data-app-del="${esc(a.path)}" aria-label="Удалить ${esc(a.name)}">×</button>
                  </span>
                </div>`).join("")
              : `<div class="setting"><div class="label"><span class="faint" style="font-size:var(--fs-13)">Список пуст — добавь .exe, чтобы запускать его через туннель</span></div></div>`}
          </div>
        </div>

        <div class="group">
          <div class="caps group-title">Данные</div>
          <div class="card" style="padding:0">
            ${row("Ключи", "ключи veil:// из буфера", `<button class="btn ghost" style="min-height:36px" data-action="add-key">Добавить</button>`)}
            ${row("Логи", "последние 200 строк", `<button class="btn ghost" style="min-height:36px" data-action="sheet-logs">Открыть</button>`)}
            ${row("Версия", "0.9.1 · Go 1.27.4", `<span class="faint num" style="font-size:var(--fs-13)">обновлено</span>`)}
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

    document.documentElement.dataset.theme = "dark";
    const badgeHost = $("#topBadgeHost");
    if (badgeHost) badgeHost.innerHTML = comp.badge(snap.state);

    const view = $("#view");
    view.innerHTML = state.tab === "settings" ? views.settings(snap)
      : state.tab === "add" ? views.add(snap)
      : state.tab === "server" ? views.server(snap)
      : views.home(snap, servers);

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

    const appDel = e.target.closest("[data-app-del]");
    if (appDel) { e.stopPropagation(); adapter.appDel(appDel.dataset.appDel); return; }

    const appToggle = e.target.closest('[data-action="app-toggle"]');
    if (appToggle) {
      const box = appToggle.closest("[data-app]");
      adapter.appToggle(box ? box.dataset.app : "");
      return;
    }

    const fav = e.target.closest("[data-fav]");
    if (fav) { e.stopPropagation(); adapter.toggleFav(fav.dataset.fav); return; }

    // плитка сервера: другой — переключаемся, тот же и уже подключено — отключаем
    const tile = e.target.closest("[data-server]");
    if (tile && !e.target.closest("[data-fav]")) {
      const snap = state.snapshot;
      const id = tile.dataset.server;
      if (snap.state === "connecting") return;
      if (snap.state === "connected" && snap.serverId === id) { await adapter.disconnect(); return; }
      if (snap.serverId !== id) adapter.selectServer(id);
      if (state.snapshot.state !== "connected") await adapter.connect();
      return;
    }

    const action = e.target.closest("[data-action]");
    if (!action) return;
    const a = action.dataset.action;
    const snap = state.snapshot;

    if (a === "toggle") {
      if (snap.state === "connected") await adapter.disconnect();
      else if (snap.state !== "connecting") await adapter.connect();
    } else if (a === "set-kill") adapter.setSetting("killSwitch", !snap.settings.killSwitch);
    else if (a === "set-autostart") adapter.setSetting("autostart", !snap.settings.autostart);
    else if (a === "set-split") adapter.setSetting("split", !snap.settings.split);
    else if (a === "set-tun") adapter.setSetting("tun", !snap.settings.tun);
    else if (a === "add-key") { state.tab = "add"; render(); }
    else if (a === "key-paste") adapter.keyPaste();
    else if (a === "srv-install") {
      const root = action.closest("section");
      const val = (id) => { const el = root.querySelector("#" + id); return el ? el.value.trim() : ""; };
      adapter.srvInstall({ host: val("srvHost"), port: val("srvPort") || "22", user: val("srvUser") || "root",
                           pass: val("srvPass"), keyfile: val("srvKey"), domain: val("srvDomain") });
    }
    else if (a === "srv-connect") {
      const root = action.closest("section");
      const val = (id) => { const el = root.querySelector("#" + id); return el ? el.value.trim() : ""; };
      adapter.srvConnect({ host: val("srvHost"), port: val("srvPort") || "22", user: val("srvUser") || "root",
                           pass: val("srvPass"), keyfile: val("srvKey") });
    }
    else if (a === "srv-issue") {
      const root = action.closest("section");
      const el = root.querySelector("#srvNewKey");
      const name = el ? el.value.trim() : "";
      if (name) adapter.srvIssue(name); else toast("Впиши имя, для кого ключ", "error");
    }
    else if (a === "srv-revoke") adapter.srvRevoke(action.dataset.name || "");
    else if (a === "srv-refresh") adapter.srvKeys();
    else if (a === "app-add") adapter.appAdd();
    else if (a === "app-run") adapter.appRun(action.dataset.path);
    else if (a === "sheet-rules") openSheet("Исключения маршрутизации", rulesSheet());
    else if (a === "sheet-logs") openSheet("Логи клиента", logsSheet());
    else if (a === "toast") toast(action.dataset.msg || "Готово", "ok");
    else if (a === "close-sheet") closeSheet();
  });

  document.addEventListener("change", (e) => {
    const sel = e.target.closest("[data-setting]");
    if (!sel) return;
    const key = sel.dataset.setting;
    adapter.setSetting(key, sel.value);
  });

  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") closeSheet();
    if (e.key === "Enter" && e.target && e.target.id === "keyInput") {
      e.preventDefault();
      const v = e.target.value.trim();
      if (v) adapter.keyAdd(v);
    }
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


  /* ── демо-состояния (?demo=1&state=error) ────────────────────────────── */
  function applyForcedState(snap) {
    if (!state.forced) return snap;
    const f = state.forced;
    if (f === "disconnected") snap.state = "disconnected";
    if (f === "connecting") snap.state = "connecting";
    if (f === "connected") snap.state = "connected";
    if (f === "error") { snap.state = "error"; snap.error = "Сервер отклонил TLS-рукопожатие"; }
    if (f === "no-internet") snap.state = "no-internet";
    if (f === "loading") state.loading = true;
    if (f === "empty") state.forcedEmpty = true;
    return snap;
  }

  /* ── запуск ──────────────────────────────────────────────────────────── */
  function setAdapter(a) {
    adapter = a;
    adapter.subscribe((snap) => {
      state.snapshot = applyForcedState(snap);
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
