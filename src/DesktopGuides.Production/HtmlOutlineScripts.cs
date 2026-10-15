namespace DesktopGuides.Production;

// T18.2: the collect script. It reports the page's visible headings and
// its same-page links, each with the walk position it leads to; Core's
// HtmlOutlineRules decides the contents. One pass over the walk finds each
// element's first text, so a long page stays inside the script timeout.
internal static class HtmlOutlineScripts
{
    public const string Collect = "(() => {" + HtmlPositionScripts.Prelude + """
        const w = walk();
        const body = bodyOf.call(document);
        const textOf = getter(Node.prototype, 'textContent');
        const attributeOf = Element.prototype.getAttribute;
        const positionOf = Node.prototype.compareDocumentPosition;
        const all = (selector) => Array.from(Document.prototype.querySelectorAll.call(document, selector));
        const tagOf = (element) => String(nameOf.call(element)).toUpperCase();
        // Each element's first non-blank text offset. A node's chain stops at
        // the first ancestor already set: an earlier node set the rest.
        const firstText = new Map();
        for (let i = 0; i < w.nodes.length; i++) {
          const lead = dataOf.call(w.nodes[i]).search(/\S/);
          if (lead < 0) continue;
          for (let p = parentOf.call(w.nodes[i]); p && p !== body; p = parentOf.call(p)) {
            if (firstText.has(p)) break;
            firstText.set(p, w.starts[i] + lead);
          }
        }
        // An empty anchor (<a name="forest"></a>) leads to the text after it.
        // The walk is in document order, so a binary search finds the first
        // node after the anchor; a scan per anchor would be quadratic.
        const followingText = (element) => {
          let lo = 0, hi = w.nodes.length;
          while (lo < hi) {
            const mid = (lo + hi) >> 1;
            if (positionOf.call(element, w.nodes[mid]) & Node.DOCUMENT_POSITION_FOLLOWING) hi = mid; else lo = mid + 1;
          }
          for (let i = lo; i < w.nodes.length; i++) {
            const lead = dataOf.call(w.nodes[i]).search(/\S/);
            if (lead >= 0) return w.starts[i] + lead;
          }
          return -1;
        };
        const idNear = (element) => {
          for (let p = element; p && p instanceof Element; p = parentOf.call(p)) {
            const value = idOf.call(p);
            if (value) return value.length > 128 ? null : value;
          }
          return null;
        };
        const fractionOf = (element) => {
          const height = heightOf.call(scrollingOf.call(document));
          return height > 0 ? Math.min(1, Math.max(0, (elementRect.call(element).top + scrollY) / height)) : 0;
        };
        const offsetOf = (element) => firstText.has(element) ? firstText.get(element) : followingText(element);
        // The cut must not leave half a surrogate pair.
        const quoteAt = (offset) => {
          let quote = slice(w, offset, 160);
          const last = quote.charCodeAt(quote.length - 1);
          if (last >= 0xD800 && last <= 0xDBFF) quote = quote.substring(0, quote.length - 1);
          return quote;
        };
        const place = (element) => {
          const offset = offsetOf(element);
          return offset < 0 ? null : { offset, quote: quoteAt(offset), id: idNear(element), fraction: fractionOf(element) };
        };
        const headings = [];
        for (const heading of all('h1,h2,h3,h4,h5,h6')) {
          if (elementRects.call(heading).length === 0) continue;
          const at = place(heading);
          if (at !== null) headings.push({ level: Number(tagOf(heading)[1]), text: String(textOf.call(heading)), ...at });
        }
        // The outermost list holds a contents list's nested lists too.
        const containers = new Map();
        const links = [];
        for (const link of all('a[href^="#"]')) {
          if (elementRects.call(link).length === 0) continue;
          const href = String(attributeOf.call(link, 'href') || '');
          let id = href.slice(1);
          try { id = decodeURIComponent(id); } catch (e) { }
          if (!id) continue;
          let container = null, depth = 0;
          for (let p = parentOf.call(link); p && p !== body && p instanceof Element; p = parentOf.call(p)) {
            const tag = tagOf(p);
            if (tag === 'UL' || tag === 'OL') { depth++; container = p; }
            else if (container === null && (tag === 'NAV' || tag === 'DL' || tag === 'TABLE')) container = p;
          }
          if (container === null) continue;
          if (!containers.has(container)) containers.set(container, containers.size);
          const target = Document.prototype.getElementById.call(document, id) ||
            all('a[name="' + CSS.escape(id) + '"]')[0] || null;
          // The link's own place: a contents list leads further down the page.
          links.push({ text: String(textOf.call(link)), offset: offsetOf(link), container: containers.get(container),
            depth: Math.max(1, depth), target: target ? place(target) : null });
        }
        return { headings, links };
        })()
        """;
}
