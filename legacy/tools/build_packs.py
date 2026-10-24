"""
Build Lamha's optional offline language packs: French–French, German–German, Spanish–Spanish and Turkish–Turkish
dictionaries, downloaded from Settings (packs.js) instead of shipped with everyone's copy of Lamha.

Source: the Wiktionary edition written in that language (fr.wiktionary.org for French…), as extracted by kaikki.org
(wiktextract). Wiktionary text is CC BY-SA 4.0: the pack's meta says so, and Settings credits it.
Vocabulary: the most common words by wordfreq (CC BY-SA 4.0), so a pack stays a few megabytes.

Usage:
  pip install wordfreq
  python tools/build_packs.py tr                 # streams the source from kaikki.org (tr: 44 MB … fr: 730 MB)
  python tools/build_packs.py fr --src fr-extract.jsonl.gz   # or a file downloaded before
  python tools/build_packs.py tr --max 30000

Output: dist-packs/<lang>.json.gz (git-ignored; uploaded to the GitHub release named in packs.js)
  { "meta": { lang, format, version, words, forms, built, source, license },
    "shards": { "<key>": { "w": { word: entry }, "f": { form: lemma } } } }
  entry: { "s": [[pos, gloss, example, [synonyms]], …], "p": ipa }   pos: local-dict.js letters (n, v, a, r, …)
  key: the first two letters of the word, lowercase, without accents (packShard() in packs.js does the same).
"""
import argparse, collections, datetime, gzip, io, json, os, re, sys, unicodedata, urllib.request
from wordfreq import zipf_frequency

FORMAT = 1
SOURCE = "https://kaikki.org/dictionary/downloads/{0}/{0}-extract.jsonl.gz"
LANGS = {"fr": "French", "de": "German", "es": "Spanish", "tr": "Turkish"}

MAX_WORDS = 40000     # headwords kept per pack, most common first
MIN_ZIPF = 2.0        # a headword must be at least this common (wordfreq's Zipf scale: 3 ≈ once per million words)
MIN_ZIPF_FORM = 2.5   # an inflected form ("maisons", "evler") is kept only when it's this common
MAX_SENSES = 4
MAX_FORMS = 30        # per headword
MAX_GLOSS = 240
MAX_EXAMPLE = 160

# wiktextract's part of speech → local-dict.js letters; names, affixes, symbols… are left out
POS = {"noun": "n", "verb": "v", "adj": "a", "adv": "r", "prep": "p", "postp": "p", "pron": "o", "conj": "c",
       "intj": "i", "det": "d", "article": "t", "num": "u", "phrase": "h", "prep_phrase": "h", "proverb": "h",
       "particle": "t", "contraction": "h"}
WORD_RE = re.compile(r"^[^\W\d_](?:[^\W\d_]|[' -](?=[^\W\d_]))*$")  # letters, with single spaces, hyphens or apostrophes inside


def shard_key(word):
    s = unicodedata.normalize("NFD", word.lower().replace("ı", "i").replace("ß", "ss"))
    s = "".join(c for c in s if "a" <= c <= "z")
    return (s[:2] + "__")[:2] if s else "__"


def clean(text, limit):
    text = re.sub(r"\s+", " ", str(text or "")).strip()
    return text if len(text) <= limit else text[:limit - 1].rsplit(" ", 1)[0] + "…"


def ipa_of(entry):
    for s in entry.get("sounds") or []:
        ipa = (s.get("ipa") or "").strip().strip("\\/[]").strip()
        if ipa:
            return ipa
    return ""


def synonyms_of(sense, entry, first):
    words = [x.get("word") for x in (sense.get("synonyms") or [])]
    if not words and first:  # some editions list them per word, not per sense: shown once, with the first sense
        words = [x.get("word") for x in (entry.get("synonyms") or [])]
    out = []
    for w in words:
        w = clean(w, 40)
        if w and w not in out and WORD_RE.match(w):
            out.append(w)
    return out[:4]


def senses_of(entry, pos):
    out = []
    for sense in entry.get("senses") or []:
        if sense.get("form_of") or sense.get("alt_of"):
            continue
        glosses = [g for g in (sense.get("glosses") or []) if g and g.strip()]
        if not glosses:
            continue
        gloss = clean(glosses[-1], MAX_GLOSS)  # the most specific one; the first is the general heading when nested
        example = ""
        for ex in sense.get("examples") or []:
            t = clean(ex.get("text"), MAX_EXAMPLE)
            if t and len(t) > 3:
                example = t
                break
        out.append([pos, gloss, example, synonyms_of(sense, entry, not out)])
    return out


def pick_senses(all_senses):
    """At most MAX_SENSES, every part of speech getting its first sense before any gets a second
    ("güzel": the adjective isn't crowded out by four noun senses), shown grouped by part of speech."""
    groups = collections.OrderedDict()
    for s in all_senses:
        groups.setdefault(s[0], []).append(s)
    chosen = []
    for i in range(MAX_SENSES):
        for g in groups.values():
            if i < len(g) and len(chosen) < MAX_SENSES:
                chosen.append(g[i])
    return [s for g in groups.values() for s in g if s in chosen]


def stream(src):
    if re.match(r"^https?://", src):
        print(f"• downloading {src} (streamed, not saved)")
        raw = urllib.request.urlopen(urllib.request.Request(src, headers={"User-Agent": "Lamha pack builder"}))
    else:
        raw = open(src, "rb")
    fh = gzip.GzipFile(fileobj=raw) if src.endswith(".gz") else raw
    return io.TextIOWrapper(fh, encoding="utf-8")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("lang", choices=sorted(LANGS))
    ap.add_argument("--src", help="a downloaded kaikki.org JSONL(.gz) file instead of streaming it")
    ap.add_argument("--max", type=int, default=MAX_WORDS)
    ap.add_argument("--out", default="dist-packs")
    args = ap.parse_args()
    lang = args.lang
    src = args.src or SOURCE.format(lang)

    zipf_cache = {}
    def zipf(w):
        if w not in zipf_cache:
            zipf_cache[w] = zipf_frequency(w, lang)
        return zipf_cache[w]

    senses = collections.defaultdict(list)   # word → [[pos, gloss, example, synonyms], …] in the source's order
    ipa = {}
    forms = collections.defaultdict(set)     # lemma → inflected forms
    form_of = []                              # (form, lemma) from the "plural of …" entries
    lines = 0
    for line in stream(src):
        lines += 1
        if lines % 200000 == 0:
            print(f"  {lines:,} lines, {len(senses):,} words so far", flush=True)
        if f'"lang_code": "{lang}"' not in line:  # cheap test before parsing: most lines are other languages
            continue
        try:
            e = json.loads(line)
        except ValueError:
            continue
        word = e.get("word") or ""
        if e.get("lang_code") != lang or not WORD_RE.match(word) or len(word) > 40:
            continue
        pos = POS.get(e.get("pos"))
        if not pos:
            continue
        for s in e.get("senses") or []:
            for f in s.get("form_of") or []:
                lemma = f.get("word")
                if lemma and lemma != word and zipf(word) >= MIN_ZIPF_FORM:
                    form_of.append((word, lemma))
        if zipf(word) < MIN_ZIPF:
            continue
        got = senses_of(e, pos)
        if got:
            senses[word].extend(got)
            if word not in ipa and ipa_of(e):
                ipa[word] = ipa_of(e)
        for f in e.get("forms") or []:
            form = f.get("form") or ""
            tags = f.get("tags") or []
            if form != word and WORD_RE.match(form) and " " not in form and "romanization" not in tags and "table-tags" not in tags:
                forms[word].add(form)

    ranked = sorted((w for w in senses if senses[w]), key=lambda w: -zipf(w))[:args.max]
    kept = set(ranked)
    shards = collections.defaultdict(lambda: {"w": {}, "f": {}})
    for w in ranked:
        entry = {"s": pick_senses(senses[w])}
        if ipa.get(w):
            entry["p"] = ipa[w]
        shards[shard_key(w)]["w"][w] = entry
    n_forms = 0
    lemma_forms = [(f, w) for w in ranked for f in forms[w]] + [(f, l) for f, l in form_of if l in kept]
    per_lemma = collections.Counter()
    for form, lemma in sorted(lemma_forms, key=lambda p: -zipf(p[0])):
        if form in kept or per_lemma[lemma] >= MAX_FORMS or zipf(form) < MIN_ZIPF_FORM:
            continue
        f = shards[shard_key(form)]["f"]
        if form not in f:
            f[form] = lemma
            per_lemma[lemma] += 1
            n_forms += 1

    meta = {
        "lang": lang, "format": FORMAT, "version": datetime.date.today().isoformat(), "words": len(ranked), "forms": n_forms,
        "built": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"),
        "source": f"{LANGS[lang]} Wiktionary via kaikki.org (wiktextract)", "license": "CC BY-SA 4.0"
    }
    os.makedirs(args.out, exist_ok=True)
    path = os.path.join(args.out, f"{lang}.json.gz")
    data = json.dumps({"meta": meta, "shards": shards}, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    with gzip.open(path, "wb", compresslevel=9) as out:
        out.write(data)
    print(f"✓ {path}: {len(ranked):,} words, {n_forms:,} forms, {len(shards)} shards, "
          f"{len(data) / 1e6:.1f} MB of JSON, {os.path.getsize(path) / 1e6:.1f} MB to download ({lines:,} source lines)")


if __name__ == "__main__":
    sys.exit(main())
