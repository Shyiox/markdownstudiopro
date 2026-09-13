import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const projectRoot = resolve(here, '..');
const xaml = readFileSync(resolve(projectRoot, 'MainWindow.xaml'), 'utf8');
const code = readFileSync(resolve(projectRoot, 'MainWindow.xaml.cs'), 'utf8');
const editor = readFileSync(resolve(projectRoot, 'App', 'editor.html'), 'utf8');

const results = [];
const run = (name, test) => {
  try {
    const detail = test();
    results.push({ name, pass: detail === true, detail: detail === true ? '' : String(detail) });
  } catch (error) {
    results.push({ name, pass: false, detail: String(error && error.stack || error) });
  }
};
const contains = (text, pattern, label) => pattern.test(text) || `${label} not found`;
const all = (...checks) => checks.find(value => value !== true) ?? true;

run('main menu exposes Drucken and routes the canonical print handler', () => all(
  contains(xaml, /<MenuFlyoutItem\b[^>]*Text="Drucken…"[^>]*Click="PrintPdfButton_Click"[^>]*\/>/, 'Drucken menu route'),
  contains(code, /PrintPdfButton_Click[^=]*=>\s*await RunUiEventAsync\("PrintPdf",\s*PrintPdfAsync\)/, 'menu canonical print call')
));

run('native Ctrl+P routes the same print handler while Ctrl+K remains unclaimed', () => all(
  contains(xaml, /<KeyboardAccelerator\b[^>]*Key="P"[^>]*Modifiers="Control"[^>]*Invoked="PrintKeyboardAccelerator_Invoked"[^>]*\/>/, 'Ctrl+P accelerator'),
  contains(code, /PrintKeyboardAccelerator_Invoked[\s\S]{0,500}RunUiEventAsync\("PrintPdf",\s*PrintPdfAsync\)/, 'Ctrl+P canonical print call'),
  !/<KeyboardAccelerator\b[^>]*Key="K"/i.test(xaml) || 'native Ctrl+K accelerator must remain unclaimed'
));

run('WebView2 browser accelerator keys stay disabled', () =>
  contains(code, /AreBrowserAcceleratorKeysEnabled\s*=\s*false\s*;/, 'disabled browser accelerators'));

run('canonical native print path uses WebView2 ShowPrintUI without script timeout', () => all(
  contains(code, /PrintPdfAsync\(\)[\s\S]{0,500}ShowPrintUI\(CoreWebView2PrintDialogKind\.Browser\)/, 'native ShowPrintUI browser print'),
  !/PrintPdfAsync\(\)[\s\S]{0,300}RunEditorCommandAsync\("printPdf"\)/.test(code) || 'PrintPdfAsync must not route through RunEditorCommandAsync',
  !/PrintPdfAsync\(\)[\s\S]{0,300}ExecuteScriptWithTimeoutAsync/.test(code) || 'PrintPdfAsync must not use script timeout'
));

run('all web PDF print entry points request the native print host', () => all(
  contains(editor, /cmd\s*===\s*'printPdf'\)\s*post\('printPdf'\)/, 'editor print command posts native request'),
  contains(editor, /choice\s*===\s*'pdf'\)\s*runCommand\('printPdf'\)/, 'fallback PDF export routes canonical command'),
  contains(editor, /type\s*===\s*'pdf'\)\s*\{\s*runCommand\('printPdf'\)/, 'PDF export button routes canonical command'),
  !/window\.print\(\)/.test(editor) || 'window.print must not be used for native WinUI printing'
));

run('Etappe 5 print CSS remains present for Light and Dark print preview', () => all(
  contains(editor, /@media\s+print\s*\{/, 'print media rules'),
  contains(editor, /color-scheme\s*:\s*light\s*!important/, 'light print color scheme'),
  contains(editor, /background\s*:\s*#fff\s*!important/, 'white print background')
));


run('Dark print rules explicitly outrank the Dark screen calibration', () => {
  const printStart = editor.indexOf('@media print{');
  const printEnd = editor.indexOf('/* === WINDOWS V2 FOLLOW-UP: WEB TRANSIENT SURFACE FAMILY === */', printStart);
  const printCss = printStart >= 0 && printEnd > printStart ? editor.slice(printStart, printEnd) : '';
  return all(
    printCss.length > 0 || 'print CSS block not found',
    contains(printCss, /body\.windows-native-shell\[data-theme="dark"\] \.document\s*\{[\s\S]*?color\s*:\s*#141413\s*!important/, 'Dark document print color override'),
    contains(printCss, /body\.windows-native-shell\[data-theme="dark"\] \.document\s+:where\(h1,h2,h3,h4,h5,h6,p,li,blockquote,td,th\)\s*\{[\s\S]*?color\s*:\s*#141413\s*!important/, 'Dark content print color override')
  );
});

run('Ctrl+P inside the WebView routes to the native print command', () =>
  contains(editor, /document\.addEventListener\('keydown',[\s\S]{0,900}\(e\.ctrlKey\|\|e\.metaKey\)\s*&&\s*key==='p'\s*\)\s*\{\s*e\.preventDefault\(\);\s*runCommand\('printPdf'\);\s*\}/, 'WebView Ctrl+P route'));



run('WebView2 download flyout is suppressed without cancelling downloads', () => all(
  contains(code, /CoreWebView2\.DownloadStarting\s*\+=\s*OnWebViewDownloadStarting\s*;/, 'DownloadStarting event registration'),
  contains(code, /OnWebViewDownloadStarting\s*\(object\??\s+sender,\s*CoreWebView2DownloadStartingEventArgs\s+args\)[\s\S]{0,350}args\.Handled\s*=\s*true\s*;/, 'DownloadStarting marks built-in UI as handled'),
  !/OnWebViewDownloadStarting\s*\([^)]*\)[\s\S]{0,350}args\.Cancel\s*=\s*true\s*;/.test(code) || 'DownloadStarting must not cancel the download',
  contains(code, /coreWebView\.DownloadStarting\s*-=\s*OnWebViewDownloadStarting\s*;/, 'DownloadStarting event cleanup')
));

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
