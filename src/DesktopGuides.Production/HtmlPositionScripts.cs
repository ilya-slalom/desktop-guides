using System.Text.Json;

namespace DesktopGuides.Production;

// Fixed scripts the HTML session runs through Runtime.evaluate. They only
// measure and scroll; every decision is Core's (HtmlLocationRules). Page
// scripts stay off, but named elements can still shadow document and form
// properties, so DOM members are reached through their prototypes. Host
// values enter only as JsonSerializer literals.
internal static class HtmlPositionScripts
{
    // The text walk: the body's text nodes in document order, skipping
    // script, style, template and noscript. Offsets count UTF-16 code units.
    private const string Prelude = """
        'use strict';
        const getter = (proto, name) => Object.getOwnPropertyDescriptor(proto, name).get;
        const bodyOf = getter(Document.prototype, 'body');
        const scrollingOf = getter(Document.prototype, 'scrollingElement');
        const imagesOf = getter(Document.prototype, 'images');
        const parentOf = getter(Node.prototype, 'parentNode');
        const nameOf = getter(Node.prototype, 'nodeName');
        const dataOf = getter(CharacterData.prototype, 'data');
        const idOf = getter(Element.prototype, 'id');
        const heightOf = getter(Element.prototype, 'scrollHeight');
        const completeOf = getter(HTMLImageElement.prototype, 'complete');
        const contains = Node.prototype.contains;
        const elementRect = Element.prototype.getBoundingClientRect;
        const elementRects = Element.prototype.getClientRects;
        const skipped = new Set(['SCRIPT', 'STYLE', 'TEMPLATE', 'NOSCRIPT']);
        const walk = () => {
          const body = bodyOf.call(document);
          const nodes = [], starts = [];
          let length = 0;
          if (!body) return { nodes, starts, length };
          const walker = Document.prototype.createTreeWalker.call(document, body, NodeFilter.SHOW_TEXT);
          for (let node = walker.nextNode(); node; node = walker.nextNode()) {
            let hidden = false;
            for (let p = parentOf.call(node); p && p !== body; p = parentOf.call(p)) {
              if (skipped.has(String(nameOf.call(p)).toUpperCase())) { hidden = true; break; }
            }
            if (hidden) continue;
            nodes.push(node);
            starts.push(length);
            length += dataOf.call(node).length;
          }
          return { nodes, starts, length };
        };
        const locate = (w, offset) => {
          let lo = 0, hi = w.nodes.length - 1;
          while (lo < hi) {
            const mid = (lo + hi + 1) >> 1;
            if (w.starts[mid] <= offset) lo = mid; else hi = mid - 1;
          }
          return lo;
        };
        const range = Document.prototype.createRange.call(document);
        // The first character at or after the offset with a box of its own.
        // Line breaks, collapsed spaces and zero-width characters have none,
        // and neither has text CSS hides, which can run for thousands of
        // characters: a node whose element has no boxes is skipped whole.
        const boxAt = (w, offset) => {
          for (let i = offset < w.length ? locate(w, offset) : w.nodes.length; i < w.nodes.length; i++) {
            const node = w.nodes[i], data = dataOf.call(node);
            if (elementRects.call(parentOf.call(node)).length === 0) continue;
            for (let local = Math.max(0, offset - w.starts[i]); local < data.length; local++) {
              const ch = data[local];
              if (ch === '\n' || ch === '\r') continue;
              range.setStart(node, local);
              range.setEnd(node, local + 1);
              const r = range.getBoundingClientRect();
              if (r.width > 0 && r.height > 0) return { offset: w.starts[i] + local, top: r.top, bottom: r.bottom };
            }
          }
          return null;
        };
        const slice = (w, offset, count) => {
          const first = locate(w, offset);
          let out = dataOf.call(w.nodes[first]).substring(offset - w.starts[first]);
          for (let i = first + 1; i < w.nodes.length && out.length < count; i++) out += dataOf.call(w.nodes[i]);
          return out.substring(0, count);
        };
        const fraction = () => {
          const max = heightOf.call(scrollingOf.call(document)) - innerHeight;
          return max > 0 ? Math.min(1, Math.max(0, scrollY / max)) : 0;
        };
        // Only an image above the viewport's bottom can move the top line;
        // lazy images further down may never load and don't matter.
        const pending = () => {
          const images = imagesOf.call(document);
          let count = 0;
          for (let i = 0; i < images.length; i++) {
            const image = images[i];
            if (!completeOf.call(image) && elementRect.call(image).top < innerHeight) count++;
          }
          return count;
        };
        const scrollToBox = (box) => { scrollTo(0, scrollY + box.top); return pending(); };
        """;

    public const string ReadScroll =
        "(() => {" + Prelude + "return [scrollY, heightOf.call(scrollingOf.call(document)), innerHeight]; })()";

    // The first character whose box ends more than 1 px below the viewport
    // top. Boxes go down the page in walk order, so a binary search finds it.
    public const string Capture = "(() => {" + Prelude + """
        const w = walk();
        // No box left counts as below, so the search stays monotonic.
        const below = (o) => { const b = boxAt(w, o); return b === null || b.bottom > 1; };
        let lo = 0, hi = w.length;
        while (lo < hi) {
          const mid = (lo + hi) >> 1;
          if (below(mid)) hi = mid; else lo = mid + 1;
        }
        const box = lo < w.length ? boxAt(w, lo) : null;
        if (box === null) {
          return { offset: 0, quote: null, id: null, fraction: fraction(), href: location.href };
        }
        let id = null;
        for (let p = parentOf.call(w.nodes[locate(w, box.offset)]); p && p instanceof Element; p = parentOf.call(p)) {
          const value = idOf.call(p);
          if (value) { id = value.length > 128 ? null : value; break; }
        }
        // The cut must not leave half a surrogate pair.
        let quote = slice(w, box.offset, 160);
        const last = quote.charCodeAt(quote.length - 1);
        if (last >= 0xD800 && last <= 0xDBFF) quote = quote.substring(0, quote.length - 1);
        return { offset: box.offset, quote, id, fraction: fraction(), href: location.href };
        })()
        """;

    // Whether the quote is at the saved offset, and the quote's occurrences
    // nearest it: inside the id's element and in the whole walk.
    public static string Find(string argsJson) => "(() => {" + Prelude + "const args = " + argsJson + ";" + """
        const w = walk();
        let text = '';
        for (const node of w.nodes) text += dataOf.call(node);
        const quote = args.quote;
        const nearest = (list) => list
          .sort((a, b) => Math.abs(a - args.offset) - Math.abs(b - args.offset))
          .slice(0, 64);
        const all = [];
        for (let at = text.indexOf(quote); at >= 0 && quote.length > 0; at = text.indexOf(quote, at + 1)) all.push(at);
        let element = [];
        const target = args.id ? Document.prototype.getElementById.call(document, args.id) : null;
        if (target) {
          let start = -1, end = -1;
          for (let i = 0; i < w.nodes.length; i++) {
            if (contains.call(target, w.nodes[i])) {
              if (start < 0) start = w.starts[i];
              end = w.starts[i] + dataOf.call(w.nodes[i]).length;
            }
          }
          if (start >= 0) element = all.filter((at) => at >= start && at + quote.length <= end);
        }
        return { exact: text.substr(args.offset, quote.length) === quote, element: nearest(element), all: nearest(all) };
        })()
        """;

    // Puts the character's box top at the viewport top, clamped by the
    // browser to the scroll range.
    public static string ScrollToOffset(int offset) =>
        "(() => {" + Prelude + "const offset = " + JsonSerializer.Serialize(offset) + ";" + """
        const w = walk(), box = boxAt(w, offset);
        if (box === null) return null;
        // The page's first text is its start: the margin above it stays.
        const first = boxAt(w, 0);
        if (first !== null && box.offset <= first.offset) { scrollTo(0, 0); return pending(); }
        return scrollToBox(box);
        })()
        """;

    public static string ScrollToFraction(double fraction) =>
        "(() => {" + Prelude + "const f = " + JsonSerializer.Serialize(fraction) + ";" + """
        const max = heightOf.call(scrollingOf.call(document)) - innerHeight;
        scrollTo(0, max > 0 ? f * max : 0);
        return pending();
        })()
        """;
}
