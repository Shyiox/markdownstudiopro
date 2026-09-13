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
<script id="stage5-regression-tests">
(() => {
  const results = [];
  const run = (name, test) => {
    try {
      const detail = test();
      results.push({ name, pass: detail === true, detail: detail === true ? '' : String(detail) });
    } catch (error) {
      results.push({ name, pass: false, detail: String(error && error.stack || error) });
    }
  };
  const equal = (actual, expected, label) => actual === expected || (label + ': expected ' + JSON.stringify(expected) + ', got ' + JSON.stringify(actual));
  const all = (...checks) => checks.find(value => value !== true) ?? true;
  const parseColor = value => {
    const match = String(value).match(/rgba?\(([^)]+)\)/i);
    if (!match) throw new Error('Unsupported computed color: ' + value);
    const parts = match[1].split(/[,\s/]+/).filter(Boolean).map(Number);
    return { r: parts[0], g: parts[1], b: parts[2], a: parts.length > 3 ? parts[3] : 1 };
  };
  const composite = (front, back) => {
    const alpha = front.a + back.a * (1 - front.a);
    if (!alpha) return { r: 0, g: 0, b: 0, a: 0 };
    return {
      r: (front.r * front.a + back.r * back.a * (1 - front.a)) / alpha,
      g: (front.g * front.a + back.g * back.a * (1 - front.a)) / alpha,
      b: (front.b * front.a + back.b * back.a * (1 - front.a)) / alpha,
      a: alpha
    };
  };
  const effectiveBackground = element => {
    const layers = [];
    for (let current = element; current; current = current.parentElement) {
      layers.push(parseColor(getComputedStyle(current).backgroundColor));
    }
    let result = { r: 255, g: 255, b: 255, a: 1 };
    for (const layer of layers.reverse()) result = composite(layer, result);
    return result;
  };
  const luminance = color => {
    const channel = value => {
      const normalized = value / 255;
      return normalized <= 0.04045 ? normalized / 12.92 : Math.pow((normalized + 0.055) / 1.055, 2.4);
    };
    return 0.2126 * channel(color.r) + 0.7152 * channel(color.g) + 0.0722 * channel(color.b);
  };
  const contrast = (foreground, background) => {
    const opaqueForeground = composite(foreground, background);
    const lighter = Math.max(luminance(opaqueForeground), luminance(background));
    const darker = Math.min(luminance(opaqueForeground), luminance(background));
    return (lighter + 0.05) / (darker + 0.05);
  };
  const readable = (element, backgroundElement, minimum, label, pseudo = null) => {
    const foreground = parseColor(getComputedStyle(element, pseudo).color);
    const background = effectiveBackground(backgroundElement);
    const ratio = contrast(foreground, background);
    return ratio >= minimum || (label + ': expected contrast >= ' + minimum + ', got ' + ratio.toFixed(2));
  };
  const paletteElements = theme => {
    applySettings({ theme });
    showCommandPalette();
    const menu = document.getElementById('commandMenu');
    const input = document.getElementById('commandPaletteInput');
    const item = document.querySelector('.command-palette-item.selected');
    if (!menu || !input || !item) throw new Error('Command Palette did not render its selected command');
    return {
      menu,
      input,
      item,
      label: item.querySelector('.label'),
      icon: item.querySelector('.icon'),
      desc: item.querySelector('.desc'),
      shortcut: item.querySelector('.shortcut')
    };
  };

  const withBridgeCapture = test => {
    const messages = [];
    const chromeObject = window.chrome || (window.chrome = {});
    const originalWebView = chromeObject.webview;
    chromeObject.webview = { postMessage: message => messages.push(message) };
    try { return test(messages); }
    finally {
      if (originalWebView === undefined) delete chromeObject.webview;
      else chromeObject.webview = originalWebView;
    }
  };

  const printStyleRules = () => {
    const rules = [];
    for (const sheet of document.styleSheets) {
      for (const rule of sheet.cssRules) {
        if (rule.type === CSSRule.MEDIA_RULE && rule.conditionText === 'print') {
          rules.push(...rule.cssRules);
        }
      }
    }
    return rules;
  };
  const printRule = selectorPart => printStyleRules().find(rule => rule.selectorText?.includes(selectorPart));
  const printDeclaration = (selectorPart, property) => {
    const rule = printRule(selectorPart);
    return rule ? {
      value: rule.style.getPropertyValue(property).trim(),
      priority: rule.style.getPropertyPriority(property)
    } : null;
  };
  const printContract = () => {
    setMarkdown('# Heading\n\n## Subheading\n\nParagraph with [link](https://example.com) and \`inline\`.\n\n\`\`\`\ncode\n\`\`\`\n\n| A | B |\n| --- | --- |\n| 1 | 2 |');
    const htmlBodyBackground = printDeclaration('html, body', 'background');
    const htmlBodyColor = printDeclaration('html, body', 'color');
    const documentBackground = printDeclaration('.document', 'background');
    const documentColor = printDeclaration('.document', 'color');
    const contentColor = printDeclaration(':where(h1, h2, h3, h4, h5, h6, p, li, blockquote, td, th)', 'color');
    const codeColor = printDeclaration('code:not(pre code)', 'color');
    const preColor = printDeclaration('.document pre', 'color');
    const tableCellColor = printDeclaration('.document td', 'color');
    return all(
      equal(htmlBodyBackground?.value, 'rgb(255, 255, 255)', 'page background'),
      equal(htmlBodyBackground?.priority, 'important', 'page background priority'),
      equal(htmlBodyColor?.value, 'rgb(20, 20, 19)', 'page text color'),
      equal(htmlBodyColor?.priority, 'important', 'page text priority'),
      equal(documentBackground?.value, 'rgb(255, 255, 255)', 'document background'),
      equal(documentColor?.value, 'rgb(20, 20, 19)', 'document text color'),
      equal(contentColor?.value, 'rgb(20, 20, 19)', 'content text color'),
      equal(codeColor?.priority, 'important', 'inline code priority'),
      equal(preColor?.priority, 'important', 'code block priority'),
      equal(tableCellColor?.priority, 'important', 'table cell priority')
    );
  };

  run('print colors stay paper-safe in Dark theme', () => {
    applySettings({ theme: 'dark' });
    return all(equal(document.body.dataset.theme, 'dark', 'screen theme'), printContract());
  });

  run('print colors stay paper-safe in Light theme', () => {
    applySettings({ theme: 'light' });
    return all(equal(document.body.dataset.theme, 'light', 'screen theme'), printContract());
  });

  run('print colors survive Dark Light Dark switching', () => {
    applySettings({ theme: 'dark' });
    applySettings({ theme: 'light' });
    applySettings({ theme: 'dark' });
    return all(equal(document.body.dataset.theme, 'dark', 'final screen theme'), printContract());
  });

  run('export dialog Markdown action posts file export with current content', () => withBridgeCapture(messages => {
    setMarkdown('ETAPPE5 MARKDOWN EXPORT TEST');
    showExportDialog();
    document.querySelector('[data-export="md"]').click();
    const exportMessages = messages.filter(message => ['copyMarkdown', 'exportMarkdownFile'].includes(message.type));
    return all(
      equal(exportMessages.length, 1, 'message count'),
      equal(exportMessages[0]?.type, 'exportMarkdownFile', 'message type'),
      equal(exportMessages[0]?.markdown?.trim(), 'ETAPPE5 MARKDOWN EXPORT TEST', 'markdown payload')
    );
  }));

  run('Copy Markdown remains a separate clipboard command', () => withBridgeCapture(messages => {
    setMarkdown('COPY MARKDOWN TEST');
    runCommand(CONST.COMMANDS.COPY_MD);
    const copyMessages = messages.filter(message => message.type === 'copyMarkdown');
    return all(
      equal(copyMessages.length, 1, 'message count'),
      equal(copyMessages[0]?.type, 'copyMarkdown', 'message type'),
      equal(copyMessages[0]?.markdown?.trim(), 'COPY MARKDOWN TEST', 'markdown payload')
    );
  }));

  run('other export dialog actions keep their mappings', () => withBridgeCapture(messages => {
    setMarkdown('OTHER EXPORT TEST');
    showExportDialog();
    document.querySelector('[data-export="html"]').click();
    showExportDialog();
    document.querySelector('[data-export="pdf"]').click();
    const htmlMessages = messages.filter(message => message.type === 'exportHtml');
    const printMessages = messages.filter(message => message.type === 'printPdf');
    return all(
      equal(htmlMessages.length, 1, 'HTML bridge message count'),
      equal(htmlMessages[0]?.type, 'exportHtml', 'HTML message type'),
      htmlMessages[0]?.html?.includes('OTHER EXPORT TEST') || 'HTML payload lacks current content',
      equal(printMessages.length, 1, 'native print bridge message count')
    );
  }));

  const runPaletteTheme = (startTheme, commandId, expectedTheme) => withBridgeCapture(messages => {
    localStorage.removeItem('markdownStudioTheme');
    applySettings({ theme: startTheme });
    paletteCommands.find(command => command.id === commandId).action();
    const themeMessages = messages.filter(message => message.type === 'updateSetting' && message.key === 'theme');
    return all(
      equal(document.body.dataset.theme, expectedTheme, 'web theme'),
      equal(themeMessages.length, 1, 'native theme message count'),
      equal(themeMessages[0]?.value, expectedTheme, 'native theme value'),
      equal(localStorage.getItem('markdownStudioTheme'), null, 'web-only persisted theme')
    );
  });

  run('palette Light updates WebView and requests canonical native theme', () => runPaletteTheme('dark', 'theme-light', 'light'));
  run('palette Dark updates WebView and requests canonical native theme', () => runPaletteTheme('light', 'theme-dark', 'dark'));

  run('native settings still update the WebView without bridge echo', () => withBridgeCapture(messages => {
    localStorage.removeItem('markdownStudioTheme');
    applySettings({ theme: 'dark' });
    const dark = document.body.dataset.theme;
    applySettings({ theme: 'light' });
    return all(
      equal(dark, 'dark', 'Dark settings theme'),
      equal(document.body.dataset.theme, 'light', 'Light settings theme'),
      equal(messages.length, 0, 'bridge echo count'),
      equal(localStorage.getItem('markdownStudioTheme'), null, 'settings localStorage write')
    );
  }));

  run('Dark Command Palette normal command text has readable computed contrast', () => {
    const elements = paletteElements('dark');
    try {
      return all(
        readable(elements.label, elements.item, 4.5, 'command label'),
        readable(elements.desc, elements.item, 4.5, 'command description'),
        readable(elements.icon, elements.item, 3, 'command icon'),
        readable(elements.shortcut, elements.shortcut, 4.5, 'command shortcut')
      );
    } finally { hideCommandPalette(); }
  });

  run('Dark Command Palette search text and placeholder have readable computed contrast', () => {
    const elements = paletteElements('dark');
    try {
      return all(
        readable(elements.input, elements.input, 4.5, 'search text'),
        readable(elements.input, elements.input, 4.5, 'search placeholder', '::placeholder')
      );
    } finally { hideCommandPalette(); }
  });

  run('Dark Command Palette keyboard selection preserves readable computed contrast', () => {
    const elements = paletteElements('dark');
    try {
      const list = document.getElementById('commandPaletteList');
      setPaletteSelectedIndex(list, 1, false);
      const selected = list.querySelector('.command-palette-item.selected');
      return all(
        readable(selected.querySelector('.label'), selected, 4.5, 'selected label'),
        readable(selected.querySelector('.desc'), selected, 4.5, 'selected description'),
        readable(selected.querySelector('.icon'), selected, 3, 'selected icon')
      );
    } finally { hideCommandPalette(); }
  });

  run('Light Command Palette contrast remains readable', () => {
    const elements = paletteElements('light');
    try {
      return all(
        readable(elements.label, elements.item, 4.5, 'Light command label'),
        readable(elements.desc, elements.item, 4, 'Light command description'),
        readable(elements.input, elements.input, 4.5, 'Light search text'),
        readable(elements.input, elements.input, 3.5, 'Light placeholder', '::placeholder')
      );
    } finally { hideCommandPalette(); }
  });

  document.documentElement.setAttribute('data-stage5-results', encodeURIComponent(JSON.stringify(results)));
})();
</script>`;

const tempRoot = mkdtempSync(join(tmpdir(), 'markdown-studio-stage5-'));
try {
  const source = readFileSync(editorPath, 'utf8');
  const bodyEnd = source.lastIndexOf('</body>');
  if (bodyEnd < 0) throw new Error(`No </body> marker in ${editorPath}`);
  const testPage = join(tempRoot, 'editor-stage5.test.html');
  writeFileSync(testPage, source.slice(0, bodyEnd) + injectedTestScript + '\n' + source.slice(bodyEnd), 'utf8');

  const dump = execFileSync(browser, [
    '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
    `--user-data-dir=${join(tempRoot, 'browser-profile')}`, '--dump-dom', pathToFileURL(testPage).href
  ], { encoding: 'utf8', timeout: 110_000, maxBuffer: 16 * 1024 * 1024, windowsHide: true });

  const match = dump.match(/data-stage5-results="([^"]+)"/);
  if (!match) throw new Error('Browser completed without embedded Etappe-5 results.');
  const results = JSON.parse(decodeURIComponent(match[1].replaceAll('&amp;', '&')));
  let failed = 0;
  for (const result of results) {
    if (result.pass) console.log(`PASS ${result.name}`);
    else {
      failed++;
      console.error(`FAIL ${result.name}: ${result.detail}`);
    }
  }
  console.log(`\n${results.length - failed}/${results.length} passed`);
  process.exitCode = failed ? 1 : 0;
} finally {
  if (process.env.MSP_KEEP_TEST_TEMP) console.error(`Test files kept at ${tempRoot}`);
  else rmSync(tempRoot, { recursive: true, force: true });
}
