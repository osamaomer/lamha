"""
Build Lamha's offline dictionary (dict/ folder) from open data.

Sources (all openly licensed; see dict/LICENSES.md):
  * Princeton WordNet 3.0 ............ English senses, glosses, examples, synonyms
  * Arabic WordNet v2 (via OMW 1.4) ... Arabic lemmas per WordNet sense (CC BY-SA 3.0)
  * English Wiktionary (kaikki.org) .. Arabic translations + inflected forms (CC BY-SA 4.0)
  * CMU Pronouncing Dictionary ....... pronunciations -> IPA (BSD-2)
  * wordfreq ......................... word frequencies to pick the vocabulary (CC BY-SA 4.0)

Usage:
  pip install nltk wordfreq
  python -c "import nltk; [nltk.download(p) for p in ('wordnet','omw-1.4','cmudict')]"
  curl -LO https://kaikki.org/dictionary/English/kaikki.org-dictionary-English.jsonl.gz
  python tools/build_dict.py --kaikki kaikki.org-dictionary-English.jsonl.gz

Output (inside the extension):
  dict/en/<shard>.json   English headwords -> entry
  dict/ar/<shard>.json   normalised Arabic word -> [English words]
  dict/forms.json        inflected form -> lemma(s)
  dict/meta.json         counts + build info
"""
import argparse, collections, gzip, json, os, re, sys, zipfile
from nltk.corpus import wordnet as wn
from nltk.corpus import cmudict
import nltk
from wordfreq import zipf_frequency

MAX_SENSES = 6          # WordNet senses kept per common word (rare words keep 3)
MAX_SENSE_GROUPS = 6    # Wiktionary translation senses kept per part of speech
MAX_AR = 4              # Arabic terms kept per sense
MIN_ZIPF = 1.5          # vocabulary threshold for words that have Arabic translations
MIN_ZIPF_DEFS = 3.0     # below this, definition-only words keep just 2 senses
MIN_ZIPF_ANY = 2.2      # words with English definitions only must be at least this common
RARE_ZIPF = 2.5         # below this: fewer senses, no examples

WORD_RE = re.compile(r"^[a-z][a-z' -]*[a-z]$|^[a-z]$")
AR_RE = re.compile(r"[؀-ۿ]")
DIACRITICS = re.compile(r"[ً-ٰٟـ]")

WN_POS = {"n": "n", "v": "v", "a": "a", "s": "a", "r": "r"}
WIKI_POS = {"noun": "n", "verb": "v", "adj": "a", "adv": "r", "prep": "p", "pron": "o", "conj": "c",
            "intj": "i", "det": "d", "num": "u", "phrase": "h", "name": "m", "article": "d",
            "particle": "t", "prep_phrase": "h", "contraction": "h"}


def norm_ar(s):
    s = DIACRITICS.sub("", s)
    s = re.sub("[إأآٱ]", "ا", s)
    s = s.replace("ى", "ي").replace("ة", "ه")
    return s.strip()


def clean_ar(s):
    s = re.sub(r"\(.*?\)|\[.*?\]", "", s).strip(" ،,;")
    return s if s and AR_RE.search(s) and len(s) <= 40 else None


def shard(word):
    k = re.sub(r"[^a-z]", "", word.lower())[:2]
    return (k + "_")[:2] if k else "__"


def shard_ar(nw):
    return "%04x" % ord(nw[0]) if nw else "0000"


# ---------------------------------------------------------------- ARPAbet -> IPA
ARPA = {"AA": "ɑ", "AE": "æ", "AH": "ʌ", "AO": "ɔ", "AW": "aʊ", "AY": "aɪ", "EH": "ɛ", "ER": "ɝ", "EY": "eɪ",
        "IH": "ɪ", "IY": "i", "OW": "oʊ", "OY": "ɔɪ", "UH": "ʊ", "UW": "u", "B": "b", "CH": "tʃ", "D": "d",
        "DH": "ð", "F": "f", "G": "ɡ", "HH": "h", "JH": "dʒ", "K": "k", "L": "l", "M": "m", "N": "n", "NG": "ŋ",
        "P": "p", "R": "r", "S": "s", "SH": "ʃ", "T": "t", "TH": "θ", "V": "v", "W": "w", "Y": "j", "Z": "z", "ZH": "ʒ"}


ONSETS = {"pl", "bl", "kl", "ɡl", "fl", "sl", "pr", "br", "tr", "dr", "kr", "ɡr", "fr", "θr", "ʃr", "kw", "tw", "dw",
          "sw", "ɡw", "sp", "st", "sk", "sm", "sn", "sf", "pj", "bj", "kj", "fj", "mj", "hj", "vj", "spl", "spr", "str",
          "skr", "skw", "spj", "skj"}


def to_ipa(phones):
    syms, vowel = [], []
    for ph in phones:
        base, stress = re.match(r"([A-Z]+)(\d?)", ph).groups()
        sym = ARPA.get(base, "")
        if stress == "0" and base == "AH": sym = "ə"
        if stress == "0" and base == "ER": sym = "ɚ"
        syms.append(sym); vowel.append(stress)
    multi = sum(1 for v in vowel if v) > 1
    marks = {}
    for i, v in enumerate(vowel):
        if not multi or v not in ("1", "2"): continue
        j = i
        while j > 0 and not vowel[j - 1]: j -= 1          # j = first consonant after previous vowel
        cluster = syms[j:i]
        if j == 0: start = 0                                # word-initial: whole onset
        elif not cluster: start = i
        else:
            start = i - 1                                   # at least one consonant starts the syllable
            for k in range(j, i - 1):
                if "".join(syms[k:i]) in ONSETS: start = k; break
        marks[start] = "ˈ" if v == "1" else "ˌ"
    return "".join(marks.get(i, "") + s for i, s in enumerate(syms))


# ---------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--kaikki", required=True)
    ap.add_argument("--omw", default=None, help="path to omw-1.4.zip (default: nltk data)")
    ap.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "..", "dict"))
    args = ap.parse_args()

    omw = args.omw or nltk.data.find("corpora/omw-1.4.zip").path
    print("• Arabic WordNet …")
    awn = collections.defaultdict(list)
    for line in zipfile.ZipFile(omw).read("omw-1.4/arb/wn-data-arb.tab").decode("utf8").splitlines():
        p = line.split("\t")
        if len(p) == 3 and p[1] == "arb:lemma":
            w = clean_ar(p[2])
            if w and norm_ar(w) not in {norm_ar(x) for x in awn[p[0]]}: awn[p[0]].append(w)

    print("• Wiktionary (streaming, this takes a few minutes) …")
    # word -> pos -> [[english sense hint, [arabic…]], …]
    wiki_tr = collections.defaultdict(lambda: collections.OrderedDict())
    forms = collections.defaultdict(list)                                  # form -> [lemma]
    n = 0
    with gzip.open(args.kaikki, "rt", encoding="utf8") as fh:
        for line in fh:
            n += 1
            if n % 200000 == 0: print("   ", n, "entries")
            e = json.loads(line)
            if e.get("lang_code") != "en": continue
            word = e.get("word", "").lower()
            if not WORD_RE.match(word) or len(word) > 40: continue
            pos = WIKI_POS.get(e.get("pos"))
            trs = list(e.get("translations") or [])
            for s in e.get("senses") or []:
                trs.extend(s.get("translations") or [])
                for f in s.get("form_of") or []:
                    lemma = (f.get("word") or "").lower()
                    if lemma and lemma != word and WORD_RE.match(lemma) and lemma not in forms[word]:
                        forms[word].append(lemma)
            if pos and any(t.get("lang_code") == "ar" or t.get("code") == "ar" for t in trs):
                groups = wiki_tr[word].setdefault(pos, [])
                seen = {norm_ar(a) for g in groups for a in g[1]}
                for t in trs:
                    if t.get("lang_code") != "ar" and t.get("code") != "ar": continue
                    w = clean_ar(t.get("word") or "")
                    if not w: continue
                    nw = norm_ar(w)
                    if nw in seen:
                        # prefer the vocalised spelling if we only had the bare one
                        for g in groups:
                            for i, a in enumerate(g[1]):
                                if a == nw and w != nw: g[1][i] = w
                        continue
                    sense = re.sub(r"\s+", " ", (t.get("sense") or "")).strip()
                    if len(sense) > 45: sense = sense[:44].rstrip(" ,;") + "…"
                    g = next((g for g in groups if g[0] == sense), None)
                    if not g:
                        if len(groups) >= MAX_SENSE_GROUPS: continue
                        g = [sense, []]; groups.append(g)
                    if len(g[1]) < MAX_AR:
                        g[1].append(w); seen.add(nw)
                if not groups: del wiki_tr[word][pos]
                if not wiki_tr[word]: del wiki_tr[word]
    print("   Wiktionary words with Arabic:", len(wiki_tr))

    print("• Vocabulary …")
    vocab = set()
    for name in wn.all_lemma_names():
        w = name.replace("_", " ").lower()
        if WORD_RE.match(w) and len(w.split()) <= 3 and zipf_frequency(w, "en") >= MIN_ZIPF:
            vocab.add(w)
    for w in wiki_tr:
        if len(w.split()) <= 3 and zipf_frequency(w, "en") >= MIN_ZIPF:
            vocab.add(w)
    print("   headwords:", len(vocab))

    print("• Pronunciations …")
    cmu = cmudict.dict()

    print("• Entries …")
    en_shards = collections.defaultdict(dict)
    ar_index = collections.defaultdict(lambda: collections.defaultdict(list))
    stats = collections.Counter()
    for w in sorted(vocab, key=lambda x: -zipf_frequency(x, "en")):
        z = zipf_frequency(w, "en")
        rare = z < RARE_ZIPF
        entry = {}
        senses = []
        own = [s for s in wn.synsets(w.replace(" ", "_")) if w in (l.replace("_", " ").lower() for l in s.lemma_names())]
        for s in own[:(2 if z < MIN_ZIPF_DEFS else 3 if rare else MAX_SENSES)]:
            key = "%08d-%s" % (s.offset(), "a" if s.pos() == "s" else s.pos())
            syn = [l.replace("_", " ") for l in s.lemma_names() if l.replace("_", " ").lower() != w][:4]
            ex = "" if rare else (s.examples() or [""])[0]
            senses.append([WN_POS[s.pos()], s.definition(), ex, syn, awn.get(key, [])[:4]])
        if senses: entry["s"] = senses
        if wiki_tr.get(w): entry["t"] = dict(wiki_tr[w])
        has_ar = bool(entry.get("t") or any(x[4] for x in senses))
        if not entry or (not has_ar and z < MIN_ZIPF_ANY): continue
        phones = cmu.get(w) if " " not in w else None
        if phones: entry["p"] = to_ipa(phones[0])
        en_shards[shard(w)][w] = entry
        stats["entries"] += 1
        stats["with_arabic"] += bool(entry.get("t") or any(s[4] for s in senses))
        # reverse index Arabic -> English (only words people are likely to select)
        if zipf_frequency(w, "en") >= 2.5 and " " not in w:
            ars = [a for gs in entry.get("t", {}).values() for g in gs for a in g[1]] + [a for s in senses for a in s[4]]
            for a in ars[:12]:
                nw = norm_ar(a)
                if nw and len(nw.split()) <= 2 and w not in ar_index[shard_ar(nw)][nw]:
                    ar_index[shard_ar(nw)][nw].append(w)

    # inflected forms (only those that are not headwords themselves or map to a known headword)
    all_words = set(k for sh in en_shards.values() for k in sh)
    form_map = {}
    for f, lemmas in forms.items():
        ls = [l for l in lemmas if l in all_words and l != f][:3]
        if ls and zipf_frequency(f, "en") >= 2.0:
            form_map[f] = ls
    # WordNet exception lists (irregular forms)
    for pos in "nvar":
        for f, lemmas in wn._exception_map[pos].items():
            f = f.replace("_", " ")
            ls = [l.replace("_", " ") for l in lemmas if l.replace("_", " ") in all_words]
            if ls and f not in all_words:
                form_map.setdefault(f, [])
                form_map[f] += [l for l in ls if l not in form_map[f]]

    print("• Writing …")
    out = os.path.abspath(args.out)
    for sub in ("en", "ar"):
        d = os.path.join(out, sub)
        os.makedirs(d, exist_ok=True)
        for fn in os.listdir(d): os.remove(os.path.join(d, fn))
    dump = lambda path, obj: json.dump(obj, open(path, "w", encoding="utf8"), ensure_ascii=False, separators=(",", ":"))
    for k, v in en_shards.items(): dump(os.path.join(out, "en", k + ".json"), v)
    for k, v in ar_index.items(): dump(os.path.join(out, "ar", k + ".json"), v)
    dump(os.path.join(out, "forms.json"), form_map)
    meta = {"entries": stats["entries"], "withArabic": stats["with_arabic"], "forms": len(form_map),
            "arabicIndex": sum(len(v) for v in ar_index.values()),
            "sources": ["WordNet 3.0", "Arabic WordNet v2 (OMW 1.4)", "English Wiktionary via kaikki.org", "CMUdict"]}
    dump(os.path.join(out, "meta.json"), meta)
    total = sum(os.path.getsize(os.path.join(dp, f)) for dp, _, fs in os.walk(out) for f in fs)
    print("   ", meta, "size: %.1f MB" % (total / 1e6))


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
