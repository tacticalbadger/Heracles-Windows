import { marked } from 'marked';

const LANGUAGE_CLASS_RE = /^language-/;

/**
 * Convert a Markdown string into HTML that the TipTap editor can load.
 *
 * Beyond a standard Markdown -> HTML pass, this maps a couple of constructs
 * onto the editor's custom nodes:
 *  - GFM task lists (`- [ ]` / `- [x]`) become TipTap task lists.
 *  - Fenced code blocks become the editor's CodeMirror block (the only code
 *    block type the editor renders).
 *  - Images (no dedicated node in the editor) are preserved as links so the
 *    reference is not silently dropped.
 */
export const markdownToEditorHtml = (markdown: string): string => {
  const rawHtml = marked.parse(markdown ?? '', { gfm: true, breaks: false }) as string;

  // Outside a DOM (e.g. tests) just return the raw HTML.
  if (typeof document === 'undefined') {
    return rawHtml;
  }

  const doc = new DOMParser().parseFromString(rawHtml, 'text/html');

  // GFM task lists -> TipTap task list / task items.
  doc.querySelectorAll('li').forEach((li) => {
    const checkbox = li.querySelector('input[type="checkbox"]');
    if (!checkbox) return;
    const checked = (checkbox as HTMLInputElement).checked || checkbox.hasAttribute('checked');
    checkbox.remove();
    li.setAttribute('data-type', 'taskItem');
    li.setAttribute('data-checked', checked ? 'true' : 'false');
    const list = li.closest('ul');
    if (list) list.setAttribute('data-type', 'taskList');
  });

  // Fenced code blocks -> CodeMirror block node.
  doc.querySelectorAll('pre > code').forEach((code) => {
    const pre = code.parentElement;
    if (!pre) return;
    const languageClass = Array.from(code.classList).find((cls) => LANGUAGE_CLASS_RE.test(cls));
    const language = languageClass ? languageClass.replace(LANGUAGE_CLASS_RE, '') : 'plaintext';
    const replacement = doc.createElement('pre');
    replacement.setAttribute('data-code-mirror', 'true');
    replacement.setAttribute('language', language || 'plaintext');
    replacement.setAttribute('code', code.textContent ?? '');
    pre.replaceWith(replacement);
  });

  // The editor has no image node — keep the reference as a link.
  doc.querySelectorAll('img').forEach((img) => {
    const src = img.getAttribute('src') ?? '';
    const alt = img.getAttribute('alt') || src || 'image';
    const link = doc.createElement('a');
    link.setAttribute('href', src);
    link.textContent = alt;
    img.replaceWith(link);
  });

  const html = doc.body.innerHTML.trim();
  return html || '<p></p>';
};
