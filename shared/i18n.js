/* Lamha — interface language (Arabic / English) for every page: popup, settings, the card on web pages, and the
 * desktop app. Setting `uiLang` (storage.sync): "auto" (Arabic on an Arabic system, otherwise English) | "ar" | "en".
 * Strings are [Arabic, English]; either may be a function of the values passed to t(). Browser-safe, no dependencies:
 * loaded with <script>, as a content script, or with vm in Node (background in the desktop app, tests). */
// eslint-disable-next-line no-unused-vars
var LamhaI18n = (() => {
  "use strict";

  const S = Object.create(null);
  let lang = "ar";

  /** Adds strings: { key: [arabic, english] }. */
  function add(dict) { Object.assign(S, dict); }

  /** Numbers as the interface writes them: Arabic-Indic digits in Arabic. */
  const num = (n, opts) => Number(n).toLocaleString(lang === "ar" ? "ar-EG" : "en-US", opts);

  /** t("key", { n: 3, name: "x" }): {name} placeholders; numbers are formatted for the language. */
  function t(key, vars) {
    const e = S[key];
    if (!e) return key;
    const v = e[lang === "en" ? 1 : 0];
    if (typeof v === "function") return v(vars || {});
    return String(v).replace(/\{(\w+)\}/g, (m, k) => (vars && k in vars ? (typeof vars[k] === "number" ? num(vars[k]) : vars[k]) : m));
  }

  /** Arabic counting: 1 · 2 (dual) · 3–10 plural · 11+ singular with tanween. */
  const arCount = (n, one, two, few, many) => (n === 1 ? one : n === 2 ? two : n >= 3 && n <= 10 ? `${num(n)} ${few}` : `${num(n)} ${many}`);
  const enCount = (n, one, other) => `${num(n)} ${n === 1 ? one : other}`;

  const resolve = (pref, system) => (pref === "ar" || pref === "en" ? pref : /^ar\b/i.test(String(system || "")) ? "ar" : "en");
  function systemLang() {
    try { if (typeof browser !== "undefined" && browser.i18n && browser.i18n.getUILanguage) return browser.i18n.getUILanguage(); } catch (_) { /* not available here */ }
    return (typeof navigator !== "undefined" && navigator.language) || "en";
  }
  const setLang = l => { lang = l === "en" ? "en" : "ar"; return lang; };
  const dir = () => (lang === "ar" ? "rtl" : "ltr");
  /** The writing direction of a language code ("ar", "fa", "ur-PK" …): "rtl" or "ltr". */
  const RTL = new Set(["ar", "fa", "ur", "he", "iw", "ps", "yi", "ku", "sd"]);
  const textDir = code => (RTL.has(String(code || "").split("-")[0].toLowerCase()) ? "rtl" : "ltr");

  /**
   * Reads the setting and follows its changes. `onChange(lang)` runs when the language changes
   * (pages redraw or reload). Resolves to the language.
   */
  async function init({ onChange } = {}) {
    let pref = "auto";
    try { pref = (await browser.storage.sync.get({ uiLang: "auto" })).uiLang; } catch (_) { /* keep auto */ }
    setLang(resolve(pref, systemLang()));
    try {
      browser.storage.onChanged.addListener((changes, area) => {
        if (area !== "sync" || !changes.uiLang) return;
        const next = resolve(changes.uiLang.newValue || "auto", systemLang());
        if (next !== lang) { setLang(next); if (onChange) onChange(lang); }
      });
    } catch (_) { /* no storage events here */ }
    return lang;
  }

  /**
   * Translates a page's markup: data-i18n="key" (text), data-i18n-html="key" (our own markup, never user text),
   * data-i18n-attr="placeholder:key; aria-label:key; title:key". On a whole document also sets lang and dir.
   */
  function applyDom(root) {
    const doc = root.nodeType === 9 ? root : root.ownerDocument;
    root.querySelectorAll("[data-i18n]").forEach(el => { el.textContent = t(el.dataset.i18n); });
    root.querySelectorAll("[data-i18n-html]").forEach(el => {
      // our own markup (<b>, <kbd>, <a>, <code>…), parsed inertly: DOMParser never runs scripts
      const body = new DOMParser().parseFromString(`<body>${t(el.dataset.i18nHtml)}</body>`, "text/html").body;
      el.replaceChildren(...[...body.childNodes].map(n => doc.importNode(n, true)));
    });
    root.querySelectorAll("[data-i18n-attr]").forEach(el => {
      for (const pair of el.dataset.i18nAttr.split(";")) {
        const [attr, key] = pair.split(":").map(s => s.trim());
        if (attr && key) el.setAttribute(attr, t(key));
      }
    });
    if (root.nodeType === 9) {
      doc.documentElement.lang = lang;
      doc.documentElement.dir = dir();
      doc.documentElement.removeAttribute("data-i18n-pending");
    }
  }

  /* ================= strings shared by the extension and the desktop app ================= */
  add({
    // common
    "common.lamha": ["لمحة", "Lamha"],
    "common.settings": ["الإعدادات", "Settings"],
    "common.copy": ["نسخ", "Copy"],
    "common.copied": ["نُسخ ✓", "Copied ✓"],
    "common.copyFailed": ["تعذّر النسخ", "Couldn't copy"],
    "common.retry": ["إعادة المحاولة", "Try again"],
    "common.remove": ["إزالة", "Remove"],
    "common.clear": ["مسح", "Clear"],
    "common.undo": ["تراجع", "Undo"],
    "p.histCleared": ["مُسح السجل", "History cleared"],
    "common.close": ["إغلاق", "Close"],
    "common.back": ["رجوع", "Back"],
    "common.listen": ["استمع", "Listen"],
    "common.working": ["جارٍ العمل…", "Working…"],
    "common.translating": ["جارٍ الترجمة…", "Translating…"],
    "common.saved": ["تم الحفظ ✓", "Saved ✓"],
    "lang.ar": ["العربية", "Arabic"],
    "lang.en": ["الإنجليزية", "English"],
    "lang.fr": ["الفرنسية", "French"],
    "lang.tr": ["التركية", "Turkish"],
    "lang.ur": ["الأردية", "Urdu"],
    "lang.fa": ["الفارسية", "Persian"],
    "lang.es": ["الإسبانية", "Spanish"],
    "lang.de": ["الألمانية", "German"],

    // context menu (background)
    "menu.lookup": ["لمحة: ترجمة «%s»", "Lamha: translate “%s”"],
    "menu.write": ["لمحة: أدوات الكتابة ✨", "Lamha: writing tools ✨"],
    "menu.page": ["لمحة: ترجمة الصفحة", "Lamha: translate this page"],
    "menu.summary": ["لمحة: تلخيص الصفحة ✨", "Lamha: summarize this page ✨"],

    // writing tools
    "tool.proofread": ["تدقيق لغوي", "Proofread"],
    "tool.improve": ["تحسين الأسلوب", "Improve"],
    "tool.improveShort": ["تحسين", "Improve"],
    "tool.formal": ["رسمي", "Formal"],
    "tool.friendly": ["ودّي", "Friendly"],
    "tool.concise": ["أقصر", "Shorter"],
    "tool.expand": ["أطول", "Longer"],
    "tool.toEnglish": ["اكتبه بالإنجليزية", "Write it in English"],
    "tool.toEnglishShort": ["بالإنجليزية", "In English"],
    "tool.summarize": ["تلخيص", "Summarize"],
    "tool.explain": ["اشرح بالعربية", "Explain"],
    "tool.reply": ["اكتب ردًّا", "Write a reply"],
    "write.noErrors": ["لا توجد أخطاء — نصّك سليم", "No mistakes — your text is fine"],
    "write.noErrorsCheck": ["✓ لا توجد أخطاء — نصّك سليم", "✓ No mistakes — your text is fine"],
    "write.useIt": ["استخدمه", "Use it"],
    "write.useItTitle": ["ضع النتيجة في مربع الكتابة لتكمل عليها", "Put the result in the compose box to keep working on it"],
    "write.again": ["محاولة أخرى", "Try again"],
    "write.topMistake": [v => `أكثر أخطائك: `, v => `Your most frequent mistake: `],
    "write.learnRule": [v => ` (${num(v.n)}) — اعرف القاعدة ←`, v => ` (${num(v.n)}) — learn the rule →`],

    // popup
    "p.subtitle": ["ترجمة وبحث فوري", "Instant lookup & translation"],
    "p.onOff": ["تشغيل / إيقاف", "On / off"],
    "p.enableLamha": ["تفعيل لمحة", "Enable Lamha"],
    "p.permTitle": ["مطلوب إذن الوصول للمواقع", "Site access permission needed"],
    "p.permText": ["ليتمكن لمحة من العمل على الصفحات التي تزورها.", "So Lamha can work on the pages you visit."],
    "p.grant": ["منح الإذن", "Grant permission"],
    "p.siteToggle": ["تفعيل على هذا الموقع", "Enable on this site"],
    "p.translatePage": ["ترجمة هذه الصفحة", "Translate this page"],
    "p.stopPage": ["إيقاف ترجمة الصفحة", "Stop translating the page"],
    "p.reloadNote": ["أعد تحميل الصفحة لتفعيل لمحة عليها.", "Reload the page to use Lamha on it."],
    "p.specialPage": ["لا يمكن للإضافات العمل على صفحات Firefox الخاصة.", "Extensions can't work on Firefox's own pages."],
    "p.modes": ["الوضع", "Mode"],
    "p.tabTranslate": ["ترجمة", "Translate"],
    "p.tabWrite": ["كتابة", "Write"],
    "p.tabReview": ["مراجعة", "Review"],
    "p.qPlaceholder": ["اكتب كلمة أو جملة للترجمة…", "Type a word or sentence to translate…"],
    "p.draftPlaceholder": ["اكتب رسالتك هنا بالإنجليزية — أو بالعربية لتحويلها إلى إنجليزية طبيعية…", "Write your message here in English — or in Arabic to turn it into natural English…"],
    "p.writeTools": ["أدوات الكتابة", "Writing tools"],
    "p.recent": ["آخر عمليات البحث", "Recent lookups"],
    "p.translateAgain": ["ترجم مجددًا", "Translate again"],
    "p.quickLookup": ["بحث سريع", "Quick lookup"],
    "p.busy": ["خدمة الترجمة مشغولة مؤقتًا. انتظر دقيقة ثم حاول مجددًا.", "The translation service is busy. Wait a minute and try again."],
    "p.failed": ["تعذّرت الترجمة. تحقق من الاتصال.", "Couldn't translate. Check your connection."],

    // review (popup)
    "rv.due": [v => `للمراجعة ${num(v.n)}`, v => `${num(v.n)} due`],
    "rv.fresh": [v => `جديدة ${num(v.n)}`, v => `${num(v.n)} new`],
    "rv.totals": [v => `${arCount(v.total, "كلمة واحدة", "كلمتان", "كلمات", "كلمة")} · ${num(v.learned)} محفوظة`, v => `${enCount(v.total, "word", "words")} · ${num(v.learned)} learned`],
    "rv.doneTitle": ["أحسنت! لا توجد كلمات للمراجعة الآن 🎉", "Well done! Nothing to review right now 🎉"],
    "rv.next": [v => `المراجعة القادمة بعد ${v.span}.`, v => `Next review in ${v.span}.`],
    "rv.streak": [
      v => (v.n === 2 ? "🔥 يومان متتاليان من المراجعة" : v.n <= 10 ? `🔥 ${num(v.n)} أيام متتالية من المراجعة` : `🔥 ${num(v.n)} يومًا متتاليًا من المراجعة`),
      v => `🔥 ${num(v.n)}-day review streak`
    ],
    "ms.lookups": [v => `🎉 هذه الكلمة رقم ${num(v.n)} تبحث عنها مع لمحة!`, v => `🎉 That's word number ${num(v.n)} you've looked up with Lamha!`],
    // today (popup): the daily goal — words looked up + review answers — the streak, and the word of the day
    "td.progress": [v => `${num(v.done)} من ${num(v.goal)} اليوم`, v => `${num(v.done)} of ${num(v.goal)} today`],
    "td.left": [v => `بقي ${num(v.n)} لهدف اليوم: ابحث عن كلمات أو راجع بطاقاتك`, v => `${num(v.n)} to go: look up words or review your cards`],
    "td.met": ["حققت هدف اليوم. أحسنت!", "Today's goal is done. Well done!"],
    "td.streak": [v => (v.n === 2 ? "🔥 يومان" : v.n <= 10 ? `🔥 ${num(v.n)} أيام` : `🔥 ${num(v.n)} يومًا`), v => `🔥 ${num(v.n)} days`],
    "td.streakTitle": ["أيام متتالية من التعلّم", "Days in a row of practice"],
    "td.ring": [v => `${num(v.done)} من ${num(v.goal)}`, v => `${num(v.done)} of ${num(v.goal)}`],
    "td.label": ["اليوم", "Today"],
    "td.wordNew": ["كلمة اليوم", "Word of the day"],
    "td.wordDeck": ["كلمة اليوم · من كلماتك", "Word of the day · one of yours"],
    "td.wordDeckTitle": ["كلمة بحثت عنها من قبل وحان وقت مراجعتها", "A word you looked up before, due for review"],
    "td.lookUp": ["اعرض معناها", "See its meaning"],
    "td.goalDone": [v => `🎯 حققت هدف اليوم: ${num(v.n)}!`, v => `🎯 Today's goal reached: ${num(v.n)}!`],
    "rv.progress": ["التقدّم في المراجعة", "Review progress"],
    "rv.session": [v => `راجعت ${arCount(v.n, "كلمة واحدة", "كلمتين", "كلمات", "كلمة")} · عرفت ${num(v.good)} · صعبة ${num(v.hard)} · نسيت ${num(v.again)}`, v => `Reviewed ${enCount(v.n, "word", "words")} · knew ${num(v.good)} · hard ${num(v.hard)} · forgot ${num(v.again)}`],
    // the Animations setting (Settings → Appearance, and the tray menu in the desktop app)
    "motion.auto": ["تلقائية", "Automatic"],
    "motion.full": ["كاملة", "Full"],
    "motion.subtle": ["خفيفة", "Subtle"],
    "motion.off": ["متوقفة", "Off"],
    "rv.emptyTitle": ["لا توجد كلمات بعد", "No words yet"],
    "rv.emptyText": ["ابحث عن كلمات إنجليزية في أي صفحة، وستظهر هنا لتراجعها وتحفظها.", "Look up English words on any page, and they'll show up here to review and learn."],
    "rv.newWord": ["كلمة جديدة", "New word"],
    "rv.show": ["أظهر المعنى ", "Show meaning "],
    "rv.again": ["نسيت", "Forgot"],
    "rv.hard": ["صعبة", "Hard"],
    "rv.good": ["عرفتها", "Knew it"],
    "rv.key": [v => `مفتاح ${v.k}`, v => `Key ${v.k}`],
    "rv.remove": ["إزالة من المراجعة", "Remove from review"],
    "rv.removed": ["أُزيلت الكلمة", "Word removed"],
    "span.min": [v => `${num(v.n)} د`, v => `${num(v.n)} min`],
    "span.hours": [v => arCount(v.n, "ساعة", "ساعتان", "ساعات", "ساعة"), v => enCount(v.n, "hour", "hours")],
    "span.days": [v => arCount(v.n, "يوم", "يومان", "أيام", "يومًا"), v => enCount(v.n, "day", "days")],
    "span.months": [v => arCount(v.n, "شهر", "شهران", "أشهر", "شهرًا"), v => enCount(v.n, "month", "months")],
    "span.years": [v => (v.n === 1 ? "سنة" : `${num(v.n)} سنوات`), v => enCount(v.n, "year", "years")],

    // the card on web pages (content script)
    "c.pill": ["لمحة", "Lamha"],
    "c.lookupWord": ["ابحث عن الكلمة", "Look up the word"],
    "c.translateText": ["ترجم النص", "Translate the text"],
    "c.lookupBtn": ["بحث", "Look up"],
    "c.translateBtn": ["ترجمة", "Translate"],
    "c.writeBtn": ["كتابة", "Write"],
    "c.writeNewHere": ["اكتب نصًّا جديدًا في هذا المربع", "Write something new in this box"],
    "c.writeTip": ["نقرتان على مربع فارغ تفتحان الكتابة مع لمحة. يمكنك إيقاف ذلك من الإعدادات.", "Double-click an empty box to write with Lamha. You can turn this off in Settings."],
    "c.cardLabel": ["لمحة — ترجمة", "Lamha — translation"],
    "c.localBadgeTitle": ["قاموس محلي: النتيجة من القاموس المدمج، ويعمل دون إنترنت", "Offline dictionary: from the built-in dictionary, works without internet"],
    "c.closeEsc": ["إغلاق (Esc)", "Close (Esc)"],
    "c.google": ["ترجمة Google", "Google Translate"],
    "c.cambridge": ["قاموس كامبريدج", "Cambridge Dictionary"],
    "c.dictEn": ["إنجليزي", "English"],
    "c.dictSwitch": ["معنى الكلمة: مترجمًا، أو مشروحًا بلغتها", "Word meaning: translated, or explained in its own language"],
    "c.noDef": ["لا يوجد تعريف لهذه الكلمة باللغة {lang} — جرّب الترجمة.", "No {lang} definition for this word — try the translation."],
    "c.noExplainAI": ["لا يوجد شرح لهذه الكلمة باللغة {lang} هنا. لشرح الكلمات بلغتها اختر مزوّد ذكاء اصطناعي في الإعدادات.", "No {lang} explanation for this word here. To explain words in their own language, choose an AI provider in Settings."],
    "c.root": ["الجذر: {r}", "Root: {r}"],
    "c.plural": ["الجمع: {p}", "Plural: {p}"],
    "c.errNotFound": ["الكلمة غير موجودة في القاموس المحلي", "The word isn't in the offline dictionary"],
    "c.errOfflineMode": ["ترجمة الجمل تحتاج إلى الإنترنت", "Translating sentences needs the internet"],
    "c.errOffline": ["لا يوجد اتصال بالإنترنت", "No internet connection"],
    "c.errBusy": ["خدمة الترجمة مشغولة مؤقتًا", "The translation service is busy"],
    "c.errFailed": ["تعذّرت الترجمة", "Couldn't translate"],
    "c.errLocalOnly": ["أنت في وضع «القاموس المحلي فقط». غيّر مصدر القاموس من الإعدادات للبحث عبر الإنترنت.", "You're in “Offline only” mode. Change the dictionary source in Settings to search online."],
    "c.errCheckConn": ["تحقق من اتصالك ثم حاول مجددًا.", "Check your connection and try again."],
    "c.errTooMany": ["أُرسلت طلبات كثيرة في وقت قصير. انتظر دقيقة ثم أعد المحاولة.", "Too many requests in a short time. Wait a minute and try again."],
    "c.errService": ["حدث خطأ أثناء الاتصال بخدمة الترجمة.", "Something went wrong reaching the translation service."],
    "c.listenPron": ["استمع للنطق", "Listen to the pronunciation"],
    "c.formOf": [" ← صيغة من ", " → a form of "],
    "c.formOfPlain": ["صيغة من ", "A form of "],
    "c.lookUpX": [v => `ابحث عن ${v.w}`, v => `Look up ${v.w}`],
    "c.showAll": ["اضغط لعرض النص كاملًا", "Click to show the whole text"],
    "c.listenOrig": ["استمع للنص الأصلي", "Listen to the original"],
    "c.didYouMean": ["هل تقصد: ", "Did you mean: "],
    "c.didYouMeanEnd": ["؟", "?"],
    "c.inContext": ["في هذا السياق", "In this context"],
    "c.inContextName": ["في هذا السياق: اسم يبقى كما هو", "In this context: a name, kept as is"],
    "c.noArSense": ["لا يوجد معنى عربي لهذا الاستخدام في القاموس المحلي. المعنى المقصود هنا:", "The offline dictionary has no Arabic meaning for this use. The meaning here:"],
    "c.noArDirect": ["لا يوجد معنى عربي مباشر في القاموس المحلي — راجع التعريف أدناه.", "No direct Arabic meaning in the offline dictionary — see the definition below."],
    "c.listenTr": ["استمع للترجمة", "Listen to the translation"],
    "c.copyTr": ["نسخ الترجمة", "Copy the translation"],
    "c.otherMeanings": ["معانٍ أخرى", "Other meanings"],
    "c.showMore": ["عرض المزيد", "Show more"],
    "c.definition": ["التعريف", "Definition"],
    "c.bestSense": ["المعنى الأقرب لسياق الجملة", "The meaning closest to this sentence"],
    "c.examples": ["أمثلة", "Examples"],
    "c.inReview": ["في قائمة المراجعة — اضغط للإزالة", "In your review list — click to remove"],
    "c.addReview": ["أضف إلى المراجعة", "Add to review"],
    "c.addedReview": ["أُضيفت إلى المراجعة", "Added to review"],
    "c.removedReview": ["أُزيلت من المراجعة", "Removed from review"],
    "c.moreMeanings": [v => `معانٍ أخرى (${num(v.n)})`, v => `Other meanings (${num(v.n)})`],
    "c.wikipedia": ["ويكيبيديا", "Wikipedia"],
    "c.readWiki": ["اقرأ المزيد في ويكيبيديا", "Read more on Wikipedia"],
    "c.readWikiHere": ["اقرأ المقالة في لمحة", "Read the article in Lamha"],
    "c.wikiOffline": ["من النسخة المنزّلة · {date}", "Downloaded copy · {date}"],
    "c.wikiOfflineShort": ["من النسخة المنزّلة", "Downloaded copy"],
    "c.wikiOfflineTitle": ["من ويكيبيديا التي نزّلتها على هذا الجهاز، دون إنترنت", "From the Wikipedia you downloaded to this PC, no internet needed"],
    "c.writeLabel": ["لمحة — أدوات الكتابة", "Lamha — writing tools"],
    "c.writeTitle": [v => `أدوات الكتابة · ${v.p}`, v => `Writing tools · ${v.p}`],
    "c.replace": ["استبدال", "Replace"],
    "c.inArabic": ["بالعربية", "In Arabic"],
    "c.inEnglish": ["بالإنجليزية", "In English"],
    "c.editRequest": ["تعديل الطلب", "Edit the request"],
    "c.toolKey": [v => `اضغط ${num(v.n)} لتشغيلها`, v => `Press ${v.n} to run it`],
    "c.composeTitle": [v => `كتابة جديدة · ${v.p}`, v => `Write new · ${v.p}`],
    "c.composeLabel": ["ماذا تريد أن تكتب؟", "What do you want to write?"],
    "c.composePlaceholder": ["ماذا تريد أن تكتب؟ صِف فكرتك بالعربية أو الإنجليزية — مثلًا: بريد لمديري أطلب إجازة يوم الأحد", "What do you want to write? Describe it in English or Arabic — e.g. an email to my manager asking for Sunday off"],
    "c.kind": ["النوع:", "Type:"],
    "c.kindMessage": ["رسالة", "Message"],
    "c.kindEmail": ["بريد إلكتروني", "Email"],
    "c.composeGo": ["اكتب", "Write"],
    "c.insert": ["إدراج", "Insert"],
    "c.inserted": ["تم الإدراج", "Inserted"],
    "c.corrections": [v => `التصحيحات (${num(v.n)})`, v => `Corrections (${num(v.n)})`],
    "c.replyPlaceholder": ["ماذا تريد أن تقول؟ اكتب فكرتك بالعربية أو الإنجليزية (اختياري)", "What do you want to say? Write your idea in English or Arabic (optional)"],
    "c.toneAuto": ["تلقائي", "Automatic"],
    "c.toneShort": ["مختصر", "Short"],
    "c.toneLong": ["مفصّل", "Longer"],
    "c.tone": ["النبرة:", "Tone:"],
    "c.writeReply": ["اكتب الرد", "Write the reply"],
    "c.replaced": ["تم الاستبدال", "Replaced"],
    "c.replaceFailed": ["تعذّر الاستبدال — نُسخ النص، الصقه بنفسك", "Couldn't replace — the text was copied, paste it yourself"],
    "c.nothingToSummarize": ["لا يوجد نص لتلخيصه", "No text to summarize"],
    "c.pageBar": ["ترجمة الصفحة", "Page translation"],
    "c.pageDone": ["تُرجمت الصفحة", "Page translated"],
    "c.betterTr": ["ترجمة أدق", "Better translation"],
    "c.betterTrTitle": [v => `ترجمة النص مجددًا بـ ${v.p}: أدق في التعابير والعامية، وأبطأ`, v => `Translate again with ${v.p}: better with idioms and slang, but slower`],
    "c.aiBadgeTitle": [v => `ترجمها الذكاء الاصطناعي (${v.p})`, v => `Translated by AI (${v.p})`],
    "c.pagePartial": ["تعذّرت ترجمة بعض الأجزاء", "Some parts couldn't be translated"],
    "c.original": ["الأصل", "Original"],
    "c.stopTranslation": ["إيقاف الترجمة", "Stop translating"],
    "c.selectFirst": ["حدّد نصًّا أولًا", "Select some text first"],
    "c.copiedToast": ["تم النسخ", "Copied"]
  });

  // safety net: never leave a Lamha page hidden if something above failed
  if (typeof document !== "undefined" && document.documentElement && document.documentElement.hasAttribute("data-i18n-pending")) {
    setTimeout(() => document.documentElement.removeAttribute("data-i18n-pending"), 1500);
  }

  return { add, t, num, arCount, enCount, resolve, systemLang, setLang, lang: () => lang, dir, textDir, init, applyDom };
})();
