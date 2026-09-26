/* Lamha — Arabic-aware text normalization for search (clipboard history in the desktop app).
 * Browser-safe, no dependencies: loaded with <script>, or with vm in Node (desktop main process, tools/). */
// eslint-disable-next-line no-unused-vars
var LamhaArabic = (() => {
  "use strict";

  const MARKS = /[ً-ٰٟـ]/g; // tashkeel, superscript alef, tatweel
  const LETTERS = {
    "أ": "ا", "إ": "ا", "آ": "ا", "ٱ": "ا", // أ إ آ ٱ → ا
    "ى": "ي", // ى → ي
    "ة": "ه", // ة → ه
    "ؤ": "و", // ؤ → و
    "ئ": "ي" // ئ → ي
  };
  const LETTER_RE = /[أإآٱىةؤئ]/g;
  const DIGIT_RE = /[٠-٩۰-۹]/g; // Arabic-Indic ٠–٩, Persian ۰–۹

  /** A form of `str` where spelling variants compare equal: مَصْرِف → مصرف, أحمد → احمد, ٢٠٢٤ → 2024, Invoice → invoice. */
  function normalizeForSearch(str) {
    return String(str == null ? "" : str)
      .normalize("NFKC")
      .replace(MARKS, "")
      .replace(LETTER_RE, c => LETTERS[c])
      .replace(DIGIT_RE, c => String(c.charCodeAt(0) - (c >= "۰" ? 0x06F0 : 0x0660)))
      .toLowerCase()
      .replace(/\s+/g, " ")
      .trim();
  }

  /** One character's contribution to the normalized string (whitespace kept as a single space, not collapsed). */
  const normChar = ch => (/\s/.test(ch) ? " " : normalizeForSearch(ch));

  /**
   * Where the query's words occur in the original `text`, for highlighting: sorted, merged [start, end) ranges.
   * Matches like search does (مصرف highlights all of مَصْرِف, including its marks).
   */
  function matchRanges(text, query) {
    text = String(text == null ? "" : text);
    const tokens = normalizeForSearch(query).split(" ").filter(Boolean);
    if (!tokens.length) return [];
    let norm = "";
    const from = []; // normalized UTF-16 unit → index of the original character it came from
    for (let i = 0; i < text.length;) {
      const ch = String.fromCodePoint(text.codePointAt(i));
      const n = normChar(ch);
      norm += n;
      for (let k = 0; k < n.length; k++) from.push(i);
      i += ch.length;
    }
    from.push(text.length);
    const ranges = [];
    for (const t of tokens) {
      for (let s = norm.indexOf(t); s !== -1; s = norm.indexOf(t, s + 1)) ranges.push([from[s], from[s + t.length]]);
    }
    ranges.sort((a, b) => a[0] - b[0]);
    const merged = [];
    for (const r of ranges) {
      const last = merged[merged.length - 1];
      if (last && r[0] <= last[1]) last[1] = Math.max(last[1], r[1]); else merged.push(r.slice());
    }
    return merged;
  }

  /**
   * "ar" | "en" | "mixed" | "other" — from the share of Arabic letters (U+0621–U+064A) among Arabic + Latin letters
   * in the first 2,000 characters. Fewer than 2 letters (numbers, emoji, symbols) → "other".
   */
  function detectLang(text) {
    const head = String(text == null ? "" : text).slice(0, 2000);
    const a = (head.match(/[ء-ي]/g) || []).length;
    const l = (head.match(/[A-Za-z]/g) || []).length;
    if (a + l < 2) return "other";
    const share = a / (a + l);
    return share >= 0.8 ? "ar" : share <= 0.2 ? "en" : "mixed";
  }

  return { normalizeForSearch, matchRanges, detectLang };
})();
