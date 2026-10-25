// A tiny .zim writer for the tests (desktop/zim.js reads what Kiwix publishes; this writes the same layout, version 6.3):
// header, MIME list, path pointers, entries, cluster pointers, one Zstandard cluster for text and one stored cluster
// for pictures, the front-article title list (X/listing/titleOrdered/v1), and the MD5 checksum at the end.
//   writeZim(file, { name: "wikipedia_ar_top", lang: "ara", flavour: "mini", date: "2026-07-10", entries: [...] })
//   entries: { path, title?, html? | data?, mime?, redirect?: "Target path" }  (content namespace "C")
import { writeFileSync } from "node:fs";
import { createHash, randomBytes } from "node:crypto";
import zlib from "node:zlib";

export const hasZstd = typeof zlib.zstdCompressSync === "function";

const u16 = n => { const b = Buffer.alloc(2); b.writeUInt16LE(n); return b; };
const u32 = n => { const b = Buffer.alloc(4); b.writeUInt32LE(n >>> 0); return b; };
const u64 = n => { const b = Buffer.alloc(8); b.writeBigUInt64LE(BigInt(n)); return b; };
const cstr = s => Buffer.concat([Buffer.from(s, "utf8"), Buffer.from([0])]);

function blobs(list) {
  const head = 4 * (list.length + 1);
  const offs = [head];
  for (const b of list) offs.push(offs[offs.length - 1] + b.length);
  return Buffer.concat([...offs.map(u32), ...list]);
}

export function writeZim(file, { name = "wikipedia_en_test", lang = "eng", flavour = "mini", date = "2026-07-10", title = "Test Wikipedia", entries = [], main = "" } = {}) {
  const meta = { Name: name, Language: lang, Flavour: flavour, Date: date, Title: title };
  const all = [
    ...entries.map(e => ({ ns: "C", ...e, mime: e.redirect ? "" : e.mime || (e.data ? "image/webp" : "text/html") })),
    ...Object.entries(meta).map(([k, v]) => ({ ns: "M", path: k, data: Buffer.from(v, "utf8"), mime: "text/plain" }))
  ];
  if (main) all.push({ ns: "W", path: "mainPage", redirect: main, redirectNs: "C" });
  all.push({ ns: "X", path: "listing/titleOrdered/v1", mime: "application/octet-stream+zimlisting", data: Buffer.alloc(0), listing: true });
  const key = e => Buffer.from(e.ns + e.path, "utf8");
  all.sort((a, b) => Buffer.compare(key(a), key(b)));
  all.forEach((e, i) => { e.index = i; });
  const byPath = (ns, p) => all.find(e => e.ns === ns && e.path === p);

  // the title list: every "C" article and redirect, by title
  const front = all.filter(e => e.ns === "C" && (e.redirect || e.mime === "text/html"));
  front.sort((a, b) => Buffer.compare(Buffer.from(a.title || a.path), Buffer.from(b.title || b.path)));
  all.find(e => e.listing).data = Buffer.concat(front.map(e => u32(e.index)));

  const mimes = [...new Set(all.filter(e => !e.redirect).map(e => e.mime))];
  const text = [], pics = [];
  for (const e of all) {
    if (e.redirect) continue;
    const data = e.html !== undefined ? Buffer.from(e.html, "utf8") : e.data;
    if (e.mime.startsWith("image/")) { e.cluster = 1; e.blob = pics.push(data) - 1; }
    else { e.cluster = 0; e.blob = text.push(data) - 1; }
  }
  const clusters = [
    Buffer.concat([Buffer.from([5]), zlib.zstdCompressSync(blobs(text))]),
    Buffer.concat([Buffer.from([1]), blobs(pics.length ? pics : [Buffer.alloc(0)])])
  ];

  const dirents = all.map(e => {
    const title = e.title && e.title !== e.path ? e.title : "";
    if (e.redirect) {
      const target = byPath(e.redirectNs || "C", e.redirect);
      if (!target) throw new Error("no redirect target " + e.redirect);
      return Buffer.concat([u16(0xffff), Buffer.from([0]), Buffer.from(e.ns), u32(0), u32(target.index), cstr(e.path), cstr(title)]);
    }
    return Buffer.concat([u16(mimes.indexOf(e.mime)), Buffer.from([0]), Buffer.from(e.ns), u32(0), u32(e.cluster), u32(e.blob), cstr(e.path), cstr(title)]);
  });

  const mimeList = Buffer.concat([...mimes.map(cstr), Buffer.from([0])]);
  const mimeListPos = 80;
  const pathPtrPos = mimeListPos + mimeList.length;
  let at = pathPtrPos + 8 * all.length;
  const direntPos = dirents.map(d => { const p = at; at += d.length; return p; });
  const clusterPtrPos = at;
  at += 8 * clusters.length;
  const clusterPos = clusters.map(c => { const p = at; at += c.length; return p; });
  const checksumPos = at;
  const mainIdx = main ? byPath("C", main).index : 0xffffffff;

  const header = Buffer.concat([
    u32(72173914), u16(6), u16(3), randomBytes(16), u32(all.length), u32(clusters.length),
    u64(pathPtrPos), Buffer.alloc(8, 0xff), u64(clusterPtrPos), u64(mimeListPos), u32(mainIdx), u32(0xffffffff), u64(checksumPos)
  ]);
  const body = Buffer.concat([header, mimeList, ...direntPos.map(u64), ...dirents, ...clusterPos.map(u64), ...clusters]);
  const out = Buffer.concat([body, createHash("md5").update(body).digest()]);
  writeFileSync(file, out);
  return out;
}
