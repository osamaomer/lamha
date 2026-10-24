/* Lamha desktop — turns an article from a downloaded Wikipedia (.zim) into safe nodes for the reader.
 * The file's HTML is parsed inert (DOMParser: nothing runs, nothing loads), then rebuilt element by element from an
 * allow-list: known tags only, a few plain attributes, never scripts, styles, event handlers, forms or frames.
 * Links become data-path (another article), data-anchor (a place in this one) or data-ext (a web address): the reader
 * handles every click, so nothing navigates by itself. Pictures point at lamha-wiki: (images only, main.js). */
// eslint-disable-next-line no-unused-vars
var LamhaWikiSanitize = (() => {
  "use strict"; // inside: a file-level one would keep this global to itself where the tests eval it
  const KEEP = new Set(("a abbr b bdi bdo blockquote br caption cite code dd del details dfn div dl dt em figcaption figure " +
    "h1 h2 h3 h4 h5 h6 hr i img ins kbd li mark ol p pre q rp rt ruby s samp section small span strong sub summary sup " +
    "table tbody td tfoot th thead time tr u ul var wbr").split(" "));
  const DROP = new Set(("script style link meta noscript template iframe frame frameset object embed applet form input button " +
    "textarea select option audio video source track canvas svg math map area base head title").split(" "));
  // boxes that only make sense on the website: edit links, navigation boxes, maintenance notices, Kiwix's footer
  const DROP_CLASS = /(^|\s)(mw-editsection|navbox|vertical-navbox|navbox-styles|ambox|sistersitebox|noprint|catlinks|printfooter|mw-authority-control|zim-footer|mw-empty-elt|Z3988)(\s|$)/;
  const ATTRS = new Set(["dir", "lang", "title", "alt", "colspan", "rowspan", "scope", "width", "height", "start", "reversed", "datetime"]);
  const ID = "wk-"; // article ids never meet the reader's own
  const MAX_DEPTH = 200;

  /** The address of the article itself, to resolve its relative links and pictures against. */
  const baseOf = path => "http://zim/C/" + String(path).split("/").map(encodeURIComponent).join("/");
  /** decodeURIComponent that never throws: one link with a stray "%" mustn't stop the whole article. */
  const decode = s => { try { return decodeURIComponent(s); } catch (_) { return s; } };
  const internal = url => (url.protocol === "http:" && url.host === "zim" && url.pathname.startsWith("/C/") ? decode(url.pathname.slice(3)) : null);
  const anchor = hash => ID + decode(String(hash).replace(/^#/, ""));

  /**
   * html: the article's page. opts.path: its path in the file. opts.assetUrl(path): the address of a picture.
   * opts.document: where the nodes are made (the reader's page; jsdom in the tests).
   * → { fragment, headings: [{ id, text, level }] }
   */
  function article(html, { path, assetUrl, document: doc = document }) {
    const src = new DOMParser().parseFromString(String(html), "text/html");
    const root = src.querySelector("#mw-content-text .mw-parser-output") || src.querySelector("#mw-content-text") || src.body;
    const base = baseOf(path);
    const headings = [];
    const fragment = doc.createDocumentFragment();

    function copy(node, into, depth, inTable) {
      if (depth > MAX_DEPTH) return;
      if (node.nodeType === 3) { into.append(doc.createTextNode(node.data)); return; }
      if (node.nodeType !== 1) return; // comments and the rest
      const tag = node.localName;
      if (DROP.has(tag) || DROP_CLASS.test(node.getAttribute("class") || "")) return;
      if (!KEEP.has(tag)) { for (const c of node.childNodes) copy(c, into, depth + 1, inTable); return; } // unknown: its content only

      let el = doc.createElement(tag);
      for (const { name, value } of [...node.attributes]) {
        if (ATTRS.has(name) && value.length < 500) el.setAttribute(name, value);
      }
      const cls = String(node.getAttribute("class") || "").split(/\s+/).filter(c => /^[\w-]{1,60}$/.test(c));
      if (cls.length) el.setAttribute("class", cls.join(" "));
      const id = node.getAttribute("id");
      if (id && id.length < 200) el.id = ID + id;
      if (tag === "details") el.setAttribute("open", "");

      if (tag === "a") {
        const href = node.getAttribute("href") || "";
        let url = null;
        try { url = href && !href.startsWith("#") ? new URL(href, base) : null; } catch (_) { url = null; }
        if (href.startsWith("#") && href.length > 1) el.dataset.anchor = anchor(href);
        else if (url && internal(url) !== null) {
          el.dataset.path = internal(url);
          if (url.hash) el.dataset.anchor = anchor(url.hash);
        } else if (url && (url.protocol === "https:" || url.protocol === "http:")) {
          el.dataset.ext = url.href;
          el.classList.add("ext");
        } else { // mailto:, javascript:, nothing: the text only
          for (const c of node.childNodes) copy(c, into, depth + 1, inTable);
          return;
        }
        el.setAttribute("href", "#");
      }

      if (tag === "img") {
        let url = null;
        try { url = new URL(node.getAttribute("src") || "", base); } catch (_) { url = null; }
        const p = url && internal(url);
        if (!p) return; // a picture from elsewhere: offline, and never a request to the web
        el.setAttribute("src", assetUrl(p)); // not lazy: they come from the file on this PC, and lazy ones stayed blank
        el.setAttribute("decoding", "async");
      }

      for (const c of node.childNodes) copy(c, el, depth + 1, inTable || tag === "table");
      if (/^h[2-4]$/.test(tag) && el.id) headings.push({ id: el.id, text: el.textContent.trim(), level: Number(tag[1]) });
      if (tag === "table") { // wide tables scroll sideways instead of widening the page (or the info box around them)
        const wrap = doc.createElement("div");
        wrap.className = "wk-scroll" + (cls.some(c => /^infobox/.test(c)) ? " wk-infobox" : "");
        wrap.append(el);
        el = wrap;
      }
      into.append(el);
    }

    for (const c of root.childNodes) copy(c, fragment, 0, false);
    return { fragment, headings };
  }

  return { article, baseOf };
})();
