/* Lamha — offline dictionary.
 * Data lives in dict/ (built by tools/build_dict.py) and is split into small
 * shards by the first two letters, so a lookup only loads a few KB.
 *
 *   dict/en/<xx>.json  { word: { p: ipa, s: [[pos, gloss, example, [synonyms], [arabic]]…],
 *                                t: { pos: [[senseHint, [arabic…]]…] } } }
 *   dict/ar/<xxxx>.json { normalisedArabic: [english…] }
 *   dict/forms.json     { inflectedForm: [lemma…] }
 */
"use strict";

// eslint-disable-next-line no-unused-vars
const LocalDict = (() => {
  const POS_AR = {
    n: "اسم", v: "فعل", a: "صفة", r: "ظرف", p: "حرف جر", o: "ضمير", c: "حرف عطف",
    i: "تعجّب", d: "أداة", u: "عدد", h: "عبارة", m: "اسم علم", t: "أداة",
    e: "بالإنجليزية" // Arabic → English results: the list of English words
  };
  const POS_EN = {
    n: "noun", v: "verb", a: "adjective", r: "adverb", p: "preposition", o: "pronoun", c: "conjunction",
    i: "interjection", d: "determiner", u: "number", h: "phrase", m: "proper noun", t: "article",
    e: "in English"
  };
  /** Part-of-speech label in the interface language (shared/i18n.js, when loaded). */
  const posName = p => ((typeof LamhaI18n !== "undefined" && LamhaI18n.lang() === "en" ? POS_EN : POS_AR)[p] || p);
  const cache = new Map(); // path → Promise<object>

  function load(path) {
    if (!cache.has(path)) {
      cache.set(path, fetch(browser.runtime.getURL("dict/" + path))
        .then(r => (r.ok ? r.json() : {}))
        .catch(() => ({})));
    }
    return cache.get(path);
  }

  const shardEn = w => {
    const k = w.toLowerCase().replace(/[^a-z]/g, "").slice(0, 2);
    return k ? (k + "_").slice(0, 2) : "__";
  };
  const normAr = s => s
    .replace(/[ً-ٰٟـ]/g, "")
    .replace(/[إأآٱ]/g, "ا").replace(/ى/g, "ي").replace(/ة/g, "ه")
    .replace(/\s+/g, " ").trim();
  const shardAr = nw => (nw ? nw.codePointAt(0).toString(16).padStart(4, "0") : "0000");

  /* ---- context scoring: which sense fits the sentence the word came from ---- */
  const STOP = new Set(("the a an and or but of to in on at for with by from as is are was were be been being it its this that " +
    "these those his her their our your my we you they he she not no so than then there here what which who whom whose when " +
    "where why how all any some more most other such only own same too very can will just should now into over under again " +
    "once about against between through during before after above below up down out off also may might must would could has " +
    "have had do does did get got one").split(" "));
  const stem = w => w.replace(/ies$/, "y").replace(/(ing|ed|es|s|ly)$/, "");
  const tokens = s => new Set(((s || "").toLowerCase().match(/[a-z]{3,}/g) || []).filter(w => !STOP.has(w)).map(stem));
  const TECH_CTX = /\b(software|computers?|apps?|programs?|code|coding|data|files?|models?|prompts?|settings?|users?|browsers?|systems?|devices?|servers?|click|options?|config\w*|install\w*|api|web|websites?|online|digital|network|database|algorithms?|ai|llm|keyboard|screen|menu|download\w*)\b/i;
  const TECH_SENSE = /(comput|software|program|option|setting|automatic|electronic|device|data|file|internet|web|digital|machine|selected)/i;

  function scorer(context, word) {
    if (!context) return null;
    const own = tokens(word);
    const ctx = tokens(`${context.before} ${context.after}`);
    own.forEach(w => ctx.delete(w));
    const tech = TECH_CTX.test(`${context.before} ${context.after}`);
    if (!ctx.size && !tech) return null;
    return text => {
      let s = 0;
      for (const w of tokens(text)) if (ctx.has(w)) s += 1;
      if (tech && TECH_SENSE.test(text)) s += 1.5;
      return s;
    };
  }

  async function entry(word) {
    const shard = await load(`en/${shardEn(word)}.json`);
    return Object.prototype.hasOwnProperty.call(shard, word) ? shard[word] : null;
  }

  // WordNet "morphy"-style suffix rules + a few extras (doubling, -ly, -ied)
  const RULES = [
    ["ies", "y"], ["ied", "y"], ["ier", "y"], ["iest", "y"], ["ily", "y"],
    ["ches", "ch"], ["shes", "sh"], ["sses", "ss"], ["xes", "x"], ["zes", "z"], ["ses", "s"], ["men", "man"],
    ["ing", ""], ["ing", "e"], ["ed", ""], ["ed", "e"], ["es", ""], ["es", "e"], ["s", ""],
    ["est", ""], ["est", "e"], ["er", ""], ["er", "e"], ["ly", ""], ["'s", ""], ["s'", "s"]
  ];

  /** Returns { lemma, e, formOf? } or null. */
  async function find(raw) {
    const w = raw.toLowerCase().replace(/[’`]/g, "'").replace(/\s+/g, " ").trim();
    const forms = await load("forms.json");
    const lemmas = (forms[w] || []).filter(l => l !== w);
    let e = await entry(w);
    if (e) {
      // "studied" (adj.) is rarely what a reader wants when "study" exists and has Arabic meanings
      if (!e.t && lemmas.length) {
        const le = await entry(lemmas[0]);
        if (le && le.t) return { lemma: lemmas[0], e: le };
      }
      return { lemma: w, e, formOf: lemmas[0] || "" };
    }

    for (const l of lemmas) {
      e = await entry(l);
      if (e) return { lemma: l, e };
    }

    const tried = new Set([w]);
    for (const [suf, rep] of RULES) {
      if (!w.endsWith(suf) || w.length <= suf.length + 1) continue;
      const base = w.slice(0, -suf.length) + rep;
      const cands = [base];
      if (!rep && /([bdfgklmnprtvz])\1$/.test(base)) cands.push(base.slice(0, -1)); // running → run
      for (const c of cands) {
        if (tried.has(c)) continue;
        tried.add(c);
        e = await entry(c);
        if (e) return { lemma: c, e };
      }
    }
    return null;
  }

  function toResult(query, lemma, e, formOf, context) {
    const score = scorer(context, lemma);
    const dict = [];
    const groupFor = pos => {
      let g = dict.find(d => d.pos === pos);
      if (!g) { g = { pos, terms: [], seen: new Set() }; dict.push(g); }
      return g;
    };
    // Wiktionary translations, grouped by sense
    for (const [p, groups] of Object.entries(e.t || {})) {
      const g = groupFor(posName(p));
      for (const [hint, ars] of groups) for (const a of ars) {
        const k = normAr(a);
        if (!g.seen.has(k)) { g.seen.add(k); g.terms.push({ word: a, back: [], hint }); }
      }
    }
    // Arabic WordNet lemmas per sense
    for (const [p, gloss, , , ars] of e.s || []) {
      if (!ars || !ars.length) continue;
      const g = groupFor(posName(p));
      for (const a of ars) {
        const k = normAr(a);
        if (!g.seen.has(k)) { g.seen.add(k); g.terms.push({ word: a, back: [], hint: gloss.length > 60 ? gloss.slice(0, 57) + "…" : gloss }); }
      }
    }
    dict.forEach(g => delete g.seen);

    // best sense for this sentence (Wiktionary sense hints and WordNet definitions compete)
    let best = null;
    if (score) {
      for (const g of dict) for (const t of g.terms) {
        const sc = score(t.hint || "");
        t.score = sc;
        if (sc > 0 && (!best || sc > best.score)) best = { score: sc, ar: t.word, hint: t.hint, pos: g.pos };
      }
      for (const [p, gloss, example, synonyms, ar] of e.s || []) {
        const sc = score(`${gloss} ${example} ${(synonyms || []).join(" ")}`);
        if (sc > 0 && (!best || sc > best.score)) best = { score: sc, ar: ar && ar[0], gloss, pos: posName(p) };
      }
      if (best) {
        for (const g of dict) {
          g.terms.sort((x, y) => (y.score || 0) - (x.score || 0));
          g.terms.forEach(t => { t.best = !!best.hint && t.hint === best.hint; });
        }
        dict.sort((x, y) => (y.terms[0].score || 0) - (x.terms[0].score || 0));
      }
    }

    const definitions = [];
    for (const [p, gloss, example, synonyms, ar] of e.s || []) {
      const pos = posName(p);
      let d = definitions.find(x => x.pos === pos);
      if (!d) { d = { pos, entries: [] }; definitions.push(d); }
      if (d.entries.length < 5) d.entries.push({ gloss, example, synonyms, ar, best: !!(best && best.gloss === gloss) });
    }
    if (best && best.gloss) {
      for (const d of definitions) d.entries.sort((x, y) => y.best - x.best);
      definitions.sort((x, y) => (y.entries[0].best ? 1 : 0) - (x.entries[0].best ? 1 : 0));
    }

    // main translation:
    //  1. the sense that fits the sentence (if it has an Arabic word; otherwise say so honestly)
    //  2. Wiktionary's first translation (it lists the most common use first)
    //  3. Arabic WordNet only for the first two WordNet senses — a rare sense is never shown as "the" meaning
    let main = "", bestGloss = "";
    if (best && !best.ar && best.gloss) {
      // WordNet sense without Arabic: borrow the Wiktionary sense whose label matches its definition
      const gt = tokens(best.gloss);
      let pick = null, top = 0;
      for (const g of dict) for (const t of g.terms) {
        let o = 0;
        for (const w of tokens(t.hint || "")) if (gt.has(w)) o++;
        if (o > top) { top = o; pick = t; }
      }
      if (pick) {
        best.ar = pick.word;
        for (const g of dict) g.terms.forEach(t => { t.best = t.hint === pick.hint; });
        for (const g of dict) g.terms.sort((x, y) => y.best - x.best);
        dict.sort((x, y) => y.terms[0].best - x.terms[0].best);
      }
    }
    if (best) {
      if (best.ar) main = best.ar;
      else bestGloss = best.gloss || best.hint || "";
    } else if (e.t) {
      const first = Object.values(e.t)[0];
      main = first && first[0] && first[0][1][0] || "";
    } else {
      const s = (e.s || []).slice(0, 2).find(x => x[4] && x[4].length);
      main = s ? s[4][0] : "";
    }

    return {
      query: lemma, inflected: lemma !== query ? query : "", formOf: formOf || "",
      type: "word", src: "en", tl: "ar",
      translation: main, bestGloss, contextSense: !!best,
      translit: "", srcTranslit: e.p || "", spell: "",
      dict: dict.filter(g => g.terms.length), definitions, examples: [],
      source: "local"
    };
  }

  /** English word/short phrase → result, or null when not in the dictionary. */
  async function lookupEn(text, context) {
    const hit = await find(text);
    return hit ? toResult(text.toLowerCase().trim(), hit.lemma, hit.e, hit.formOf, context) : null;
  }

  /**
   * English word → an English–English dictionary result, or null when the word has no English definition here.
   * The main meaning (`translation`) is the definition that fits the sentence (`contextSense`), else the first one;
   * `ar` keeps the Arabic meaning for the review card, which shows both.
   */
  async function lookupEnglish(text, context) {
    const hit = await find(text);
    if (!hit) return null;
    const r = toResult(text.toLowerCase().trim(), hit.lemma, hit.e, hit.formOf, context);
    const entries = r.definitions.flatMap(d => d.entries);
    if (!entries.length) return null;
    const top = entries.find(e => e.best) || entries[0];
    return {
      ...r, mode: "en", tl: "en", dict: [], bestGloss: "",
      translation: top.gloss, heroExample: top.example || "", contextSense: !!top.best,
      ar: r.translation || (context ? toResult(r.query, hit.lemma, hit.e, hit.formOf, null).translation : ""), // the sense's Arabic, else the word's usual one
      definitions: r.definitions.map(d => ({ ...d, entries: d.entries.map(e => ({ ...e, ar: undefined })) })) // no Arabic in this view
    };
  }

  /** Arabic word → English words, or null. */
  async function lookupAr(text) {
    const base = normAr(text);
    const cands = [base];
    if (base.startsWith("ال")) cands.push(base.slice(2));
    if (/^[وفب]ال/.test(base)) cands.push(base.slice(3));
    if (/^[وف]/.test(base)) cands.push(base.slice(1));
    for (const c of cands) {
      if (!c) continue;
      const shard = await load(`ar/${shardAr(c)}.json`);
      const en = shard[c];
      if (en && en.length) {
        return {
          query: text.trim(), inflected: "", type: "word", src: "ar", tl: "en",
          translation: en[0], translit: "", srcTranslit: "", spell: "",
          dict: [{ pos: posName("e"), terms: en.slice(0, 12).map(w => ({ word: w, back: [] })) }],
          definitions: [], examples: [], source: "local"
        };
      }
    }
    return null;
  }

  const meta = () => load("meta.json");

  return { lookupEn, lookupEnglish, lookupAr, meta };
})();
