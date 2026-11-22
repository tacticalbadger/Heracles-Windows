const BLOCK_TAGS = new Set([
  'P', 'DIV', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'BLOCKQUOTE',
  'PRE', 'UL', 'OL', 'LI', 'HR', 'TABLE', 'THEAD', 'TBODY',
  'TR', 'TD', 'TH'
]);

const INLINE_TAGS_TO_PRESERVE = new Set([
  'STRONG', 'B', 'EM', 'I', 'U', 'S', 'STRIKE', 'DEL', 'A', 'CODE',
  'SPAN', 'SUB', 'SUP', 'MARK', 'SMALL'
]);

/**
 * Convert HTML to TipTap-compatible editor HTML.
 *
 * This sanitizes and normalizes HTML content so it can be safely loaded
 * into the TipTap editor. It preserves common formatting while filtering
 * out unsupported elements.
 */
export const htmlToEditorHtml = (html: string): string => {
  if (!html || typeof document === 'undefined') {
    return '<p></p>';
  }

  const doc = new DOMParser().parseFromString(html, 'text/html');

  // Remove script and style tags entirely
  doc.querySelectorAll('script, style, noscript, iframe, object, embed').forEach((el) => el.remove());

  // Keep images: the editor renders them as block image nodes. Strip everything
  // except src/alt/title; drop images with no usable source entirely.
  doc.querySelectorAll('img').forEach((img) => {
    const src = img.getAttribute('src') || '';
    if (!src) {
      img.remove();
      return;
    }
    const alt = img.getAttribute('alt');
    const title = img.getAttribute('title');
    const clean = doc.createElement('img');
    clean.setAttribute('src', src);
    if (alt) clean.setAttribute('alt', alt);
    if (title) clean.setAttribute('title', title);
    img.replaceWith(clean);
  });

  // Convert checkboxes in lists to task items
  doc.querySelectorAll('li').forEach((li) => {
    const checkbox = li.querySelector('input[type="checkbox"]');
    if (checkbox) {
      const checked = (checkbox as HTMLInputElement).checked || checkbox.hasAttribute('checked');
      checkbox.remove();
      li.setAttribute('data-type', 'taskItem');
      li.setAttribute('data-checked', checked ? 'true' : 'false');
      const list = li.closest('ul, ol');
      if (list) list.setAttribute('data-type', 'taskList');
    }
  });

  // Convert pre/code blocks to CodeMirror blocks
  doc.querySelectorAll('pre').forEach((pre) => {
    const code = pre.querySelector('code');
    const text = code ? code.textContent || '' : pre.textContent || '';

    // Try to extract language from code class (e.g., "language-javascript")
    let language = 'plaintext';
    if (code) {
      const langClass = Array.from(code.classList).find((cls) => cls.startsWith('language-'));
      if (langClass) {
        language = langClass.replace('language-', '');
      }
    }

    const replacement = doc.createElement('pre');
    replacement.setAttribute('data-code-mirror', 'true');
    replacement.setAttribute('language', language);
    replacement.setAttribute('code', text);
    pre.replaceWith(replacement);
  });

  // Convert headings
  const headingMap: Record<string, string> = { H1: 'h1', H2: 'h2', H3: 'h3', H4: 'h4', H5: 'h4', H6: 'h4' };
  Object.keys(headingMap).forEach((tag) => {
    doc.querySelectorAll(tag.toLowerCase()).forEach((el) => {
      const replacement = doc.createElement(headingMap[tag]);
      replacement.innerHTML = el.innerHTML;
      el.replaceWith(replacement);
    });
  });

  // Clean up empty paragraphs and normalize
  const body = doc.body;
  let result = body.innerHTML.trim();

  // If result is empty or just whitespace, return empty paragraph
  if (!result || result === '<p></p>') {
    return '<p></p>';
  }

  return result;
};
