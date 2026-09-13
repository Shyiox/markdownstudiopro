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
