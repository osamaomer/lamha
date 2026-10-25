// The inputs: real words and sentences, each under the settings and network of the part of Lamha it exercises.
// A case: { id, group, text, context?, settings, net, offline?, forceAi? }. `net` is one of network.mjs's NETS.

const EN_WORDS = [
  "bank", "light", "run", "set", "book", "fair", "spring", "bear", "match", "date", "watch", "right", "patient", "present",
  "rock", "seal", "bat", "pitch", "crane", "bark", "address", "object", "content", "minute", "close", "lead", "wind",
  "tear", "bow", "row", "house", "water", "beautiful", "quickly", "although", "between", "serendipity", "ubiquitous",
  "ephemeral", "resilience", "mitigate", "nuance", "leverage", "deadline", "awkward", "gorgeous", "thorough", "whereas",
  "hence", "albeit", "furthermore", "the", "a", "I", "it's", "don't", "well-being", "e-mail", "mother-in-law", "okay"
];
const FORMS = [
  "went", "children", "running", "better", "mice", "studies", "was", "gone", "taught", "leaves", "geese", "wolves",
  "happier", "best", "swimming", "made", "analyses", "criteria", "bought", "lying"
];
const PHRASES = [
  "look up", "give up", "piece of cake", "break the ice", "by the way", "in spite of", "as well as", "take off",
  "run out", "carry on", "point of view", "ice cream", "high school", "New York", "on the other hand"
];
const AR_WORDS = [
  "كتاب", "مدرسة", "بيت", "سلام", "يكتب", "كتب", "الكتاب", "والكتاب", "فالبيت", "بالقلم", "جميل", "سريع", "حب", "ماء",
  "شمس", "قمر", "عربية", "مستشفى", "حاسوب", "طالب", "معلم", "تعلم", "استقلال", "مسؤولية", "إن"
];
const AR_PHRASES = ["السلام عليكم", "صباح الخير", "شكرا جزيلا", "إن شاء الله", "كرة القدم", "الأمم المتحدة", "يوم الجمعة", "بيت كبير"];
const ODD = [
  "Gemini", "Paris", "recieve", "yeet", "COVID-19", "iPhone", "bonjour", "gracias", "Straße", "42", "x", "qwzxv"
];
const EN_SENTENCES = [
  "It's a piece of cake, honestly.",
  "The meeting has been postponed until further notice.",
  "Could you please send me the report by Friday?",
  "She sells seashells by the seashore.",
  "I'm not sure whether he'll come or not.",
  "The quick brown fox jumps over the lazy dog.",
  "We need to mitigate the risks before launching.",
  "Break a leg tonight!",
  "He's been under the weather lately.",
  "The results were inconclusive, so further research is needed.",
  "Please don't hesitate to reach out if you have any questions.",
  "This product is out of stock.",
  "Where is the nearest train station?",
  "I've been learning English for three years.",
  "Climate change affects everyone.",
  "Our new app helps Arabic speakers read English.",
  "Can't wait to see you!",
  "The bank of the river was covered in flowers.",
  "To be or not to be, that is the question.",
  "Thanks a lot for your help.",
  "Click the button below to continue.",
  "Your password must contain at least 8 characters.",
  "Error: file not found.",
  "Hello world",
  "What time is it?",
  "Version 2.0.1 was released on October 4, 2026.",
  "Visit https://example.com for more info.",
  "I love it 😍",
  "Apples, oranges, and bananas are fruits.",
  "Let's call it a day."
];
const AR_SENTENCES = [
  "مرحبا، كيف حالك؟",
  "أريد أن أتعلم اللغة الإنجليزية.",
  "الطقس جميل اليوم.",
  "هل يمكنك مساعدتي من فضلك؟",
  "ذهبت إلى السوق أمس واشتريت بعض الخضروات.",
  "هذا الكتاب مفيد جدا للطلاب.",
  "أين أقرب مستشفى؟",
  "شكرا لك على وقتك.",
  "العلم نور والجهل ظلام.",
  "سأتصل بك غدا إن شاء الله.",
  "يعمل في شركة Google منذ عامين.",
  "قرأت مقالة عن الذكاء الاصطناعي."
];
const PARAGRAPHS = [
  "Hello   there.\r\n\n\n\nGood morning.  ",
  "First paragraph here.\n\nSecond paragraph, a bit longer than the first.\nA new line in it.",
  "مرحبا.\n\nكيف حالك؟"
];
const LONG = [
  Array.from({ length: 6 }, (_, i) => `Paragraph ${i + 1}. ` + "Reading in a second language takes patience and practice. ".repeat(5).trim()).join("\n\n"), // > 1,500: POST
  Array.from({ length: 12 }, (_, i) => `Section ${i + 1}. ` + "Some words here, and a few more to make it long. ".repeat(9).trim()).join("\n\n") // > 4,500: in pieces
];
const CONTEXT = [
  ["bank", "We sat on the ", " of the river and watched the boats."],
  ["bank", "I need to go to the ", " to deposit this check."],
  ["light", "The box was ", " enough to carry with one hand."],
  ["light", "Turn off the ", " before you leave."],
  ["fair", "That's not ", "; you got more than me."],
  ["fair", "We went to the county ", " last weekend."],
  ["spring", "The flowers bloom in ", "."],
  ["spring", "A broken ", " in the mattress kept me awake."],
  ["bear", "I can't ", " the noise any longer."],
  ["match", "Light a ", " and hold it to the candle."],
  ["match", "The ", " ended in a draw."],
  ["date", "What's today's ", "?"],
  ["date", "He asked her out on a ", "."],
  ["watch", "I forgot my ", " at home, so I don't know the time."],
  ["present", "She gave me a lovely ", " for my birthday."],
  ["patient", "Be ", "; the results will come."],
  ["rock", "They listen to ", " music all day."],
  ["leaves", "The ", " turn red in autumn."],
  ["leaves", "She ", " for work at eight."],
  ["Gemini", "I asked ", " to summarise the article."],
  ["Paris", "We flew to ", " last summer."],
  ["minute", "Wait a ", ", please."],
  ["object", "I ", " to the proposal."],
  ["went", "She ", " home early."],
  ["mitigate", "We must ", " the damage."]
];
const FRENCH = ["maison", "livre", "voiture", "heureux", "manger", "toujours"];
const AR_EXPLAIN = ["كتاب", "مدرسة", "جميل", "استقلال", "سريع", "قمر"];

const BASE = {
  uiLang: "ar", targetLang: "ar", dictSource: "local", useContext: true, translateDefinitions: true, enDict: false,
  explainLangs: [], showWikipedia: false, saveHistory: true, trService: "auto", aiProvider: "gemini", ollamaModel: ""
};
const OLLAMA = { aiProvider: "ollama", ollamaModel: "qwen3.5:4b" };

/** Every other item of `list`, then the first `n`: a spread-out subset. */
const some = (list, n) => list.filter((_, i) => i % 2 === 0).concat(list.filter((_, i) => i % 2 === 1)).slice(0, n);

export function allCases() {
  const cases = [];
  const add = (group, texts, { settings = {}, net = "up", offline = false, forceAi = false } = {}) => {
    for (const t of texts) {
      const [text, context] = Array.isArray(t) ? [t[0], { before: t[1], after: t[2] }] : [t, null];
      cases.push({ id: `${group} #${cases.filter(c => c.group === group).length + 1}`, group, text, ...(context ? { context } : {}),
        settings: { ...BASE, ...settings }, net, ...(offline ? { offline } : {}), ...(forceAi ? { forceAi } : {}) });
    }
  };
  const online = { dictSource: "online" };
  const localOnly = { dictSource: "offline" };
  const sentences = [...EN_SENTENCES, ...AR_SENTENCES];

  // The dictionary, Local first (the default)
  add("Local first: English words", EN_WORDS);
  add("Local first: inflected forms", FORMS);
  add("Local first: phrases", PHRASES);
  add("Local first: Arabic", [...AR_WORDS, ...AR_PHRASES]);
  add("Local first: not in the dictionary", ODD);
  // Online first: Google, the dictionary when Google can't answer
  add("Online first: words", [...some(EN_WORDS, 25), ...some(FORMS, 8), ...some(PHRASES, 5), ...some(AR_WORDS, 10), ...some(ODD, 6)], { settings: online });
  // Local only: never online for words; only a local AI may translate
  const offlineSet = [...some(EN_WORDS, 10), ...some(FORMS, 4), ...some(AR_WORDS, 5), ...some(ODD, 5), ...some(sentences, 4)];
  add("Local only, Gemini set up", offlineSet, { settings: localOnly });
  add("Local only, Ollama", some(offlineSet, 12), { settings: { ...localOnly, ...OLLAMA } });
  // Text
  add("Sentences", sentences);
  add("Paragraphs and long text", [...PARAGRAPHS, ...LONG]);
  // A word in its sentence
  add("Words in sentences, Local first", CONTEXT);
  add("Words in sentences, Online first", some(CONTEXT, 10), { settings: online });
  add("Words in sentences, context off", some(CONTEXT, 4), { settings: { useContext: false } });
  // The translation service: Google ↔ the AI
  const words = ["bank", "serendipity", "Gemini", "recieve", "كتاب", "piece of cake", "yeet", "went"];
  const fewSentences = some(sentences, 6);
  add("Google rate-limited, Automatic", [...words, ...fewSentences], { net: "rate" });
  add("Google rate-limited, Online first", some(words, 5), { net: "rate", settings: online });
  add("Google rate-limited, Google only", [...some(words, 4), ...some(fewSentences, 4)], { net: "rate", settings: { trService: "google" } });
  add("Google rate-limited, no AI set up", [...some(words, 3), ...some(fewSentences, 3)], { net: "rate", settings: { aiProvider: null } });
  add("AI first", [...some(words, 6), ...fewSentences], { settings: { trService: "ai" } });
  add("AI first, Gemini quota used up", some(fewSentences, 4), { net: "quota", settings: { trService: "ai" } });
  add("Both rate-limited", [...some(words, 3), ...some(fewSentences, 3)], { net: "rateQuota" });
  add("Offline, Gemini", [...some(words, 6), ...some(fewSentences, 3)], { net: "offline", offline: true });
  add("Offline, Ollama", [...some(words, 4), ...some(fewSentences, 3)], { net: "offline", offline: true, settings: OLLAMA });
  add("Network answers nothing, Online first", some(words, 4), { net: "dead", settings: online });
  add("Network answers nothing, Local first", [...some(fewSentences, 2), ...some(CONTEXT, 2)], { net: "dead" });
  add("AI with stray letters once", some(fewSentences, 3), { net: "stray" });
  add("AI with stray letters always", [...some(fewSentences, 3), ...some(words, 2)], { net: "strayAlways" });
  add("Better translation (the AI, asked)", [...some(words, 4), ...some(fewSentences, 3), CONTEXT[0]], { forceAi: true });
  // Explained in their own language
  add("English–English, Local first", some(EN_WORDS, 12), { settings: { enDict: true } });
  add("English–English, Online first", [...some(EN_WORDS, 6), ...some(ODD, 3)], { settings: { enDict: true, ...online } });
  add("English–English in a sentence", some(CONTEXT, 4), { settings: { enDict: true } });
  add("Translating into English", [...some(EN_WORDS, 5), ...some(AR_WORDS, 3), ...some(fewSentences, 2)], { settings: { targetLang: "en" } });
  add("French explained in French", FRENCH, { settings: { explainLangs: ["fr"] } });
  add("Arabic explained in Arabic", AR_EXPLAIN, { settings: { explainLangs: ["ar"] } });
  // The English interface (Google's labels and the dictionary's parts of speech in English)
  add("English interface", [...some(EN_WORDS, 8), ...some(AR_WORDS, 3), ...some(sentences, 2)], { settings: { uiLang: "en" } });
  add("English interface, Online first", some(EN_WORDS, 6), { settings: { uiLang: "en", ...online } });
  return cases;
}
