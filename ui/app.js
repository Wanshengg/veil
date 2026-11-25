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

  /* ── тема: светлая / тёмная / системная ───────────────────────────────── */

  const THEME_KEY = "veil.theme";

  const resolveTheme = (mode) =>
    mode === "dark" ? "dark"
    : mode === "light" ? "light"
    : (window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");

  const applyTheme = (snap) => {
    let mode = snap && snap.settings && snap.settings.theme;
    if (!mode) { try { mode = localStorage.getItem(THEME_KEY); } catch (e) {} }
    if (!mode) mode = "system";
    const resolved = resolveTheme(mode);
    if (document.documentElement.dataset.theme !== resolved) {
      document.documentElement.dataset.theme = resolved;
    }
    return mode;
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
      srvInstall() {}, srvKeys() {}, srvIssue() {}, srvRevoke() {}, srvForget() {}, refreshSubs() {}, profileDel() {},
      poolAdd() {}, poolDel() {}, poolIssue() {}, clipWrite() {}, poolKeys() {}, poolRevoke() {}, poolIssueOne() {},
      cascadeBuild() {}, zapInstall() {}, zapToggle() {}, zapCheck() {}, zapSelect() {}, zapAutoconnect() {},
      zapAutoStrategy() {}
    };
  })();

  let adapter = mock;

  // Строка фактов: меняется каждую секунду (таймер), поэтому обновляется точечно,
  // без перерисовки экрана — иначе всё мигает.
  const factsText = (snap, servers) => {
    const active = servers.find((x) => x.id === snap.serverId) || servers[0] || null;
    const st = snap.state;
    if (st === "connected") {
      const ip = active && active.exitIp && /^\d+\./.test(active.exitIp) ? active.exitIp
        : (active && /^\d+\.\d+\.\d+\.\d+$/.test(active.city || "") ? active.city : "—");
      const ping = active && active.ping ? active.ping + " мс" : "—";
      return `${ip} · ${ping} · ${clock(snap.traffic.uptime)}`;
    }
    if (st === "disconnected") {
      const c = active ? country(active) : null;
      return active ? `Выбран: ${c ? c.name : active.country}` : "Профилей нет";
    }
    if (st === "connecting") return snap.note || "Рукопожатие";
    if (st === "reconnecting") return "Повторное рукопожатие";
    return "";
  };

  /* ── мелкие части ─────────────────────────────────────────────────────── */

  const clock = (sec) => {
    if (sec === null || sec === undefined) return "—";
    if (sec === 0) return "0 с";
    if (sec < 60) return sec + " с";
    const m = Math.floor(sec / 60);
    if (m < 60) return m + " мин";
    return Math.floor(m / 60) + " ч " + (m % 60) + " мин";
  };

  // поле формы: подпись капсом + ввод
  const field = (id, label, placeholder, type, value) => `
    <label class="field">
      <span class="caps">${label}</span>
      <input id="${id}" type="${type || "text"}" placeholder="${placeholder || ""}"
             value="${esc(value || "")}">
    </label>`;

  const switchBtn = (on, action, label) =>
    `<button type="button" class="switch" role="switch" aria-checked="${on}" aria-label="${esc(label)}" data-action="${action}">
       <span class="track"><span class="knob"></span></span>
     </button>`;

  const row = (title, value, extra = "") =>
    `<div class="row"><span class="t">${esc(title)}</span>${extra || `<span class="v">${esc(value || "—")}</span>`}</div>`;

  // Знак «фраза»: строка и точка. Цвета берутся из темы.
  const mark = (w = 76) => `
    <svg width="${w}" height="${Math.round(w * 0.3)}" viewBox="0 0 100 30" role="img" aria-label="phrase" class="mark">
      <rect x="0" y="18" width="72" height="8" fill="var(--ink)" />
      <rect x="76" y="2" width="24" height="24" fill="var(--accent)" />
    </svg>`;

  /* ── экран: онбординг ─────────────────────────────────────────────────── */

  const viewOnboarding = () => `
    <section class="screen">
      <div class="mark-row">${mark(96)}</div>
      <h1 class="phrase">Подключение<br>к своему серверу.</h1>
      <p class="facts onboarding-note">Ключ даёт тот, у кого уже есть сервер,<br>или поставьте свой — это одна кнопка.</p>
      <div class="spacer"></div>
      <div class="onboarding-actions">
        <button class="btn filled" data-action="onboard-key">У меня есть ключ</button>
        <button class="btn outline" data-action="onboard-server">Создать свой сервер</button>
      </div>
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

    const facts = factsText(snap, servers);

    // нет ни одного ключа — ведём добавлять, а не «подключаться» в пустоту
    const empty = servers.length === 0;

    const primary = empty ? ["Добавить ключ", "filled"] : {
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
      ${facts ? `<${st === "disconnected" ? 'button class="facts facts-btn" data-action="cities" aria-label="Выбрать сервер"' : 'div class="facts"'}>${st === "connected" ? '<span class="dot" aria-hidden="true"></span>' : ""}<span data-live="facts">${esc(facts)}</span>${st === "disconnected" ? '<span class="chev">▾</span>' : ""}</${st === "disconnected" ? "button" : "div"}>` : ""}
      ${st === "error" ? `<p class="reason">${esc(snap.error || "Провайдер блокирует этот порт")}</p>` : ""}

      <div class="spacer"></div>
      <button class="btn ${primary[1]}" data-action="${empty ? "keys" : "primary"}" ${busy ? "" : ""}>${primary[0]}</button>
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
      const row = `
        <button class="city" data-pick="${esc(s.id)}" ${isActive ? 'aria-current="true"' : ""}>
          <span class="mark">${isActive ? "<i></i>" : ""}</span>
          <span>
            <span class="name">${esc(c ? c.name : s.country)}</span>
            <span class="country">${esc(/^\d+\./.test(s.city) ? s.city : (s.sub ? s.city : s.country))}</span>
          </span>
          <span class="ping">${s.ping ? s.ping : "—"}</span>
        </button>`;
      // удалить можно только свой ключ: серверы подписки уходят вместе с пулом
      return s.own
        ? `<div class="city-row">${row}<button class="del" data-del="${esc(s.id)}" aria-label="Удалить">×</button></div>`
        : row;
    };

    const pool = list.filter((s) => s.sub);
    const own = list.filter((s) => !s.sub);

    const section = (title, items) => items.length
      ? `<div class="caps section">${title}</div>${items.map(item).join("")}`
      : "";

    return `
      <div class="scrim" data-action="close-sheet"></div>
      <div class="sheet" role="dialog" aria-label="Выбор сервера">
        <div class="head">
          <input class="search" id="citySearch" type="search" placeholder="Поиск" value="${esc(S.query)}" aria-label="Поиск сервера">
        </div>
        <div class="body">
          ${list.length
            ? section("Подписка", pool) + section("Свои ключи", own)
            : `<p class="msg">Ничего не нашлось</p>`}
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
        ${row("РУ-трафик", "через РУ-сервер")}
      </div>

      <div class="group">
        <div class="caps">Маршрутизация</div>
        <div class="row"><span class="t">TUN-режим</span>${switchBtn(!!s.tun, "set-tun", "TUN-режим")}</div>
        <div class="row clickable" data-action="dns">
          <span class="t">DNS</span>
          <span class="v">${esc(s.dns || "—")} ›</span>
        </div>
      </div>

      <div class="group">
        <div class="caps">Профиль</div>
        <div class="row clickable" data-action="keys"><span class="t">Ключ из буфера</span><span class="v">›</span></div>
        <div class="row clickable" data-action="myservers"><span class="t">Мои серверы</span><span class="v">${(snap.poolServers || []).length} ›</span></div>
        <div class="row clickable" data-action="newserver"><span class="t">Создать новый сервер</span><span class="v">›</span></div>
        <div class="row clickable" data-action="cities"><span class="t">Серверы</span><span class="v">${snap.srvPoolCount || 0} подписка · ${snap.srvOwnCount || 0} ключи ›</span></div>
        <div class="row clickable" data-action="sub-refresh"><span class="t">Обновить подписку</span><span class="v">↻</span></div>
        <div class="row clickable" data-action="zapret"><span class="t">Запрет (DPI)</span><span class="v">${snap.zap && snap.zap.running ? "включён" : (snap.zap && snap.zap.installed ? "выключен" : "не установлен")} ›</span></div>
      </div>

      <div class="group">
        <div class="caps">Приложение</div>
        <div class="row">
          <span class="t">Тема</span>
          <span class="choice" role="group" aria-label="Тема">
            ${[["light", "Светлая"], ["dark", "Тёмная"], ["system", "Системная"]].map(([v, label]) =>
              `<button type="button" data-action="set-theme" data-value="${v}" aria-pressed="${(s.theme || "system") === v}">${label}</button>`
            ).join("")}
          </span>
        </div>
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

    // шаг 1: как подключаемся
    if (!S.newMode) {
      return `
      <section class="screen compact">
        <button class="back" data-action="settings">‹ Назад</button>
        <div class="group">
          <div class="caps">Новый сервер</div>
          <h2 class="phrase small">Как подключаемся?</h2>
        </div>
        <div class="group">
          <div class="actions">
            <button class="btn filled" data-action="new-mode-cascade">Каскад через РУ-сервер</button>
          </div>
          <p class="msg">Домашние и зарубежные сайты идут через зарубежный сервер, РУ-сайты — напрямую с российского.
            РУ-сервер принимает подключения из России и раздаёт список ключей.</p>
          <div class="actions">
            <button class="btn outline" data-action="new-mode-direct">Напрямую</button>
          </div>
          <p class="msg">Один сервер без каскада. Подходит, если сервер не блокируют и РУ-выход не нужен.</p>
        </div>
      </section>`;
    }

    // шаг 2а: каскад — сначала РУ, потом зарубежный
    if (S.newMode === "cascade") {
      return `
      <section class="screen compact">
        <button class="back" data-action="new-mode-back">‹ Назад</button>
        <div class="group">
          <div class="caps">Каскад через РУ</div>
          ${field("ruHost", "РУ-сервер", "IP или домен российского VPS", "text")}
          ${field("ruPort", "Порт SSH", "22", "number", "22")}
          ${field("ruUser", "Пользователь", "root", "text", "root")}
          ${field("ruSecret", "Пароль или путь к ключу", "пароль или C:\\Users\\you\\.ssh\\id_ed25519")}
          ${field("ruDomain", "Домен РУ (необязательно)", "для HTTPS-раздачи списка")}
          <p class="msg">РУ-сервер станет релеем, раздатчиком списка и выходом для российских сайтов.</p>
        </div>

        <div class="group">
          <div class="caps">Зарубежный сервер</div>
          ${field("fHost", "Сервер", "IP или домен", "text")}
          ${field("fPort", "Порт SSH", "22", "number", "22")}
          ${field("fUser", "Пользователь", "root", "text", "root")}
          ${field("fSecret", "Пароль или путь к ключу", "пароль или путь к ключу")}
          ${field("fDomain", "Домен", "необязательно")}
          ${field("fCc", "Код страны", "SE, FI, NL…")}
          ${field("fCountry", "Название", "Швеция, Финляндия…")}
        </div>

        <div class="group">
          <div class="actions">
            <button class="btn filled" data-action="cascade-build" ${busy || snap.cascadeBusy ? "disabled" : ""}>Собрать каскад и подписку</button>
          </div>
          ${snap.cascadeBusy ? `<p class="msg">${esc(snap.cascadeStep || "Работаю…")}</p>` : ""}
          ${snap.cascadeResult ? `<div class="keybox">${esc(snap.cascadeResult)}</div>` : ""}
          ${snap.poolKey ? `
            <div class="keybox">${esc(snap.poolKey)}</div>
            <div class="actions"><button class="btn outline" data-action="pool-copy">Скопировать ключ</button></div>` : ""}
        </div>
      </section>`;
    }

    // шаг 2б: напрямую — обычная установка одного сервера
    return `
    <section class="screen compact">
      <button class="back" data-action="new-mode-back">‹ Назад</button>
      <div class="group">
        <div class="caps">Сервер напрямую</div>
        ${field("srvHost", "Сервер", "203.0.113.10 или домен", "text", snap.srvHost)}
        ${field("srvPort", "Порт", "22", "number", "22")}
        ${field("srvUser", "Пользователь", "root", "text", snap.srvUser)}
        ${field("srvSecret", "Пароль или путь к ключу",
          snap.srvSaved ? "сохранено — оставь пустым" : "пароль или C:\\Users\\you\\.ssh\\id_ed25519")}
        ${snap.srvInstalled ? "" : field("srvDomain", "Домен для сертификата", "необязательно — тогда самоподписанный")}
        ${snap.srvSaved
          ? `<div class="row clickable" data-action="srv-forget">
               <span class="t">Данные SSH сохранены</span><span class="v">Забыть ›</span>
             </div>`
          : ""}
        <div class="actions">
          <button class="btn filled" data-action="srv-connect" ${busy ? "disabled" : ""}>Подключиться</button>
        </div>
        <p class="msg">${snap.srvInstalled
          ? "Сервер уже установлен — ключ для него уже есть в списке серверов."
          : "Если сервер ещё не стоит, приложение само загрузит и настроит его и выдаст ключ."}</p>
        <p class="msg ${/ошиб|не принял|не удалось/i.test(snap.srvStatus || "") ? "err" : ""}">${esc(snap.srvStatus || "")}</p>
      </div>
    </section>`;
  };

  /* ── запрет (DPI-bypass) ──────────────────────────────────────────────── */

  const ordered = (list) => {
    const rank = (s) => (s.ok === true ? 0 : (s.ok === undefined ? 1 : 2));
    return [...list].sort((a, b) => {
      const ra = rank(a), rb = rank(b);
      if (ra !== rb) return ra - rb;
      if (ra === 0) {
        const sa = (a.ytMs || 0) + (a.dcMs || 0), sb = (b.ytMs || 0) + (b.dcMs || 0);
        if (sa !== sb) return sa - sb;
      }
      return String(a.name).localeCompare(String(b.name), "ru", { numeric: true });
    });
  };

  const viewZapret = (snap) => {
    const z = snap.zap || { strategies: [] };
    const sel = z.strategy || "";
    return `
    <section class="screen compact">
      <button class="back" data-action="settings">‹ Назад</button>

      <div class="group">
        <div class="caps">Запрет · DPI</div>
        <div class="row"><span class="t">Включён</span>${switchBtn(!!z.running, "zap-toggle", "Запрет")}</div>
        <p class="msg">Обходит DPI на уровне пакетов: YouTube и Discord работают без туннеля.
          Нужны права администратора; при выходе из приложения запрет останавливается.</p>
        <div class="row"><span class="t">Версия</span><span class="v">${esc(z.version || "не установлена")}${
          z.version && z.latest
            ? (z.updateAvailable ? " · есть " + esc(z.latest) : " · последняя")
            : ""}</span></div>
        <div class="row"><span class="t">Подбирать стратегию сам</span>${switchBtn(!!z.autoStrategy, "zap-autostrategy", "Авто-подбор")}</div>
        <p class="msg">Включает лучшую из проверенных. Дальше раз в минуту проверяет, что YouTube открывается:
          если стратегия перестала работать — сам переключится на другую.</p>
        <div class="row"><span class="t">Автозапуск при старте</span>${switchBtn(!!z.autoconnect, "zap-autoconnect", "Автозапуск запрета")}</div>
        <div class="actions">
          <button class="btn filled" data-action="zap-install" ${z.busy ? "disabled" : ""}>${
            !z.installed ? "Установить" : (z.updateAvailable ? "Обновить до " + esc(z.latest) : "Проверить обновление")}</button>
        </div>
        ${z.installed ? `<div class="actions">
          <button class="btn outline" data-action="zap-reinstall" ${z.busy ? "disabled" : ""}>Переустановить набор</button>
        </div>` : ""}
        ${z.status ? `<p class="msg">${esc(z.status)}</p>` : ""}
      </div>

      <div class="group">
        <div class="caps">Стратегии${z.strategies.length ? " · " + z.strategies.length : ""}</div>
        ${z.strategies.length
          ? ordered(z.strategies).map((s) => `
            <div class="zap-row clickable" data-action="zap-select" data-id="${esc(s.id)}">
              <div class="zap-top">
                <span class="zap-name">${esc(s.name)}${s.best ? ` <span class="zap-badge">Лучшая</span>` : ""}</span>
                <span class="zap-state">${s.id === sel ? "выбрана" : (s.ok === true ? "работает" : (s.ok === false ? "не работает" : ""))}</span>
              </div>
              <div class="zap-id">${esc(s.id)}</div>
              ${s.ok === undefined
                ? `<div class="zap-metrics"><span class="m">не проверялась</span></div>`
                : `<div class="zap-metrics">
                     <span class="m"><i>YT</i> ${s.ytCode || "—"} · ${s.ytMs || "—"} мс · ${s.ytPass}/${s.ytTotal}</span>
                     <span class="m"><i>DC</i> ${s.dcCode || "—"} · ${s.dcMs || "—"} мс · ${s.dcPass}/${s.dcTotal}</span>
                     <span class="m">ICMP ${esc(s.icmp)}</span>
                     <span class="m">проверок ${s.checksOk}/${s.checksTotal}</span>
                   </div>`}
            </div>`).join("")
          : `<p class="msg">${z.installed ? "В наборе нет стратегий" : "Сначала установи набор"}</p>`}
        <div class="actions">
          <button class="btn outline" data-action="zap-check" ${z.busy || !z.installed ? "disabled" : ""}>Проверить все стратегии</button>
        </div>
        <p class="msg">Проверка по очереди запускает каждую стратегию и смотрит YouTube и Discord. Лучшая включается сама.</p>
      </div>
    </section>`;
  };

  /* ── подтверждение отзыва ─────────────────────────────────────────────── */

  const viewRevokeConfirm = () => `
    <div class="scrim" data-action="close-sheet"></div>
    <div class="sheet" role="dialog" aria-label="Отзыв ключа">
      <div class="head"><div class="caps">Отзыв ключа</div></div>
      <div class="body">
        <p class="facts">Ключ «${esc(S.revokeName || "")}» перестанет работать на всех серверах списка. Вернуть его можно только новой выдачей.</p>
        <div class="actions">
          <button class="btn danger" data-action="revoke-yes">Отозвать</button>
          <button class="btn outline" data-action="close-sheet">Отмена</button>
        </div>
      </div>
    </div>`;

  /* ── мои серверы: что уже сделано + выдача ключей ─────────────────────── */

  const roleText = (h) => h.publisher ? "раздатчик списка" : (h.role === "ru" ? "РУ · каскад" : "зарубежный");

  const viewMyServers = (snap) => {
    const busy = !!snap.srvBusy;
    const list = snap.poolServers || [];
    const ru = list.filter((h) => h.role === "ru").length;
    const pub = list.filter((h) => h.publisher).length;
    return `
    <section class="screen compact">
      <button class="back" data-action="settings">‹ Назад</button>

      <div class="group">
        <div class="caps">Мои серверы · ${list.length}</div>
        ${list.length
          ? list.map((h) => `
            <div class="row">
              <span class="t">${esc(h.label)}<br><span class="sub">${roleText(h)}</span></span>
              <button class="x" data-action="pool-del" data-host="${esc(h.host)}" aria-label="Удалить">×</button>
            </div>`).join("")
          : `<p class="msg">Пока нет ни одного сервера. Создай первый — это одна кнопка.</p>`}
        <div class="actions">
          <button class="btn filled" data-action="newserver">Создать новый сервер</button>
        </div>
        ${list.length ? `<p class="msg">${ru ? `Каскад через РУ: ${ru}. ` : ""}${pub ? "Раздатчик списка отмечен ★." : "Раздатчик не выбран — ключи подписки выдавать некуда."}</p>` : ""}
        ${snap.signKey
          ? `<p class="msg ${snap.signKeyMatches ? "" : "err"}">Ключ подписи: ${esc((snap.signKey || "").slice(0, 12))}…${snap.signKeyMatches ? "" : " — не совпадает со встроенным: ключи, выданные раньше, могут не проверяться."}</p>`
          : `<p class="msg err">Ключа подписи нет — выдача и каскад работать не будут.</p>`}
      </div>

      <div class="group">
        <div class="caps">Выданные ключи · ${(snap.poolNames || []).length}</div>
        ${(snap.poolNames || []).length
          ? (snap.poolNames || []).map((n) => `
            <div class="row">
              <span class="t">${esc(n)}</span>
              <button class="x" data-action="pool-revoke" data-name="${esc(n)}" aria-label="Отозвать">×</button>
            </div>`).join("")
          : `<p class="msg">${snap.poolNamesBusy ? "Читаю серверы…" : "Ключей нет — только общий."}</p>`}
        <div class="actions">
          <button class="btn outline" data-action="pool-refresh" ${snap.poolNamesBusy ? "disabled" : ""}>${snap.poolNamesBusy ? "Читаю…" : "Обновить список"}</button>
        </div>
        <p class="msg">× снимает ключ со всех серверов и удаляет его личный список.</p>
      </div>

      <div class="group">
        <div class="caps">Выдать ключ подписки</div>
        <label class="field">
          <span class="caps">Имя человека</span>
          <input id="poolWho" type="text" placeholder="мама, друг, тест" ${busy ? "disabled" : ""}>
        </label>
        <div class="actions">
          <button class="btn filled" data-action="pool-issue" ${busy || !list.length ? "disabled" : ""}>Выдать подписку</button>
        </div>
        <p class="msg">Строка на все серверы списка: человек видит Россию, Швецию и Финляндию, переключается сам.</p>
        <div class="actions">
          <button class="btn outline" data-action="pool-issue-one" ${busy || !list.length ? "disabled" : ""}>Ключ на РУ-сервер</button>
        </div>
        <p class="msg">Один сервер: подключение сразу, без выбора городов. Для тех, кому нужен только российский выход.</p>
        ${snap.poolKeyMsg ? `<p class="msg">${esc(snap.poolKeyMsg)}</p>` : ""}
        ${snap.poolKey ? `
          <div class="keybox">${esc(snap.poolKey)}</div>
          <div class="actions"><button class="btn outline" data-action="pool-copy">Скопировать ключ</button></div>` : ""}
      </div>
    </section>`;
  };

  /* ── шторка: DNS ──────────────────────────────────────────────────────── */

  const viewDns = (snap) => {
    const values = ["Через туннель", "Системный", "1.1.1.1", "9.9.9.9"];
    const current = snap.settings.dns;
    return `
      <div class="scrim" data-action="close-sheet"></div>
      <div class="sheet" role="dialog" aria-label="DNS">
        <div class="head"><div class="caps">DNS</div></div>
        <div class="body">
          ${values.map((v) => `
            <button class="option" data-dns="${esc(v)}" ${v === current ? 'aria-current="true"' : ""}>
              <span class="mark">${v === current ? "<i></i>" : ""}</span>
              <span>${esc(v)}</span>
            </button>`).join("")}
        </div>
      </div>`;
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
    applyTheme(snap);

    let html;
    if (!snap.onboarded) html = viewOnboarding();
    else if (S.screen === "settings") html = viewSettings(snap);
    else if (S.screen === "keys") html = viewKeys(snap);
    else if (S.screen === "server") html = viewServer(snap);
    else if (S.screen === "myservers") html = viewMyServers(snap);
    else if (S.screen === "zapret") html = viewZapret(snap);
    else html = viewMain(snap, servers);

    const overlay = $("#overlay");
    const overlayHtml = S.sheet === "cities" ? viewCities(snap, servers)
      : S.sheet === "dns" ? viewDns(snap)
      : S.sheet === "revoke" ? viewRevokeConfirm()
      : S.sheet === "logs" ? viewLogs(snap)
      : "";

    // структурный ключ: всё, что требует перерисовки. Таймер (uptime) сюда не входит.
    const key = [
      S.screen, S.sheet, snap.state, snap.onboarded ? 1 : 0, snap.serverId,
      snap.settings.killSwitch, snap.settings.split, snap.settings.autostart, snap.settings.tun, snap.settings.dns,
      snap.settings.theme,
      snap.error || "", snap.keyMsg || "", snap.srvStatus || "", snap.srvBusy ? 1 : 0,
      snap.srvInstalled ? 1 : 0, snap.srvSaved ? 1 : 0,
      (snap.srvKeys || []).join(","), servers.length,
      (snap.poolServers || []).map((h) => h.host + (h.publisher ? "*" : "") + (h.role || "")).join(","),
      snap.poolKeyMsg || "", snap.poolKey || "", S.newRu ? 1 : 0, S.newPub ? 1 : 0,
      (snap.poolNames || []).join(","), snap.poolNamesBusy ? 1 : 0, S.revokeName || "",
      snap.signKey || "", snap.signKeyMatches ? 1 : 0,
      S.newMode || "", snap.cascadeBusy ? 1 : 0, snap.cascadeStep || "", snap.cascadeResult || "",
      JSON.stringify(snap.zap || {})
    ].join("|");

    if (key !== S.lastKey) {
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
      S.lastKey = key;
    }

    // ежесекундное обновление — только текст фактов, без пересборки дерева
    const live = document.querySelector('[data-live="facts"]');
    if (live) {
      const text = factsText(snap, servers);
      if (live.textContent !== text) live.textContent = text;
    }
  }

  /* ── действия ─────────────────────────────────────────────────────────── */

  const go = (screen) => { S.screen = screen; S.last = ""; render(); };
  const openSheet = (name) => { S.sheet = name; S.last = ""; render(); };
  const closeSheet = () => { S.sheet = ""; S.last = ""; render(); };

  document.addEventListener("click", (e) => {
    const del = e.target.closest("[data-del]");
    if (del) { adapter.profileDel(del.dataset.del); return; }

    const dnsPick = e.target.closest("[data-dns]");
    if (dnsPick) { adapter.setSetting("dns", dnsPick.dataset.dns); closeSheet(); return; }

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
    if (a === "onboard-key") { adapter.onboard(); S.screen = "keys"; return render(); }
    if (a === "onboard-server") { adapter.onboard(); S.screen = "server"; return render(); }
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
    if (a === "dns") return openSheet("dns");
    if (a === "sub-refresh") return adapter.refreshSubs();
    if (a === "set-theme") {
      const value = el.dataset.value;
      try { localStorage.setItem(THEME_KEY, value); } catch (e) {}
      adapter.setSetting("theme", value);
      S.lastKey = "";                 // перерисовать строку настроек
      applyTheme({ settings: { theme: value } });
      render();
      return;
    }

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
        secret: val("srvSecret"), domain: val("srvDomain")
      };
      if (!cfg.host) return;
      adapter.srvConnect(cfg);
      return;
    }
    if (a === "srv-issue") {
      const el = document.getElementById("srvNewKey");
      const name = el ? el.value.trim() : "";
      if (name) adapter.srvIssue(name);
      return;
    }
    if (a === "srv-revoke") return adapter.srvRevoke(el.dataset.name || "");
    if (a === "srv-forget") return adapter.srvForget();
    if (a === "pool-publisher") { S.poolPublisher = !S.poolPublisher; S.lastKey = ""; return render(); }
    if (a === "zapret") { S.screen = "zapret"; return render(); }
    if (a === "zap-install") return adapter.zapInstall(false);
    if (a === "zap-reinstall") return adapter.zapInstall(true);
    if (a === "zap-toggle") return adapter.zapToggle("");
    if (a === "zap-check") return adapter.zapCheck();
    if (a === "zap-autoconnect") return adapter.zapAutoconnect();
    if (a === "zap-autostrategy") return adapter.zapAutoStrategy();
    if (a === "zap-select") return adapter.zapSelect(el.dataset.id || "");
    if (a === "myservers") {
      S.screen = "myservers";
      if (!S.namesAsked) { S.namesAsked = true; adapter.poolKeys(); }
      return render();
    }
    if (a === "pool-refresh") return adapter.poolKeys();
    if (a === "pool-revoke") { S.revokeName = el.dataset.name || ""; S.sheet = "revoke"; S.lastKey = ""; return render(); }
    if (a === "revoke-yes") {
      const nm = S.revokeName || "";
      S.sheet = ""; S.revokeName = ""; S.lastKey = "";
      render();
      if (nm) adapter.poolRevoke(nm);
      return;
    }
    if (a === "pool-issue-one") {
      const who = (document.getElementById("poolWho") || {}).value || "";
      if (!who.trim()) return adapter.poolIssue("");   // покажет «впиши имя»
      adapter.poolIssueOne(who.trim());
      return;
    }
    if (a === "newserver") { S.screen = "server"; S.newMode = ""; return render(); }
    if (a === "new-mode-cascade") { S.newMode = "cascade"; S.lastKey = ""; return render(); }
    if (a === "new-mode-direct") { S.newMode = "direct"; S.lastKey = ""; return render(); }
    if (a === "new-mode-back") { S.newMode = ""; S.lastKey = ""; return render(); }
    if (a === "cascade-build") {
      const v = (id) => { const e = document.getElementById(id); return e ? e.value.trim() : ""; };
      adapter.cascadeBuild({
        ruHost: v("ruHost"), ruPort: v("ruPort") || "22", ruUser: v("ruUser") || "root",
        ruSecret: v("ruSecret"), ruDomain: v("ruDomain"),
        fHost: v("fHost"), fPort: v("fPort") || "22", fUser: v("fUser") || "root",
        fSecret: v("fSecret"), fDomain: v("fDomain"), fCc: v("fCc"), fCountry: v("fCountry")
      });
      return;
    }
    if (a === "new-ru") { S.newRu = !S.newRu; S.lastKey = ""; return render(); }
    if (a === "new-pub") { S.newPub = !S.newPub; S.lastKey = ""; return render(); }
    if (a === "pool-add") {
      const get = (id) => { const e = document.getElementById(id); return e ? e.value.trim() : ""; };
      if (!get("srvHost")) return;
      adapter.poolAdd({
        label: get("srvHost"), host: get("srvHost"), port: get("srvPort") || "22",
        user: get("srvUser") || "root", secret: get("srvSecret"),
        publisher: S.newPub ? "true" : "false",
        role: S.newRu ? "ru" : ""
      });
      S.screen = "myservers";
      return;
    }
    if (a === "pool-del") return adapter.poolDel(el.dataset.host || "");
    if (a === "pool-issue") {
      const who = (document.getElementById("poolWho") || {}).value || "";
      adapter.poolIssue(who.trim());
      return;
    }
    if (a === "pool-copy") { adapter.clipWrite(S.snap.poolKey || ""); return; }
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

  let lastToast = 0, toastTimer = null;

  function maybeToast(snap) {
    const t = snap && snap.toast;
    if (!t || !t.seq || t.seq === lastToast) return;
    lastToast = t.seq;
    const el = document.getElementById("toast");
    if (!el) return;
    el.textContent = t.text || "";
    el.className = "toast show " + (t.ok ? "ok" : "fail");
    if (toastTimer) clearTimeout(toastTimer);
    toastTimer = setTimeout(() => {
      el.className = "toast " + (t.ok ? "ok" : "fail");
    }, 2600);
  }

  function setAdapter(a) {
    adapter = a;
    adapter.subscribe((snap) => {
      const wasOnboarded = S.snap && S.snap.onboarded;
      S.snap = snap;
      if (snap.onboarded && wasOnboarded === false) S.last = "";
      maybeToast(snap);
      render();
    });
  }

  if (window.matchMedia) {
    const mq = window.matchMedia("(prefers-color-scheme: dark)");
    const onScheme = () => {
      const mode = (S.snap && S.snap.settings && S.snap.settings.theme) || "system";
      if (mode === "system") { applyTheme(S.snap); }
    };
    mq.addEventListener ? mq.addEventListener("change", onScheme) : mq.addListener(onScheme);
  }

  window.VEIL_UI = { setAdapter, state: S, mock };
  setAdapter(mock);
})();
