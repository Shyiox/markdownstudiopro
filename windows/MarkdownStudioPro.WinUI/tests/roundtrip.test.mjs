import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const editorPath = resolve(here, '..', 'App', 'editor.html');
const browsers = [
  process.env.MSP_TEST_BROWSER,
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'
].filter(Boolean);
const browser = browsers.find(existsSync);

if (!browser) {
  console.error('No Edge or Chrome executable found. Set MSP_TEST_BROWSER to its path.');
  process.exit(2);
}

const injectedTestScript = String.raw`
<script id="roundtrip-regression-tests">
(() => {
  const api = window.markdownStudio;
  const editor = document.getElementById('editor');
  const results = [];
  const run = (name, input, verify) => {
    try {
      api.setMarkdown(input, {});
      const firstOutput = api.toMarkdown();
      const firstDetail = verify(firstOutput, editor);
      if (firstDetail !== true) {
        results.push({ name, pass: false, detail: String(firstDetail), output: firstOutput });
        return;
      }
      api.setMarkdown(firstOutput, {});
      const output = api.toMarkdown();
      const detail = verify(output, editor);
      results.push({ name, pass: detail === true, detail: detail === true ? '' : 'after reload: ' + String(detail), output });
    } catch (error) {
      results.push({ name, pass: false, detail: String(error && error.stack || error), output: '' });
    }
  };
  const has = (output, expected) => output.includes(expected) || ('missing: ' + JSON.stringify(expected));
  const all = (...checks) => checks.find(value => value !== true) ?? true;

  run('table keeps empty middle cell',
    '| A | B | C |\n| --- | --- | --- |\n| x |   | z |',
    (output, root) => all(has(output, '| x |  | z |'), root.querySelector('tbody tr')?.children.length === 3 || 'body row does not have 3 cells'));

  run('table keeps empty edge cells and fully empty row',
    '| A | B | C |\n| --- | --- | --- |\n|   | y |   |\n|   |   |   |',
    (output, root) => all(has(output, '|  | y |  |'), has(output, '|  |  |  |'),
      Array.from(root.querySelectorAll('tbody tr')).every(row => row.children.length === 3) || 'a body row lost columns'));

  run('inline code keeps emphasis markers literal',
    '\`**literal**\` and \`*star*\` and \`~~literal~~\` plus **bold** and *italic* and ~~strike~~',
    output => all(has(output, '\`**literal**\`'), has(output, '\`*star*\`'), has(output, '\`~~literal~~\`'),
      has(output, '**bold**'), has(output, '*italic*'), has(output, '~~strike~~')));

  run('inline code keeps link syntax literal',
    '\`[a](b)\` plus [link](https://example.com)',
    output => all(has(output, '\`[a](b)\`'), has(output, '[link](https://example.com)')));

  run('relative image renders as img and roundtrips',
    'Vorher ![Screenshot](./assets/x.png) Nachher',
    (output, root) => {
      const image = root.querySelector('img');
      return all(
        has(output, '![Screenshot](./assets/x.png)'),
        image?.getAttribute('alt') === 'Screenshot' || 'image alt missing',
        image?.getAttribute('data-md-src') === './assets/x.png' || 'persisted image source missing');
    });

  run('remote image remains remote and roundtrips',
    '![Logo](https://example.com/logo.png)',
    (output, root) => all(
      has(output, '![Logo](https://example.com/logo.png)'),
      root.querySelector('img')?.getAttribute('data-md-src') === 'https://example.com/logo.png' || 'remote image not rendered as img'));

  run('missing relative image keeps markdown reference',
    '![Fehlt](./assets/missing.png)',
    (output, root) => all(
      has(output, '![Fehlt](./assets/missing.png)'),
      root.querySelector('img')?.getAttribute('data-md-src') === './assets/missing.png' || 'missing image reference lost'));

  run('normal link next to image remains an anchor',
    '[OpenAI](https://openai.com) und ![Bild](./assets/a.png)',
    (output, root) => all(
      has(output, '[OpenAI](https://openai.com)'),
      has(output, '![Bild](./assets/a.png)'),
      root.querySelectorAll('a').length === 1 || 'normal link count changed',
      root.querySelectorAll('img').length === 1 || 'image was not rendered separately'));

  run('task keeps bold italic and code',
    '- [ ] **Fette Aufgabe**\n- [x] *Kursive Aufgabe*\n- [ ] \`Code\`',
    output => all(has(output, '- [ ] **Fette Aufgabe**'), has(output, '- [x] *Kursive Aufgabe*'), has(output, '- [ ] \`Code\`')));

  run('task keeps link text and URL',
    '- [x] [OpenAI](https://openai.com)',
    output => has(output, '- [x] [OpenAI](https://openai.com)'));

  run('fenced code keeps every blank line',
    '\`\`\`text\nZeile 1\n\n\nZeile 4\n\n\n\nZeile 8\n\`\`\`',
    output => all(has(output, '\`\`\`text\nZeile 1\n\n\nZeile 4\n\n\n\nZeile 8\n\`\`\`'), !output.includes('text\ntext') || 'language label leaked into code'));

  run('ordered list keeps start number',
    '3. Drei\n4. Vier\n5. Fünf',
    output => all(has(output, '3. Drei\n4. Vier\n5. Fünf'), !output.includes('1. Drei') || 'ordered list restarted at 1'));

  run('nested bullet list keeps levels',
    '- Ebene 1\n  - Ebene 2\n    - Ebene 3',
    (output, root) => all(has(output, '- Ebene 1\n  - Ebene 2\n    - Ebene 3'), root.querySelectorAll('ul ul').length === 2 || 'nested list DOM was flattened'));

  run('blockquote keeps paragraph boundary',
    '> Absatz 1\n>\n> Absatz 2',
    (output, root) => all(has(output, '> Absatz 1\n>\n> Absatz 2'), root.querySelectorAll('blockquote > p').length === 2 || 'blockquote paragraph DOM was flattened'));

  run('hardbreak keeps two trailing spaces',
    'Erste Zeile  \nZweite Zeile',
    output => has(output, 'Erste Zeile  \nZweite Zeile'));

  run('frontmatter keeps unknown fields',
    '---\ntitle: Test\ncustom: keep-me\nanotherField: 123\n---\n\nText',
    output => all(has(output, 'custom: keep-me'), has(output, 'anotherField: 123')));

  run('table keeps column alignment',
    '| Links | Mitte | Rechts |\n| :------ | :-----: | -------: |\n| A | B | C |',
    output => has(output, '| :--- | :---: | ---: |'));

  run('combined structures survive one roundtrip',
    '---\ntitle: Kombiniert\ncustom: bleibt\n---\n\n3. Drei\n   - Unterpunkt\n\n> Absatz 1\n>\n> Absatz 2\n\n| A | B | C |\n| :--- | :---: | ---: |\n|   | \`**x**\` |   |\n\n- [x] **Fertig** mit [Link](https://example.com)\n\n\`\`\`js\nconst a = 1;\n\n\nconst b = 2;\n\`\`\`',
    output => all(has(output, 'custom: bleibt'), has(output, '3. Drei\n   - Unterpunkt'), has(output, '> Absatz 1\n>\n> Absatz 2'),
      has(output, '|  | \`**x**\` |  |'), has(output, '| :--- | :---: | ---: |'),
      has(output, '- [x] **Fertig** mit [Link](https://example.com)'), has(output, 'const a = 1;\n\n\nconst b = 2;')));

  run('javascript link URL stays text, not executable anchor',
    '[Bad](javascript:alert(1))',
    (output, root) => all(
      has(output, '[Bad](javascript:alert(1))'),
      !root.querySelector('a[href^="javascript:"]') || 'unsafe anchor created'));

  run('data image URL stays text, not executable image',
    '![Bad](data:text/html,evil)',
    (output, root) => all(
      has(output, '![Bad](data:text/html,evil)'),
      !root.querySelector('img[src^="data:"]') || 'unsafe image created'));

  run('setext heading renders and keeps semantic heading',
    'Titel\n=====\n\nAbschnitt\n---------',
    (output,root)=>all(root.querySelector('h1')?.textContent==='Titel' || 'setext H1 not rendered',
      root.querySelector('h2')?.textContent==='Abschnitt' || 'setext H2 not rendered'));

  run('tilde fenced code roundtrips and keeps its language',
    '~~~js\nconst x = 1;\n~~~',
    (output,root)=>all(root.querySelector('pre[data-lang="js"] code')?.textContent==='const x = 1;' || 'tilde fence not parsed',
      has(output,'~~~js\nconst x = 1;\n~~~')));

  run('nested blockquotes preserve two levels',
    '> Outer\n> > Inner',
    (output,root)=>all(root.querySelector('blockquote blockquote')?.textContent?.includes('Inner') || 'nested quote not rendered',
      has(output,'> > Inner')));

  run('escaped punctuation stays literal after save',
    '\\*literal* and \\[not-link]',
    (output,root)=>all(has(output,'\\*literal*'), !root.querySelector('em') || 'escaped star became italic'));

  run('autolink renders a real link and retains source form',
    '<https://example.org/path>',
    (output,root)=>all(Boolean(root.querySelector('a[href="https://example.org/path"]')) || 'autolink not rendered',
      has(output,'<https://example.org/path>')));

  run('URL nested parentheses are not truncated',
    '[a](https://example.org/path_(one))',
    (output,root)=>all(root.querySelector('a')?.getAttribute('href')==='https://example.org/path_(one)' || 'link URL truncated',
      has(output,'[a](https://example.org/path_(one))')));

  run('reference-style links preserve definitions',
    '[A link][ref]\n\n[ref]: https://example.org/page',
    (output,root)=>all(Boolean(root.querySelector('a[href="https://example.org/page"]')) || 'reference link not resolved',
      has(output,'[A link][ref]'), has(output,'[ref]: https://example.org/page')));

  run('escaped table pipes do not create extra columns',
    '| A \\| B | C |\n| --- | --- |\n| x | y |',
    (output,root)=>all(root.querySelector('thead tr')?.children.length===2 || 'escaped pipe split column',
      has(output,'A \\| B')));

  run('plus bullets parse as list',
    '+ Alpha\n+ Beta',
    (output,root)=>root.querySelector('ul li')?.textContent==='Alpha' || 'plus bullet not parsed');

  run('underscore emphasis renders as emphasis',
    '__bold__ and _italic_',
    (output,root)=>all(root.querySelector('strong')?.textContent==='bold' || 'double underscore missing',
      root.querySelector('em')?.textContent==='italic' || 'underscore missing'));

  run('footnote references and definitions roundtrip without losing content',
    'Note[^one]\n\n[^one]: Dies ist die Erklärung.',
    (output,root)=>all(
      Boolean(root.querySelector('sup.md-footnote-reference')) || 'footnote reference not rendered',
      Boolean(root.querySelector('.md-footnote-definition')) || 'footnote definition missing',
      has(output,'Note[^one]'),has(output,'[^one]: Dies ist die Erklärung.')));

  run('inline math persists LaTeX while providing a rendered block',
    '$x^2+1$',
    (output,root)=>all(Boolean(root.querySelector('.md-inline-math')) || 'inline math missing',
      has(output,'$x^2+1$')));

  run('block math persists LaTeX while rendering a math region',
    '$$\n\\frac{a}{b}\n$$',
    (output,root)=>all(Boolean(root.querySelector('.md-math-block')) || 'block math missing',
      has(output,'$$\n\\frac{a}{b}\n$$')));

  run('mermaid keeps editable source with separate preview',
    String.fromCharCode(96).repeat(3)+'mermaid\nflowchart LR\n A-->B\n'+String.fromCharCode(96).repeat(3),
    (output,root)=>all(Boolean(root.querySelector('pre[data-lang="mermaid"]')) || 'Mermaid code lost',
      Boolean(root.querySelector('.md-mermaid-preview')) || 'Mermaid preview missing',
      has(output,'flowchart LR\n A-->B')));

  run('plus list marker survives serialization',
    '+ Erster\n+ Zweiter',
    output => all(has(output, '+ Erster\n+ Zweiter'), !output.includes('- Erster') || 'plus marker lost'));

  run('ordered parentheses marker survives serialization',
    '3) Dritter\n4) Vierter',
    output => has(output, '3) Dritter\n4) Vierter'));

  run('footnote after nested quote retains its definition and hover description',
    '> > Ein Zitat\n\nText[^ref]\n\n[^ref]: Eine Erklärung',
    (output,root) => all(has(output, 'Text[^ref]'),
      root.querySelector('.md-footnote-reference')?.getAttribute('title')==='Eine Erklärung' || 'definition not resolved'));

  run('file protocol link stays inactive',
    '[lokal](file:///C:/Windows/system.ini)',
    (output,root) => all(has(output, '[lokal](file:///C:/Windows/system.ini)'),
      !root.querySelector('a[href^="file:"]') || 'unsafe file link created'));

  run('baseline markdown sanity',
    '# H1\n## H2\n### H3\n#### H4\n##### H5\n###### H6\n\nText äöü 😀 with **bold**, *italic*, ~~strike~~, \`code\` and [link](https://example.com).\n\n- Bullet\n\n1. Numbered\n\n| A | B |\n| --- | --- |\n| C | D |\n\n---\n\n\`\`\`javascript\nconsole.log("ok");\n\`\`\`',
    (output, root) => {
      root.querySelector('p')?.append(document.createTextNode('\u200b'));
      const cleaned = api.toMarkdown();
      return all(...['# H1','## H2','### H3','#### H4','##### H5','###### H6','Text äöü 😀','**bold**','*italic*','~~strike~~','\`code\`','[link](https://example.com)','- Bullet','1. Numbered','| C | D |','---','\`\`\`javascript\nconsole.log("ok");\n\`\`\`'].map(value => has(output, value)),
        !cleaned.includes('\u200b') || 'zero-width caret marker leaked',
        !output.includes('javascript\nJAVASCRIPT') || 'language UI label leaked');
    });

  document.documentElement.setAttribute('data-roundtrip-results', encodeURIComponent(JSON.stringify(results)));
})();
</script>`;

const tempRoot = mkdtempSync(join(tmpdir(), 'markdown-studio-roundtrip-'));
try {
  const source = readFileSync(editorPath, 'utf8');
  const bodyEnd = source.lastIndexOf('</body>');
  if (bodyEnd < 0) throw new Error(`No </body> marker in ${editorPath}`);
  const testPage = join(tempRoot, 'editor-roundtrip.test.html');
  writeFileSync(testPage, source.slice(0, bodyEnd) + injectedTestScript + '\n' + source.slice(bodyEnd), 'utf8');

  const dump = execFileSync(browser, [
    '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
    `--user-data-dir=${join(tempRoot, 'browser-profile')}`, '--dump-dom', pathToFileURL(testPage).href
  ], { encoding: 'utf8', timeout: 110_000, maxBuffer: 16 * 1024 * 1024, windowsHide: true });

  const match = dump.match(/data-roundtrip-results="([^"]+)"/);
  if (!match) throw new Error('Browser completed without embedded roundtrip results.');
  const results = JSON.parse(decodeURIComponent(match[1].replaceAll('&amp;', '&')));
  let failed = 0;
  for (const result of results) {
    if (result.pass) {
      console.log(`PASS ${result.name}`);
    } else {
      failed++;
      console.error(`FAIL ${result.name}: ${result.detail}`);
      console.error(result.output.replace(/^/gm, '  '));
    }
  }
  console.log(`\n${results.length - failed}/${results.length} passed`);
  process.exitCode = failed ? 1 : 0;
} finally {
  if (process.env.MSP_KEEP_TEST_TEMP) console.error(`Test files kept at ${tempRoot}`);
  else rmSync(tempRoot, { recursive: true, force: true });
}
