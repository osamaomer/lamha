/* Lamha desktop — reads Wikipedia's offline files (.zim, the format Kiwix publishes: https://wiki.openzim.org/wiki/ZIM_file_format).
 * Only the parts a lookup needs are read from disk: the file can be tens of GB. Everything in the file is untrusted,
 * so every offset and count is checked against the file's size before it's used.
 *
 * The layout, in short: a header; a list of MIME types; the entries' offsets in path order; the entries themselves
 * (namespace, path, title, and where the content is: a blob in a cluster, or another entry for a redirect); and the
 * clusters, each a group of blobs, compressed with Zstandard (current files) or stored as they are (pictures).
 * Titles are searched through the "front articles" list (X/listing/titleOrdered/v1), sorted by title. */
"use strict";
const fs = require("node:fs");
const zlib = require("node:zlib");
const { promisify } = require("node:util");

const zstd = promisify(zlib.zstdDecompress);
const MAGIC = 72173914;
const REDIRECT = 0xffff;
const NO_ENTRY = 0xffffffff;
const MAX_CLUSTER = 64 * 1024 * 1024; // a compressed cluster is ~1 MB; anything near this is a broken file
const MAX_BLOB = 256 * 1024 * 1024;

class ZimError extends Error {
  constructor(code) { super("zim: " + code); this.code = code; }
}

/** A small least-recently-used cache. */
class Lru {
  constructor(max) { this.max = max; this.map = new Map(); }
  get(k) {
    const v = this.map.get(k);
    if (v !== undefined) { this.map.delete(k); this.map.set(k, v); }
    return v;
  }
  set(k, v) {
    this.map.delete(k);
    this.map.set(k, v);
    if (this.map.size > this.max) this.map.delete(this.map.keys().next().value);
  }
}

const utf8 = s => Buffer.from(String(s), "utf8");

class ZimFile {
  /** Opens a file and reads its header; throws ZimError("not_zim") for anything else. */
  static async open(file) {
    const fh = await fs.promises.open(file, "r");
    try {
      const z = new ZimFile(fh, (await fh.stat()).size);
      await z.readHeader();
      return z;
    } catch (err) {
      await fh.close().catch(() => {});
      throw err instanceof ZimError ? err : new ZimError("not_zim");
    }
  }

  constructor(fh, size) {
    this.fh = fh;
    this.size = size;
    this.entries = new Lru(4096);
    this.clusters = new Lru(6); // decompressed clusters: an article and its neighbours share one
    this.titles = null; // the front-article list (Uint32Array), read the first time a title is searched
  }

  async read(pos, len) {
    if (!(pos >= 0 && len >= 0 && pos + len <= this.size)) throw new ZimError("bad_offset");
    const buf = Buffer.alloc(len);
    const { bytesRead } = await this.fh.read(buf, 0, len, pos);
    if (bytesRead !== len) throw new ZimError("short_read");
    return buf;
  }

  async readHeader() {
    const h = await this.read(0, 80);
    if (h.readUInt32LE(0) !== MAGIC) throw new ZimError("not_zim");
    this.major = h.readUInt16LE(4);
    this.minor = h.readUInt16LE(6);
    this.uuid = h.subarray(8, 24).toString("hex");
    this.entryCount = h.readUInt32LE(24);
    this.clusterCount = h.readUInt32LE(28);
    const num = at => Number(h.readBigUInt64LE(at));
    this.pathPtrPos = num(32);
    this.clusterPtrPos = num(48);
    this.mimeListPos = num(56);
    this.mainPage = h.readUInt32LE(64);
    this.checksumPos = num(72);
    if (this.major !== 5 && this.major !== 6) throw new ZimError("unsupported_version");
    const fits = (pos, n) => pos + n <= this.size;
    if (!fits(this.pathPtrPos, this.entryCount * 8) || !fits(this.clusterPtrPos, this.clusterCount * 8) || this.checksumPos > this.size) {
      throw new ZimError("bad_offset");
    }
    // current files keep articles in "C"; files from before 2021 in "A"
    this.contentNs = this.major === 6 && this.minor >= 1 ? "C" : "A";
    const mime = await this.read(this.mimeListPos, Math.min(4096, this.size - this.mimeListPos));
    this.mimeTypes = [];
    for (let at = 0; at < mime.length;) {
      const end = mime.indexOf(0, at);
      if (end <= at) break; // an empty string ends the list
      this.mimeTypes.push(mime.toString("latin1", at, end));
      at = end + 1;
    }
  }

  close() { return this.fh.close().catch(() => {}); }

  /** Entry number i (in path order): { index, ns, path, title, mime, redirect } or { …, cluster, blob }. */
  async entry(i) {
    if (!(Number.isInteger(i) && i >= 0 && i < this.entryCount)) throw new ZimError("bad_entry");
    const hit = this.entries.get(i);
    if (hit) return hit;
    const pos = Number((await this.read(this.pathPtrPos + i * 8, 8)).readBigUInt64LE(0));
    let len = 512, buf;
    for (;;) { // path and title end with a zero byte each; most entries fit in 512 bytes
      buf = await this.read(pos, Math.min(len, this.size - pos));
      const mimeIdx = buf.readUInt16LE(0);
      const start = mimeIdx === REDIRECT ? 12 : 16;
      const a = buf.indexOf(0, start);
      const b = a < 0 ? -1 : buf.indexOf(0, a + 1);
      if (b >= 0) {
        const e = {
          index: i,
          ns: String.fromCharCode(buf[3]),
          path: buf.toString("utf8", start, a),
          title: buf.toString("utf8", a + 1, b),
          mime: mimeIdx === REDIRECT ? "" : this.mimeTypes[mimeIdx] || ""
        };
        if (!e.title) e.title = e.path;
        if (mimeIdx === REDIRECT) e.redirect = buf.readUInt32LE(8);
        else if (mimeIdx < 0xfffd) { e.cluster = buf.readUInt32LE(8); e.blob = buf.readUInt32LE(12); }
        this.entries.set(i, e);
        return e;
      }
      if (len >= 16384 || pos + len >= this.size) throw new ZimError("bad_entry");
      len *= 4;
    }
  }

  /** The entry at ns/path, or null. The path list is sorted by namespace, then path (as UTF-8 bytes). */
  async find(ns, path) {
    const want = utf8(ns + path);
    let lo = 0, hi = this.entryCount - 1;
    while (lo <= hi) {
      const mid = (lo + hi) >>> 1;
      const e = await this.entry(mid);
      const c = Buffer.compare(utf8(e.ns + e.path), want);
      if (c === 0) return e;
      if (c < 0) lo = mid + 1; else hi = mid - 1;
    }
    return null;
  }

  /** Follows redirects to the entry with the content (at most 10 steps: a loop is a broken file). */
  async resolve(e) {
    for (let n = 0; e && e.redirect !== undefined; n++) {
      if (n >= 10) throw new ZimError("redirect_loop");
      e = await this.entry(e.redirect);
    }
    return e;
  }

  /** The content of an entry (after its redirects), as a Buffer. */
  async content(e) {
    e = await this.resolve(e);
    if (!e || e.cluster === undefined) throw new ZimError("no_content");
    return this.blob(e.cluster, e.blob);
  }

  async clusterRange(n) {
    if (!(n >= 0 && n < this.clusterCount)) throw new ZimError("bad_cluster");
    const ptr = await this.read(this.clusterPtrPos + n * 8, n + 1 < this.clusterCount ? 16 : 8);
    const start = Number(ptr.readBigUInt64LE(0));
    const end = n + 1 < this.clusterCount ? Number(ptr.readBigUInt64LE(8)) : this.checksumPos;
    if (!(start < end && end <= this.size)) throw new ZimError("bad_cluster");
    return { start, end };
  }

  async blob(n, b) {
    const { start, end } = await this.clusterRange(n);
    const info = (await this.read(start, 1))[0];
    const comp = info & 0x0f, wide = info & 0x10 ? 8 : 4;
    if (comp === 0 || comp === 1) { // stored as it is (pictures): read only this blob
      const at = start + 1;
      const offs = await this.read(at + b * wide, wide * 2);
      const first = await this.read(at, wide);
      const off = i => (wide === 8 ? Number(offs.readBigUInt64LE(i * wide)) : offs.readUInt32LE(i * wide));
      const count = (wide === 8 ? Number(first.readBigUInt64LE(0)) : first.readUInt32LE(0)) / wide - 1;
      if (!(b < count)) throw new ZimError("bad_blob");
      const from = off(0), to = off(1);
      if (!(from <= to && at + to <= end && to - from <= MAX_BLOB)) throw new ZimError("bad_blob");
      return this.read(at + from, to - from);
    }
    if (comp !== 5) throw new ZimError("unsupported_compression"); // xz (4): files from before 2021
    let data = this.clusters.get(n);
    if (!data) {
      if (end - start - 1 > MAX_CLUSTER) throw new ZimError("bad_cluster");
      try {
        data = await zstd(await this.read(start + 1, end - start - 1), { maxOutputLength: MAX_BLOB });
      } catch (err) {
        throw err instanceof ZimError ? err : new ZimError("bad_cluster");
      }
      this.clusters.set(n, data);
    }
    const off = i => (wide === 8 ? Number(data.readBigUInt64LE(i * wide)) : data.readUInt32LE(i * wide));
    if (data.length < wide * 2) throw new ZimError("bad_blob");
    const count = off(0) / wide - 1;
    if (!(Number.isInteger(count) && b < count && (b + 2) * wide <= data.length)) throw new ZimError("bad_blob");
    const from = off(b), to = off(b + 1);
    if (!(from <= to && to <= data.length)) throw new ZimError("bad_blob");
    return data.subarray(from, to);
  }

  /** A metadata value (M/Title, M/Language, M/Name…) as text, or "". */
  async meta(name) {
    try {
      const e = await this.find("M", name);
      return e ? (await this.content(e)).toString("utf8") : "";
    } catch (_) { return ""; }
  }

  /** The front articles in title order (entry numbers). Files without the list get an empty one: no title search. */
  async titleList() {
    if (this.titles) return this.titles;
    let list = new Uint32Array(0);
    try {
      const e = await this.find("X", "listing/titleOrdered/v1");
      if (e) {
        const buf = await this.content(e);
        list = new Uint32Array(buf.buffer.slice(buf.byteOffset, buf.byteOffset + (buf.length & ~3)));
      }
    } catch (_) { /* no list */ }
    this.titles = list;
    return list;
  }

  /**
   * Front articles whose title starts with `prefix` (exactly: case and accents count), in title order, at most `limit`.
   * Titles are compared as UTF-8 bytes, the list's own order.
   */
  async titlesStartingWith(prefix, limit = 10) {
    const list = await this.titleList();
    const want = utf8(prefix);
    let lo = 0, hi = list.length;
    while (lo < hi) { // the first title >= prefix
      const mid = (lo + hi) >>> 1;
      const e = await this.entry(list[mid]);
      if (Buffer.compare(utf8(e.title), want) < 0) lo = mid + 1; else hi = mid;
    }
    const out = [];
    for (let i = lo; i < list.length && out.length < limit; i++) {
      const e = await this.entry(list[i]);
      if (!e.title.startsWith(prefix)) break;
      out.push(e);
    }
    return out;
  }

  /** The main page's entry, or null. */
  async main() {
    if (this.mainPage !== NO_ENTRY) {
      try { return await this.resolve(await this.entry(this.mainPage)); } catch (_) { /* fall through */ }
    }
    const w = await this.find("W", "mainPage").catch(() => null);
    return w ? this.resolve(w) : null;
  }
}

module.exports = { ZimFile, ZimError };
