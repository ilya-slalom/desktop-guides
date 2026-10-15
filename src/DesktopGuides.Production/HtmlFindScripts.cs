using System.Text.Json;
using DesktopGuides.Core.Html;

namespace DesktopGuides.Production;

// T18.1: the find scripts. They read the walk's text and draw the match;
// Core matches. Highlighting goes through CSS.highlights, so the DOM, and
// every locator, stays as it was.
internal static class HtmlFindScripts
{
    private static readonly string Name = JsonSerializer.Serialize(
        HtmlReaderStyle.FindHighlight, ProductionJsonContext.Default.String);

    // The walk's text; hidden text's ranges; and the offsets where a new
    // block (a cell, a paragraph) or a <br> starts, read as spaces.
    public const string ReadText = "(() => {" + HtmlPositionScripts.Prelude + """
        const w = walk();
        const body = bodyOf.call(document);
        const previousOf = getter(Node.prototype, 'previousSibling');
        const styles = new Map();
        const styleOf = (element) => {
          let cached = styles.get(element);
          if (!cached) {
            const style = getComputedStyle(element);
            cached = {
              inline: style.display.startsWith('inline') || style.display === 'contents',
              visible: style.visibility === 'visible',
              boxed: elementRects.call(element).length > 0
            };
            styles.set(element, cached);
          }
          return cached;
        };
        const blockOf = (element) => {
          let p = element;
          while (p && p !== body && p instanceof Element && styleOf(p).inline) p = parentOf.call(p);
          return p;
        };
        // The nearest earlier sibling, at the first level that has one.
        const afterBreak = (node) => {
          for (let n = node; n && n !== body; n = parentOf.call(n)) {
            const previous = previousOf.call(n);
            if (previous) return String(nameOf.call(previous)).toUpperCase() === 'BR';
          }
          return false;
        };
        let text = '', last = null;
        const breaks = [], hidden = [];
        for (let i = 0; i < w.nodes.length; i++) {
          const node = w.nodes[i], parent = parentOf.call(node), data = dataOf.call(node);
          text += data;
          const style = styleOf(parent);
          if (!style.boxed || !style.visible) {
            hidden.push([w.starts[i], w.starts[i] + data.length]);
            continue;
          }
          const block = blockOf(parent);
          if (last !== null && (block !== last || afterBreak(node))) breaks.push(w.starts[i]);
          last = block;
        }
        return { text, breaks, hidden };
        })()
        """;

    // Highlights the walk range, then scrolls it a quarter of the way down:
    // first inside any scrolling box around it (a wide map in a <pre>),
    // then the page. True once the highlight is set.
    public static string Show(int start, int length) =>
        "(() => {" + HtmlPositionScripts.Prelude +
        "const start = " + JsonSerializer.Serialize(start, ProductionJsonContext.Default.Int32) + ";" +
        "const end = start + " + JsonSerializer.Serialize(length, ProductionJsonContext.Default.Int32) + ";" +
        "const name = " + Name + ";" + """
        const w = walk();
        if (w.nodes.length === 0 || end > w.length || end <= start) return false;
        const a = locate(w, start), b = locate(w, end - 1);
        const match = Document.prototype.createRange.call(document);
        match.setStart(w.nodes[a], start - w.starts[a]);
        match.setEnd(w.nodes[b], end - w.starts[b]);
        CSS.highlights.set(name, new Highlight(match));
        const top = Object.getOwnPropertyDescriptor(Element.prototype, 'scrollTop');
        const left = Object.getOwnPropertyDescriptor(Element.prototype, 'scrollLeft');
        const body = bodyOf.call(document);
        for (let p = parentOf.call(w.nodes[a]); p && p !== body && p instanceof Element; p = parentOf.call(p)) {
          const style = getComputedStyle(p);
          if (!/auto|scroll/.test(style.overflowX + ' ' + style.overflowY)) continue;
          const outer = elementRect.call(p), box = match.getBoundingClientRect();
          if (box.top < outer.top || box.bottom > outer.bottom) {
            top.set.call(p, top.get.call(p) + box.top - outer.top - outer.height / 4);
          }
          if (box.left < outer.left || box.right > outer.right) {
            left.set.call(p, left.get.call(p) + box.left - outer.left - outer.width / 4);
          }
        }
        const box = match.getBoundingClientRect();
        if (box.width === 0 && box.height === 0) return true;
        const x = box.left < 0 || box.right > innerWidth ? scrollX + box.left - innerWidth / 4 : scrollX;
        scrollTo(x, scrollY + box.top - innerHeight / 4);
        return true;
        })()
        """;

    public static readonly string Clear =
        "(() => { CSS.highlights.delete(" + Name + "); return true; })()";
}
