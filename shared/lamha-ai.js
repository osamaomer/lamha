/* Lamha — writing-tools helpers shared by the background script, content script, popup and options:
 * proofreading mistake categories, AI error messages and the word-level diff. */
// eslint-disable-next-line no-unused-vars
var LamhaAI = (() => {
  "use strict";

  /** Proofreading mistake types: `en` goes into the prompt, `ar` and `tip` are shown to the user. */
  const CATEGORIES = {
    articles: {
      en: "articles (a/an/the)", ar: "أدوات التعريف والتنكير (a / an / the)",
      tip: "استخدم a/an مع الاسم المفرد المعدود عند ذكره أول مرة، و the لشيء معروف أو ذُكر من قبل. لا تضع the قبل الأسماء العامة: Life is short وليس The life is short."
    },
    prepositions: {
      en: "prepositions", ar: "حروف الجر (in / on / at …)",
      tip: "حروف الجر لا تُترجم حرفيًا من العربية: in للشهور والسنوات والمدن، on للأيام والتواريخ، at للساعات والأماكن المحددة. وبعض الأفعال لا تأخذ حرف جر: discuss the plan وليس discuss about the plan."
    },
    verb_tense: {
      en: "verb tenses", ar: "أزمنة الأفعال",
      tip: "الماضي البسيط لحدث انتهى في وقت محدد (I finished yesterday)، والمضارع التام لحدث له أثر الآن أو بلا وقت محدد (I have finished). ولا تستخدم المضارع المستمر مع أفعال الحالة: I know وليس I am knowing."
    },
    agreement: {
      en: "subject–verb agreement", ar: "توافق الفاعل والفعل",
      tip: "مع he / she / it أو اسم مفرد يأخذ الفعل في المضارع s: She works, The team is. ومع الجمع بدون s: They work, The results are."
    },
    plurals: {
      en: "singular and plural nouns", ar: "المفرد والجمع",
      tip: "بعد الأعداد وكلمات مثل many / several / one of the يأتي الجمع: three days, one of the best places. وبعض الكلمات لا تُجمع: information, advice, equipment."
    },
    word_choice: {
      en: "word choice", ar: "اختيار الكلمات",
      tip: "الترجمة الحرفية من العربية تُنتج كلمات صحيحة لكنها غير طبيعية. تعلّم التراكيب الشائعة كاملة: make a decision وليس do a decision، و take a photo وليس make a photo."
    },
    spelling: {
      en: "spelling", ar: "الإملاء",
      tip: "انتبه للكلمات المتشابهة في النطق: their / there / they're، و your / you're، و its / it's."
    },
    punctuation: {
      en: "punctuation and capitalization", ar: "علامات الترقيم والأحرف الكبيرة",
      tip: "ابدأ الجملة وأسماء الأعلام والأيام والشهور و I بحرف كبير. وضع مسافة بعد الفاصلة والنقطة، لا قبلهما."
    },
    word_order: {
      en: "word order", ar: "ترتيب الكلمات",
      tip: "الصفة قبل الاسم (a big house)، والسؤال غير المباشر بترتيب الجملة الخبرية: Can you tell me where the station is وليس where is the station."
    },
    sentence_structure: {
      en: "sentence structure (run-ons, fragments)", ar: "بناء الجملة",
      tip: "الجمل الإنجليزية أقصر من العربية. لا تربط جملتين كاملتين بفاصلة فقط؛ استخدم نقطة أو and / but / so / because."
    },
    other: { en: "other", ar: "أخرى", tip: "" }
  };

  /** The same, for the English interface: label and tip. */
  const CATEGORIES_EN = {
    articles: ["Articles (a / an / the)", "Use a/an with a singular countable noun the first time you mention it, and the for something known or already mentioned. Don't put the before general nouns: Life is short, not The life is short."],
    prepositions: ["Prepositions (in / on / at …)", "Prepositions often don't translate word for word: in for months, years and cities; on for days and dates; at for times and exact places. Some verbs take no preposition: discuss the plan, not discuss about the plan."],
    verb_tense: ["Verb tenses", "Use the past simple for something finished at a known time (I finished yesterday), and the present perfect for something with an effect now or no set time (I have finished). Don't use the continuous with state verbs: I know, not I am knowing."],
    agreement: ["Subject–verb agreement", "With he / she / it or a singular noun, the present-tense verb takes s: She works, The team is. With plurals it doesn't: They work, The results are."],
    plurals: ["Singular and plural nouns", "Use the plural after numbers and words like many / several / one of the: three days, one of the best places. Some nouns have no plural: information, advice, equipment."],
    word_choice: ["Word choice", "Literal translations give words that are correct but unnatural. Learn common combinations as a whole: make a decision, not do a decision; take a photo, not make a photo."],
    spelling: ["Spelling", "Watch words that sound alike: their / there / they're, your / you're, its / it's."],
    punctuation: ["Punctuation and capitals", "Start sentences, names, days, months and I with a capital letter. Put a space after commas and full stops, not before them."],
    word_order: ["Word order", "Adjectives come before the noun (a big house), and indirect questions use statement order: Can you tell me where the station is, not where is the station."],
    sentence_structure: ["Sentence structure", "English sentences are shorter than Arabic ones. Don't join two full sentences with just a comma; use a full stop or and / but / so / because."],
    other: ["Other", ""]
  };

  const uiLang = () => (typeof LamhaI18n !== "undefined" ? LamhaI18n.lang() : "ar");
  /** A category's name in the interface language. */
  const catLabel = (c, lang = uiLang()) => (lang === "en" ? (CATEGORIES_EN[c] || CATEGORIES_EN.other)[0] : (CATEGORIES[c] || CATEGORIES.other).ar);
  /** A category's rule in the interface language ("" for other). */
  const catTip = (c, lang = uiLang()) => (lang === "en" ? (CATEGORIES_EN[c] || CATEGORIES_EN.other)[1] : (CATEGORIES[c] || CATEGORIES.other).tip);

  /** error code → [title, explanation, fixed in Settings?] */
  const ERRORS = {
    ai_no_key: ["فعّل أدوات الكتابة", "اختر من الإعدادات Ollama (مجاني على جهازك) أو Claude (بمفتاح API).", true],
    ai_bad_key: ["مفتاح Claude غير صالح", "تحقق من المفتاح في الإعدادات أو أنشئ مفتاحًا جديدًا.", true],
    ai_no_credit: ["نفد رصيد حساب Claude API", "أضف رصيدًا من console.anthropic.com ثم أعد المحاولة.", true],
    ai_forbidden: ["المفتاح لا يملك صلاحية الاستخدام", "تحقق من صلاحيات المفتاح في موقع الخدمة (Anthropic أو Google AI Studio).", true],
    ai_model: ["النموذج المختار غير متاح لحسابك", "اختر نموذجًا آخر من الإعدادات.", true],
    ai_rate_limited: ["طلبات كثيرة في وقت قصير", "انتظر قليلًا ثم أعد المحاولة."],
    ai_busy: ["خدمة الذكاء الاصطناعي مشغولة الآن", "أعد المحاولة بعد لحظات."],
    ai_timeout: ["استغرق الرد وقتًا طويلًا", "أعد المحاولة، أو استخدم نصًّا أقصر."],
    ai_too_long: ["النص طويل جدًا", "استخدم جزءًا أقصر من النص."],
    ai_refused: ["تعذّرت معالجة هذا النص", "لم يتمكن الذكاء الاصطناعي من العمل على هذا النص."],
    ollama_no_model: ["اختر نموذج Ollama", "اختر نموذجًا من الإعدادات لتفعيل أدوات الكتابة.", true],
    ollama_offline: ["Ollama لا يعمل", "شغّل تطبيق Ollama من قائمة ابدأ ثم أعد المحاولة.", true],
    ollama_origin: ["Ollama يرفض اتصال الإضافة", "أضف متغيّر البيئة OLLAMA_ORIGINS كما في خطوات الإعدادات، ثم أعد تشغيل Ollama.", true],
    ollama_model: ["النموذج غير موجود في Ollama", "حمّله أولًا (ollama pull …) أو اختر نموذجًا آخر من الإعدادات.", true],
    gemini_no_key: ["أضف مفتاح Gemini", "أنشئ مفتاحًا مجانيًا من aistudio.google.com وأضفه في الإعدادات.", true],
    gemini_bad_key: ["مفتاح Gemini غير صالح", "تحقق من المفتاح في الإعدادات أو أنشئ مفتاحًا جديدًا من aistudio.google.com.", true],
    gemini_quota: ["انتهى الحد المجاني من Gemini مؤقتًا", "للخطة المجانية حد للطلبات في الدقيقة وفي اليوم. انتظر قليلًا، أو جرّب نموذج Flash-Lite، أو استخدم Ollama."],
    gemini_busy: ["نماذج Gemini مزدحمة الآن", "خوادم Google مشغولة (يحدث كثيرًا مع النماذج الجديدة في الخطة المجانية). أعد المحاولة بعد دقيقة، أو استخدم Ollama."],
    gemini_region: ["Gemini غير متاح في بلدك", "خدمة Gemini API لا تعمل من موقعك الحالي. استخدم Ollama أو Claude.", true],
    network: ["لا يوجد اتصال بالإنترنت", "تحقق من اتصالك ثم حاول مجددًا."]
  };
  const ERRORS_EN = {
    ai_no_key: ["Turn on the writing tools", "In Settings, choose Ollama (free, on your PC) or Claude (with an API key).", true],
    ai_bad_key: ["The Claude key isn't valid", "Check the key in Settings, or create a new one.", true],
    ai_no_credit: ["The Claude API account is out of credit", "Add credit at console.anthropic.com, then try again.", true],
    ai_forbidden: ["The key isn't allowed to do this", "Check the key's permissions on the service's website (Anthropic or Google AI Studio).", true],
    ai_model: ["The chosen model isn't available to your account", "Choose another model in Settings.", true],
    ai_rate_limited: ["Too many requests in a short time", "Wait a little and try again."],
    ai_busy: ["The AI service is busy right now", "Try again in a moment."],
    ai_timeout: ["The reply took too long", "Try again, or use a shorter text."],
    ai_too_long: ["The text is too long", "Use a shorter part of the text."],
    ai_refused: ["This text couldn't be processed", "The AI couldn't work on this text."],
    ollama_no_model: ["Choose an Ollama model", "Choose a model in Settings to turn on the writing tools.", true],
    ollama_offline: ["Ollama isn't running", "Start Ollama from the Start menu, then try again.", true],
    ollama_origin: ["Ollama refuses the extension's connection", "Add the OLLAMA_ORIGINS environment variable as in the Settings steps, then restart Ollama.", true],
    ollama_model: ["The model isn't in Ollama", "Download it first (ollama pull …) or choose another model in Settings.", true],
    gemini_no_key: ["Add a Gemini key", "Create a free key at aistudio.google.com and add it in Settings.", true],
    gemini_bad_key: ["The Gemini key isn't valid", "Check the key in Settings, or create a new one at aistudio.google.com.", true],
    gemini_quota: ["Gemini's free limit is used up for now", "The free tier has per-minute and daily limits. Wait a little, try the Flash-Lite model, or use Ollama."],
    gemini_busy: ["Gemini's models are busy right now", "Google's servers are busy (common with new models on the free tier). Try again in a minute, or use Ollama."],
    gemini_region: ["Gemini isn't available in your country", "The Gemini API doesn't work from your location. Use Ollama or Claude.", true],
    network: ["No internet connection", "Check your connection and try again."]
  };

  function errorInfo(code, providerName = "Claude", lang = uiLang()) {
    code = String(code || "");
    const en = lang === "en";
    const table = en ? ERRORS_EN : ERRORS;
    if (table[code]) return table[code];
    if (code.startsWith("ai_error:")) return [(en ? "Couldn't reach " : "تعذّر الاتصال بـ ") + providerName, code.slice(9)];
    return en ? ["Something went wrong", "Try again."] : ["حدث خطأ", "أعد المحاولة."];
  }

  /** Word-level diff of `a` → `b` as [{ tag: "" | "del" | "ins", t }], adjacent pieces merged.
   *  Returns null when the texts are too long to compare quickly. */
  function diffParts(a, b) {
    const A = a.match(/\s+|[^\s]+/g) || [], B = b.match(/\s+|[^\s]+/g) || [];
    const n = A.length, m = B.length;
    if (n * m > 400000) return null;
    const dp = Array.from({ length: n + 1 }, () => new Uint16Array(m + 1));
    for (let i = n - 1; i >= 0; i--) {
      for (let j = m - 1; j >= 0; j--) dp[i][j] = A[i] === B[j] ? dp[i + 1][j + 1] + 1 : Math.max(dp[i + 1][j], dp[i][j + 1]);
    }
    const parts = [];
    const push = (tag, t) => {
      const last = parts[parts.length - 1];
      if (last && last.tag === tag) last.t += t; else parts.push({ tag, t });
    };
    let i = 0, j = 0;
    while (i < n && j < m) {
      if (A[i] === B[j]) { push("", A[i]); i++; j++; }
      else if (dp[i + 1][j] >= dp[i][j + 1]) push("del", A[i++]);
      else push("ins", B[j++]);
    }
    while (i < n) push("del", A[i++]);
    while (j < m) push("ins", B[j++]);
    return parts;
  }

  /** diffParts rendered with `h`: removed words struck through, new words highlighted. */
  function diffNodes(h, a, b) {
    const parts = diffParts(a, b);
    if (!parts) return [b];
    return parts.map(p => (p.tag && p.t.trim() ? h(p.tag, null, p.t) : p.tag === "del" ? "" : p.t));
  }

  const ARABIC_ALL = /[؀-ۿݐ-ݿࢠ-ࣿﭐ-﷿ﹰ-﻿]/g;
  const isArabicText = t => (t.match(ARABIC_ALL) || []).length > (t.match(/[A-Za-z]/g) || []).length;

  /** Writing-tools provider from storage.local values: which one, is it set up, its name, the "not set up" error. */
  function provider(local = {}) {
    const id = ["ollama", "gemini"].includes(local.aiProvider) ? local.aiProvider : "claude";
    return {
      id,
      name: { ollama: "Ollama", gemini: "Gemini", claude: "Claude" }[id],
      ready: id === "ollama" ? !!local.ollamaModel : id === "gemini" ? !!local.geminiKey : !!local.aiKey,
      notReady: { ollama: "ollama_no_model", gemini: "gemini_no_key", claude: "ai_no_key" }[id]
    };
  }
  const PROVIDER_KEYS = ["aiProvider", "aiKey", "ollamaModel", "geminiKey"];

  return { CATEGORIES, ERRORS, errorInfo, catLabel, catTip, diffParts, diffNodes, isArabicText, provider, PROVIDER_KEYS };
})();
