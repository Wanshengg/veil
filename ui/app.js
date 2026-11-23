/* veil — интерфейс концепта «Фраза».
   Состояние приходит снапшотом от ядра; весь VPN-слой за адаптером.
   Экраны: онбординг → главный (фраза) → выбор сервера → настройки → ключи/сервер. */

(() => {
  "use strict";

  const $ = (sel, root = document) => root.querySelector(sel);
  const esc = (s) =>
    String(s).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

  /* ── страны: как называть в фразе ─────────────────────────────────────── */

  const COUNTRY = {
    ES: ["Испания", "Испании", "Испанию"],
    DE: ["Германия", "Германии", "Германию"],
    NL: ["Нидерланды", "Нидерландов", "Нидерландам"],
    FI: ["Финляндия", "Финляндии", "Финляндию"],
    SE: ["Швеция", "Швеции", "Швецию"],
    PL: ["Польша", "Польше", "Польшу"],
    TR: ["Турция", "Турции", "Турцию"],
    KZ: ["Казахстан", "Казахстане", "Казахстану"],
    US: ["США", "США", "США"],
    GB: ["Великобритания", "Великобритании", "Великобританию"],
    FR: ["Франция", "Франции", "Францию"],
    IT: ["Италия", "Италии", "Италию"],
    CH: ["Швейцария", "Швейцарии", "Швейцарию"],
    AT: ["Австрия", "Австрии", "Австрию"],
    CZ: ["Чехия", "Чехии", "Чехию"],
    RO: ["Румыния", "Румынии", "Румынию"],
    RS: ["Сербия", "Сербии", "Сербию"],
    LT: ["Литва", "Литве", "Литву"],
    LV: ["Латвия", "Латвии", "Латвию"],
    EE: ["Эстония", "Эстонии", "Эстонию"],
    BG: ["Болгария", "Болгарии", "Болгарию"],
    MD: ["Молдавия", "Молдавии", "Молдавию"],
    GE: ["Грузия", "Грузии", "Грузию"],
    AM: ["Армения", "Армении", "Армению"],
    AE: ["ОАЭ", "ОАЭ", "ОАЭ"],
    SG: ["Сингапур", "Сингапуре", "Сингапуру"],
    HK: ["Гонконг", "Гонконге", "Гонконгу"],
    JP: ["Япония", "Японии", "Японию"],
    KR: ["Корея", "Корее", "Корею"],
    CA: ["Канада", "Канаде", "Канаду"],
    BR: ["Бразилия", "Бразилии", "Бразилию"],
    IN: ["Индия", "Индии", "Индию"],
    IL: ["Израиль", "Израиле", "Израилю"],
    TH: ["Таиланд", "Таиланде", "Таиланду"],
    VN: ["Вьетнам", "Вьетнаме", "Вьетнаму"],
    ID: ["Индонезия", "Индонезии", "Индонезию"],
    MX: ["Мексика", "Мексике", "Мексику"],
    AR: ["Аргентина", "Аргентине", "Аргентину"],
    ZA: ["ЮАР", "ЮАР", "ЮАР"],
    AU: ["Австралия", "Австралии", "Австралию"],
    PT: ["Португалия", "Португалии", "Португалию"],
    IE: ["Ирландия", "Ирландии", "Ирландию"],
    NO: ["Норвегия", "Норвегии", "Норвегию"],
    DK: ["Дания", "Дании", "Данию"],
    BE: ["Бельгия", "Бельгии", "Бельгию"],
    LU: ["Люксембург", "Люксембурге", "Люксембургу"],
    IS: ["Исландия", "Исландии", "Исландию"],
    GR: ["Греция", "Греции", "Грецию"],
    HR: ["Хорватия", "Хорватии", "Хорватию"],
    CY: ["Кипр", "Кипре", "Кипру"],
    UA: ["Украина", "Украине", "Украину"],
    BY: ["Беларусь", "Беларуси", "Беларусь"],
    AZ: ["Азербайджан", "Азербайджане", "Азербайджану"],
    UZ: ["Узбекистан", "Узбекистане", "Узбекистану"],
    KG: ["Киргизия", "Киргизии", "Киргизию"]
  };

  const country = (s) => {
    if (!s) return null;
    const row = COUNTRY[(s.cc || "").toUpperCase()];
    if (!row) return null;
    return { name: row[0], in: row[1], to: row[2] };
  };

  /* ── состояние экранов ────────────────────────────────────────────────── */

  const S = {
    screen: "main",      // main | settings | keys | server
    sheet: "",           // cities | logs
    query: "",
    snap: null,
    last: ""
  };

  /* ── мок-адаптер для просмотра в браузере ─────────────────────────────── */

  const mock = (() => {
    const servers = [
      { id: "es:443", cc: "ES", country: "Испания", city: "185.42.113.7", ping: 84, load: 0, fav: false },
      { id: "nl:443", cc: "NL", country: "Нидерланды", city: "185.42.113.9", ping: 37, load: 0, fav: false }
    ];
    const snap = {
      state: "disconnected",
      serverId: "es:443",
      onboarded: false,
      traffic: { up: 0, down: 0, uptime: 0 },
      settings: { killSwitch: true, split: true, autostart: false, tun: false, dns: "Через туннель" },
      keyMsg: "", srvStatus: "", srvBusy: false, srvHost: "", srvUser: "", srvKeys: [], apps: [], error: ""
    };
    const subs = new Set();
    let timer = null;
    const emit = () => subs.forEach((f) => f(JSON.parse(JSON.stringify(snap))));
    return {
      subscribe(fn) { subs.add(fn); fn(JSON.parse(JSON.stringify(snap))); return () => subs.delete(fn); },
      serverList: () => servers,
      connect() {
        snap.state = "connecting"; emit();
        setTimeout(() => {
          snap.state = "connected"; snap.traffic.uptime = 0; emit();
          clearInterval(timer);
          timer = setInterval(() => { snap.traffic.uptime += 1; emit(); }, 1000);
        }, 2200);
      },
      disconnect() { clearInterval(timer); snap.state = "disconnected"; snap.traffic.uptime = 0; emit(); },
      selectServer(id) { snap.serverId = id; emit(); },
      setSetting(k, v) { snap.settings[k] = v; emit(); },
      onboard() { snap.onboarded = true; emit(); },
      refresh() { emit(); },
      keyPaste() { snap.keyMsg = "Макет: ключ из буфера не читается"; emit(); },
      keyAdd() {},
      srvConnect() { snap.srvStatus = "Макет: SSH недоступен"; emit(); },
      srvInstall() {}, srvKeys() {}, srvIssue() {}, srvRevoke() {}
    };
  })();

  let adapter = mock;

  /* ── мелкие части ─────────────────────────────────────────────────────── */

  const clock = (sec) => {
    if (sec === null || sec === undefined) return "—";
    if (sec === 0) return "0 с";
    if (sec < 60) return sec + " с";
    const m = Math.floor(sec / 60);
    if (m < 60) return m + " мин";
    return Math.floor(m / 60) + " ч " + (m % 60) + " мин";
  };

  const switchBtn = (on, action, label) =>
    `<button type="button" class="switch" role="switch" aria-checked="${on}" aria-label="${esc(label)}" data-action="${action}">
       <span class="track"><span class="knob"></span></span>
     </button>`;

  const row = (title, value, extra = "") =>
    `<div class="row"><span class="t">${esc(title)}</span>${extra || `<span class="v">${esc(value || "—")}</span>`}</div>`;

  /* ── экран: онбординг ─────────────────────────────────────────────────── */

  const viewOnboarding = () => `
    <section class="screen">
      <h1 class="phrase">Приложение создаст VPN-подключение.</h1>
      <div class="spacer"></div>
      <button class="btn filled" data-action="onboard">Разрешить</button>
    </section>`;

  /* ── экран: главный ───────────────────────────────────────────────────── */

  const viewMain = (snap, servers) => {
    const active = servers.find((s) => s.id === snap.serverId) || servers[0] || null;
    const c = country(active);
    const st = snap.state;

    const cityWord = c
      ? `<button class="word" data-action="cities">${esc(c.in)}</button>`
      : `<button class="word" data-action="cities">${esc(active ? active.country : "выбрать")}</button>`;

    const phrase = {
      disconnected: `<span class="fade">Интернет открыт.</span>`,
      connecting: `<span class="fade">Подключаемся к&nbsp;${cityWord}…</span>`,
      connected: `<span class="fade">Вы в<br>${cityWord}<span class="chev">▾</span></span>`,
      reconnecting: `<span class="fade">Восстанавливаем связь…</span>`,
      error: `<span class="fade">Не удалось подключиться.</span>`
    }[st] || "";

    const ip = active && /^\d+\.\d+\.\d+\.\d+$/.test(active.city || "") ? active.city : "—";
    const facts = {
      disconnected: active ? `Выбран: ${country(active) ? country(active).name : active.country}` : "Профилей нет",
      connecting: "Рукопожатие",
      connected: `${ip} · ${active && active.ping ? active.ping + " мс" : "—"} · ${clock(snap.traffic.uptime)}`,
      reconnecting: "Повторное рукопожатие",
      error: ""
    }[st] || "";

    const primary = {
      disconnected: ["Подключиться", "filled"],
      connecting: ["Отмена", "outline"],
      connected: ["Отключиться", "outline"],
      reconnecting: ["Отмена", "outline"],
      error: ["Попробовать иначе", "danger"]
    }[st] || ["Подключиться", "filled"];

    const busy = st === "connecting" || st === "reconnecting";

    return `
    <section class="screen">
      <h1 class="phrase" aria-live="polite">${phrase}</h1>
      ${facts ? `<${st === "disconnected" ? 'button class="facts facts-btn" data-action="cities" aria-label="Выбрать сервер"' : 'div class="facts"'}>${st === "connected" ? '<span class="dot" aria-hidden="true"></span>' : ""}<span>${esc(facts)}</span>${st === "disconnected" ? '<span class="chev">▾</span>' : ""}</${st === "disconnected" ? "button" : "div"}>` : ""}
      ${st === "error" ? `<p class="reason">${esc(snap.error || "Провайдер блокирует этот порт")}</p>` : ""}

      <div class="spacer"></div>
      <button class="btn ${primary[1]}" data-action="primary" ${busy ? "" : ""}>${primary[0]}</button>
      <button class="link-row" data-action="settings">Настройки <span class="arrow">›</span></button>
    </section>`;
  };

  /* ── шторка: выбор сервера ────────────────────────────────────────────── */

  const viewCities = (snap, servers) => {
    const q = S.query.trim().toLowerCase();
    const list = servers.filter((s) => !q || `${s.country} ${s.cc}`.toLowerCase().includes(q));

    const item = (s) => {
      const c = country(s);
      const isActive = s.id === snap.serverId;
      return `
        <button class="city" data-pick="${esc(s.id)}" ${isActive ? 'aria-current="true"' : ""}>
          <span class="mark">${isActive ? "<i></i>" : ""}</span>
          <span>
            <span class="name">${esc(c ? c.name : s.country)}</span>
            <span class="country">${esc(/^\d+\./.test(s.city) ? s.city : s.country)}</span>
          </span>
          <span class="ping">${s.ping ? s.ping : "—"}</span>
        </button>`;
    };

    return `
      <div class="scrim" data-action="close-sheet"></div>
      <div class="sheet" role="dialog" aria-label="Выбор сервера">
        <div class="head">
          <input class="search" id="citySearch" type="search" placeholder="Поиск" value="${esc(S.query)}" aria-label="Поиск сервера">
        </div>
        <div class="body">
          ${list.length ? list.map(item).join("") : `<p class="msg">Ничего не нашлось</p>`}
        </div>
      </div>`;
  };

  /* ── экран: настройки ─────────────────────────────────────────────────── */

  const viewSettings = (snap) => {
    const s = snap.settings;
    return `
    <section class="screen compact">
      <button class="back" data-action="home">‹ Назад</button>

      <div class="group">
        <div class="caps">Соединение</div>
        ${row("Протокол", "veil · TLS 1.3")}
        <div class="row"><span class="t">Автоподключение</span>${switchBtn(s.autostart, "set-autostart", "Автоподключение")}</div>
      </div>

      <div class="group">
        <div class="caps">Безопасность</div>
        <div class="row"><span class="t">Блокировать интернет при обрыве</span>${switchBtn(s.killSwitch, "set-kill", "Блокировать интернет при обрыве")}</div>
        <p class="note">Трафик встанет, пока туннель не восстановится</p>
        <div class="row"><span class="t">RU-сайты напрямую</span>${switchBtn(s.split, "set-split", "RU-сайты напрямую")}</div>
      </div>

      <div class="group">
        <div class="caps">Маршрутизация</div>
        <div class="row"><span class="t">TUN-режим</span>${switchBtn(!!s.tun, "set-tun", "TUN-режим")}</div>
        <div class="row">
          <span class="t">DNS</span>
          <select class="plain" data-setting="dns" aria-label="DNS">
            ${["Через туннель", "Системный", "1.1.1.1", "9.9.9.9"].map((d) => `<option ${d === s.dns ? "selected" : ""}>${d}</option>`).join("")}
          </select>
        </div>
      </div>

      <div class="group">
        <div class="caps">Профиль</div>
        <div class="row clickable" data-action="keys"><span class="t">Ключ</span><span class="v">›</span></div>
        <div class="row clickable" data-action="server"><span class="t">Свой сервер</span><span class="v">›</span></div>
        <div class="row clickable" data-action="cities"><span class="t">Сервер</span><span class="v">›</span></div>
      </div>

      <div class="group">
        <div class="caps">Приложение</div>
        ${row("Тема", "Светлая")}
        ${row("Язык", "Русский")}
        <div class="row clickable" data-action="logs"><span class="t">Журнал</span><span class="v">Открыть</span></div>
      </div>
    </section>`;
  };

  /* ── экран: ключ ──────────────────────────────────────────────────────── */

  const viewKeys = (snap) => `
    <section class="screen compact">
      <button class="back" data-action="settings">‹ Назад</button>
      <div class="group">
        <div class="caps">Соединение</div>
        <label class="field">
          <span class="caps">Ключ</span>
          <input id="keyInput" type="text" placeholder="veil://…" spellcheck="false" autocomplete="off" aria-label="Ключ">
        </label>
        <div class="actions">
          <button class="btn filled" data-action="key-paste">Вставить из буфера</button>
          <button class="btn outline" data-action="key-add">Добавить ключ из поля</button>
        </div>
        <p class="msg ${/нет ключа|не распознан|не поддерживаются|уже добавлен|ошиб/i.test(snap.keyMsg || "") ? "err" : ""}">${esc(snap.keyMsg || "")}</p>
      </div>
    </section>`;

  /* ── экран: свой сервер ───────────────────────────────────────────────── */

  const viewServer = (snap) => {
    const busy = !!snap.srvBusy;
    const keys = snap.srvKeys || [];
    const field = (id, label, ph, type) => `
      <label class="field">
        <span class="caps">${esc(label)}</span>
        <input id="${id}" type="${type || "text"}" placeholder="${esc(ph)}" spellcheck="false" autocomplete="off" ${busy ? "disabled" : ""}>
      </label>`;

    return `
    <section class="screen compact">
      <button class="back" data-action="settings">‹ Назад</button>

      <div class="group">
        <div class="caps">Сервер по SSH</div>
        ${field("srvHost", "Сервер", "203.0.113.10 или домен")}
        ${field("srvPort", "Порт", "22", "number")}
        ${field("srvUser", "Пользователь", "root")}
        ${field("srvPass", "Пароль", "пароль или путь к ключу", "password")}
        ${field("srvKey", "Путь к ключу", "C:\\Users\\you\\.ssh\\id_ed25519")}
        ${field("srvDomain", "Домен для сертификата", "необязательно")}
        <div class="actions">
          <button class="btn filled" data-action="srv-install" ${busy ? "disabled" : ""}>Установить сервер</button>
          <button class="btn outline" data-action="srv-connect" ${busy ? "disabled" : ""}>Только подключиться</button>
        </div>
        <p class="msg ${/ошиб|не принял|не удалось/i.test(snap.srvStatus || "") ? "err" : ""}">${esc(snap.srvStatus || "")}</p>
      </div>

      <div class="group">
        <div class="caps">Ключи</div>
        ${keys.length
          ? keys.map((k) => `<div class="row"><span class="t">${esc(k)}</span><button class="btn outline" style="height:36px;width:auto;padding:0 12px;font-size:13px" data-action="srv-revoke" data-name="${esc(k)}">Отозвать</button></div>`).join("")
          : `<p class="msg">Пока не читал список</p>`}
        <label class="field">
          <span class="caps">Новый ключ</span>
          <input id="srvNewKey" type="text" placeholder="имя: мама, друг" ${busy ? "disabled" : ""}>
        </label>
        <div class="actions">
          <button class="btn outline" data-action="srv-issue" ${busy ? "disabled" : ""}>Выдать ключ</button>
        </div>
      </div>
    </section>`;
  };

  /* ── шторка: журнал ───────────────────────────────────────────────────── */

  const viewLogs = (snap) => {
    const lines = [
      `состояние: ${snap.state}`,
      `сервер: ${snap.serverId || "—"}`,
      `пинг: ${snap.settings ? "" : ""}${snap.traffic ? snap.traffic.uptime + " с в сессии" : "—"}`,
      snap.error ? `ошибка: ${snap.error}` : "",
      snap.keyMsg ? `ключи: ${snap.keyMsg}` : "",
      snap.srvStatus ? `сервер: ${snap.srvStatus}` : ""
    ].filter(Boolean);
    return `
      <div class="scrim" data-action="close-sheet"></div>
      <div class="sheet" role="dialog" aria-label="Журнал">
        <div class="head"><div class="caps">Журнал</div></div>
        <div class="body"><pre class="mono-block">${esc(lines.join("\n"))}</pre></div>
      </div>`;
  };

  /* ── рендер ───────────────────────────────────────────────────────────── */

  function render() {
    const snap = S.snap;
    if (!snap) return;
    const servers = adapter.serverList() || [];
    const view = $("#view");

    let html;
    if (!snap.onboarded) html = viewOnboarding();
    else if (S.screen === "settings") html = viewSettings(snap);
    else if (S.screen === "keys") html = viewKeys(snap);
    else if (S.screen === "server") html = viewServer(snap);
    else html = viewMain(snap, servers);

    const overlay = $("#overlay");
    const overlayHtml = S.sheet === "cities" ? viewCities(snap, servers)
      : S.sheet === "logs" ? viewLogs(snap)
      : "";

    const key = S.screen + "|" + snap.state + "|" + snap.onboarded + "|" + S.sheet;
    if (html !== S.last || key !== S.lastKey) {
      // перенос введённого в поля: снапшот приходит раз в секунду
      const active = document.activeElement;
      const focused = active && active.id ? active.id : "";
      const keep = {};
      document.querySelectorAll("#view input, #view select, #overlay input").forEach((el) => {
        if (el.id) keep[el.id] = el.value;
      });

      view.innerHTML = html;
      overlay.innerHTML = overlayHtml;

      Object.keys(keep).forEach((id) => {
        const el = document.getElementById(id);
        if (el) el.value = keep[id];
      });
      if (focused) {
        const el = document.getElementById(focused);
        if (el) { el.focus(); try { el.setSelectionRange(el.value.length, el.value.length); } catch (e) {} }
      }
      S.last = html;
      S.lastKey = key;
    }
  }

  /* ── действия ─────────────────────────────────────────────────────────── */

  const go = (screen) => { S.screen = screen; S.last = ""; render(); };
  const openSheet = (name) => { S.sheet = name; S.last = ""; render(); };
  const closeSheet = () => { S.sheet = ""; S.last = ""; render(); };

  document.addEventListener("click", (e) => {
    const pick = e.target.closest("[data-pick]");
    if (pick) {
      const id = pick.dataset.pick;
      adapter.selectServer(id);
      closeSheet();
      if (S.snap.state !== "connected") adapter.connect();
      else adapter.disconnect(), setTimeout(() => adapter.connect(), 120);
      return;
    }

    const el = e.target.closest("[data-action]");
    if (!el) return;
    const a = el.dataset.action;
    const snap = S.snap;

    if (a === "onboard") return adapter.onboard();
    if (a === "primary") {
      if (snap.state === "connected" || snap.state === "connecting" || snap.state === "reconnecting") adapter.disconnect();
      else adapter.connect();
      return;
    }
    if (a === "cities") return openSheet("cities");
    if (a === "close-sheet") return closeSheet();
    if (a === "settings") return go("settings");
    if (a === "home") return go("main");
    if (a === "keys") return go("keys");
    if (a === "server") return go("server");
    if (a === "logs") return openSheet("logs");

    if (a === "set-kill") return adapter.setSetting("killSwitch", !snap.settings.killSwitch);
    if (a === "set-split") return adapter.setSetting("split", !snap.settings.split);
    if (a === "set-autostart") return adapter.setSetting("autostart", !snap.settings.autostart);
    if (a === "set-tun") return adapter.setSetting("tun", !snap.settings.tun);

    if (a === "key-paste") return adapter.keyPaste();
    if (a === "key-add") {
      const input = $("#keyInput");
      const v = input ? input.value.trim() : "";
      if (v) adapter.keyAdd(v);
      return;
    }

    if (a === "srv-install" || a === "srv-connect") {
      const val = (id) => { const el = document.getElementById(id); return el ? el.value.trim() : ""; };
      const cfg = {
        host: val("srvHost"), port: val("srvPort") || "22", user: val("srvUser") || "root",
        pass: val("srvPass"), keyfile: val("srvKey"), domain: val("srvDomain")
      };
      if (!cfg.host) return;
      if (a === "srv-install") adapter.srvInstall(cfg); else adapter.srvConnect(cfg);
      return;
    }
    if (a === "srv-issue") {
      const el = document.getElementById("srvNewKey");
      const name = el ? el.value.trim() : "";
      if (name) adapter.srvIssue(name);
      return;
    }
    if (a === "srv-revoke") return adapter.srvRevoke(el.dataset.name || "");
  });

  document.addEventListener("change", (e) => {
    const sel = e.target.closest("[data-setting]");
    if (sel) adapter.setSetting(sel.dataset.setting, sel.value);
  });

  document.addEventListener("input", (e) => {
    if (e.target.id === "citySearch") {
      S.query = e.target.value;
      const overlay = $("#overlay");
      const pos = e.target.selectionStart;
      overlay.innerHTML = viewCities(S.snap, adapter.serverList() || []);
      const input = document.getElementById("citySearch");
      if (input) { input.focus(); input.setSelectionRange(pos, pos); }
    }
  });

  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") {
      if (S.sheet) closeSheet();
      else if (S.screen !== "main") go("main");
      return;
    }
    if (e.key === "Enter" && e.target) {
      if (e.target.id === "keyInput" && e.target.value.trim()) adapter.keyAdd(e.target.value.trim());
      if (e.target.id === "srvNewKey" && e.target.value.trim()) adapter.srvIssue(e.target.value.trim());
    }
  });

  /* ── запуск ───────────────────────────────────────────────────────────── */

  function setAdapter(a) {
    adapter = a;
    adapter.subscribe((snap) => {
      const wasOnboarded = S.snap && S.snap.onboarded;
      S.snap = snap;
      if (snap.onboarded && wasOnboarded === false) S.last = "";
      render();
    });
  }

  window.VEIL_UI = { setAdapter, state: S, mock };
  setAdapter(mock);
})();
