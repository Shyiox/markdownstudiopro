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
<script id="interaction-regression-tests">
(() => {
  const results = [];
  const diagnostics = [];
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
  const input = document.getElementById('commandPaletteInput');
  const list = document.getElementById('commandPaletteList');

  const fireKey = (key) => input.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
  const hover = item => item.dispatchEvent(new MouseEvent('mouseenter', { bubbles: false, cancelable: true }));
  const pointerClick = item => {
    hover(item);
    if (!item.isConnected) return false;
    item.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, buttons: 1 }));
    item.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true }));
    item.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    return true;
  };
  const withCountedAction = (command, test) => {
    let count = 0;
    const original = command.action;
    command.action = () => { count++; original(); };
    try { return test(() => count); }
    finally { command.action = original; }
  };
  const openPalette = (query = '') => {
    showCommandPalette();
    input.value = query;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  };
  const selectedItem = () => list.querySelector('.command-palette-item.selected');
  const selectedIsVisible = () => {
    const item = selectedItem();
    if (!item) return false;
    const itemRect = item.getBoundingClientRect();
    const listRect = list.getBoundingClientRect();
    return itemRect.top >= listRect.top - 0.5 && itemRect.bottom <= listRect.bottom + 0.5;
  };

  run('palette unfiltered pointer click executes exactly once', () => {
    openPalette();
    const command = paletteFiltered[0];
    return withCountedAction(command, count => {
      const item = list.children[0];
      const connectedThroughClick = pointerClick(item);
      const result = all(equal(connectedThroughClick, true, 'pointer target connection'), equal(count(), 1, 'action count'));
      document.body.classList.remove('focus-mode');
      hideCommandPalette();
      return result;
    });
  });

  run('palette filtered pointer click executes table exactly once', () => {
    openPalette('tab');
    const index = paletteFiltered.findIndex(command => command.id === 'table');
    const command = paletteFiltered[index];
    return withCountedAction(command, count => {
      const connectedThroughClick = pointerClick(list.children[index]);
      const result = all(equal(connectedThroughClick, true, 'pointer target connection'), equal(count(), 1, 'action count'), equal(tableBackdrop.hidden, false, 'table dialog visibility'));
      closeTableDialog();
      hideCommandPalette();
      return result;
    });
  });

  run('palette hover changes selection without executing', () => {
    openPalette();
    const commands = [paletteFiltered[2], paletteFiltered[5], paletteFiltered[8]];
    const originals = commands.map(command => command.action);
    let count = 0;
    commands.forEach(command => { command.action = () => { count++; }; });
    try {
      [2, 5, 8].forEach(index => hover(list.children[index]));
      return all(equal(count, 0, 'action count'), equal(paletteSelectedIndex, 8, 'selected index'));
    } finally {
      commands.forEach((command, index) => { command.action = originals[index]; });
      hideCommandPalette();
    }
  });

  run('palette click after hover switch executes the second row', () => {
    openPalette();
    const first = paletteFiltered[3];
    const second = paletteFiltered[4];
    let firstCount = 0;
    let secondCount = 0;
    const firstAction = first.action;
    const secondAction = second.action;
    first.action = () => { firstCount++; };
    second.action = () => { secondCount++; };
    try {
      hover(list.children[3]);
      hover(list.children[4]);
      const target = list.children[4];
      const connectedThroughClick = pointerClick(target);
      return all(equal(connectedThroughClick, true, 'pointer target connection'), equal(firstCount, 0, 'first action count'), equal(secondCount, 1, 'second action count'));
    } finally {
      first.action = firstAction;
      second.action = secondAction;
      hideCommandPalette();
    }
  });

  run('palette ArrowDown keeps final row visible', () => {
    list.style.maxHeight = '88px';
    openPalette();
    for (let index = 1; index < paletteFiltered.length; index++) fireKey('ArrowDown');
    const result = all(equal(paletteSelectedIndex, paletteFiltered.length - 1, 'selected index'), equal(selectedIsVisible(), true, 'selected row visibility'));
    hideCommandPalette();
    return result;
  });

  run('palette ArrowUp keeps first row visible', () => {
    list.style.maxHeight = '88px';
    openPalette();
    for (let index = 1; index < paletteFiltered.length; index++) fireKey('ArrowDown');
    for (let index = 1; index < paletteFiltered.length; index++) fireKey('ArrowUp');
    const result = all(equal(paletteSelectedIndex, 0, 'selected index'), equal(selectedIsVisible(), true, 'selected row visibility'));
    hideCommandPalette();
    return result;
  });

  run('palette keyboard hover keyboard state stays consistent and visible', () => {
    list.style.maxHeight = '88px';
    openPalette();
    for (let index = 0; index < 4; index++) fireKey('ArrowDown');
    hover(list.children[10]);
    fireKey('ArrowDown');
    const result = all(equal(paletteSelectedIndex, 11, 'selected index'), equal(selectedIsVisible(), true, 'selected row visibility'));
    hideCommandPalette();
    return result;
  });

  run('palette filtered keyboard selection stays visible', () => {
    list.style.maxHeight = '70px';
    openPalette('e');
    const steps = Math.min(7, paletteFiltered.length - 1);
    for (let index = 0; index < steps; index++) fireKey('ArrowDown');
    const result = all(paletteFiltered.length > 2 || 'filter returned too few rows', equal(selectedIsVisible(), true, 'selected row visibility'));
    hideCommandPalette();
    list.style.maxHeight = '';
    return result;
  });

  const selectText = (selector, text) => {
    const root = document.querySelector(selector);
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    let node;
    while ((node = walker.nextNode())) {
      const start = node.nodeValue.indexOf(text);
      if (start < 0) continue;
      const range = document.createRange();
      range.setStart(node, start);
      range.setEnd(node, start + text.length);
      const selection = getSelection();
      selection.removeAllRanges();
      selection.addRange(range);
      editor.focus();
      rememberSelection();
      return true;
    }
    throw new Error('Text not found for selection: ' + text);
  };
  const focusElement = selector => {
    const element = document.querySelector(selector);
    placeCaret(element);
    rememberSelection();
    return element;
  };
  const snapshot = () => toMarkdown().trim();
  const describeNode = node => {
    if (!node) return null;
    if (node.nodeType === Node.TEXT_NODE) return '#text(' + JSON.stringify((node.nodeValue || '').slice(0, 120)) + ')';
    if (node.nodeType !== Node.ELEMENT_NODE) return 'nodeType=' + node.nodeType;
    const id = node.id ? '#' + node.id : '';
    const classes = typeof node.className === 'string' && node.className.trim()
      ? '.' + node.className.trim().split(/\s+/).join('.')
      : '';
    return '<' + node.tagName.toLowerCase() + id + classes + '>';
  };
  const selectionTrace = (scenario, step) => {
    const selection = getSelection();
    const range = selection.rangeCount ? selection.getRangeAt(0) : null;
    const candidateBlock = range ? blockFromNode(range.startContainer) : null;
    const activeBlock = candidateBlock && editor.contains(candidateBlock) ? candidateBlock : null;
    const relevantNode = activeBlock || (range && range.commonAncestorContainer.nodeType === Node.ELEMENT_NODE
      ? range.commonAncestorContainer
      : range && range.commonAncestorContainer.parentElement) || editor;
    const entry = {
      scenario,
      step,
      rangeCount: selection.rangeCount,
      collapsed: selection.isCollapsed,
      selectedLength: selection.toString().length,
      selectedText: selection.toString().slice(0, 240),
      anchorNode: describeNode(selection.anchorNode),
      anchorOffset: selection.anchorOffset,
      focusNode: describeNode(selection.focusNode),
      focusOffset: selection.focusOffset,
      startContainer: describeNode(range && range.startContainer),
      startOffset: range && range.startOffset,
      endContainer: describeNode(range && range.endContainer),
      endOffset: range && range.endOffset,
      commonAncestorContainer: describeNode(range && range.commonAncestorContainer),
      activeBlock: describeNode(activeBlock),
      relevantDom: relevantNode && relevantNode.outerHTML
        ? relevantNode.outerHTML.slice(0, 1200)
        : editor.innerHTML.slice(0, 1200),
      editorDom: editor.innerHTML.slice(0, 2400)
    };
    diagnostics.push(entry);
    return entry;
  };
  const isCollapsedEditorCaret = trace => trace.rangeCount === 1 && trace.collapsed === true && trace.selectedLength === 0;
  const undoRedo = (before, after, scenario = null) => {
    if (scenario) selectionTrace(scenario, 'before undo');
    const undoChanged = runHistoryCommand('undo');
    const undone = snapshot();
    const afterUndo = scenario ? selectionTrace(scenario, 'after undo') : null;
    const redoChanged = runHistoryCommand('redo');
    const redone = snapshot();
    const afterRedo = scenario ? selectionTrace(scenario, 'after redo') : null;
    return {
      check: all(equal(undoChanged, true, 'undo result'), equal(undone, before, 'undo snapshot'), equal(redoChanged, true, 'redo result'), equal(redone, after, 'redo snapshot')),
      afterUndo,
      afterRedo
    };
  };

  const resetFindReplace = markdown => {
    const backdrop = document.getElementById('findReplaceBackdrop');
    if (!backdrop.hidden) document.getElementById('findClose').click();
    setMarkdown(markdown, { focusWritingArea: true });
    findState = { query: '', caseSensitive: false, wholeWord: false, matches: [], currentIndex: -1 };
    showFindReplace();
    return {
      backdrop,
      find: document.getElementById('findInput'),
      replacement: document.getElementById('replaceInput'),
      status: document.getElementById('findStatus'),
      previous: document.getElementById('findPreviousBtn'),
      next: document.getElementById('findNextBtn'),
      replace: document.getElementById('replaceBtn'),
      replaceAll: document.getElementById('replaceAllBtn'),
      close: document.getElementById('findClose')
    };
  };
  const searchFromField = controls => controls.find.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
  const selectedSearchText = () => getSelection().toString();

  run('find navigation exposes one compact Previous and Next pair', () => {
    const controls = resetFindReplace('alpha beta');
    const oldButtons = Array.from(controls.backdrop.querySelectorAll('button'))
      .filter(button => button.textContent.trim() === 'Weitersuchen');
    const result = all(
      equal(controls.previous?.getAttribute('aria-label'), 'Vorheriger Treffer', 'previous accessible label'),
      equal(controls.previous?.getAttribute('title'), 'Vorheriger Treffer', 'previous tooltip'),
      equal(controls.next?.getAttribute('aria-label'), 'Nächster Treffer', 'next accessible label'),
      equal(controls.next?.getAttribute('title'), 'Nächster Treffer', 'next tooltip'),
      equal(oldButtons.length, 0, 'legacy single next button count')
    );
    controls.close.click();
    return result;
  });

  run('find Enter selects a replaceable first match', () => {
    const controls = resetFindReplace('apple apple');
    controls.find.value = 'apple';
    controls.replacement.value = 'orange';
    searchFromField(controls);
    controls.replace.click();
    const result = all(
      equal(snapshot(), 'orange apple', 'replace snapshot'),
      equal(findState.currentIndex, 0, 'current index'),
      equal(findState.matches.length, 1, 'remaining match count'),
      equal(selectedSearchText(), 'apple', 'remaining selection')
    );
    controls.close.click();
    return result;
  });

  run('find query change makes Replace use the visible query', () => {
    const controls = resetFindReplace('apple banana apple');
    controls.find.value = 'apple';
    searchFromField(controls);
    controls.find.value = 'banana';
    controls.replacement.value = 'pear';
    controls.replace.click();
    const result = all(
      equal(snapshot(), 'apple pear apple', 'replace snapshot'),
      equal(findState.query, 'banana', 'synchronized query'),
      equal(findState.matches.length, 0, 'remaining match count'),
      equal(findState.currentIndex, -1, 'no-match index')
    );
    controls.close.click();
    return result;
  });

  run('find query change makes Replace All use the visible query', () => {
    const controls = resetFindReplace('apple banana apple banana');
    controls.find.value = 'apple';
    searchFromField(controls);
    controls.find.value = 'banana';
    controls.replacement.value = 'pear';
    controls.replaceAll.click();
    const result = all(
      equal(snapshot(), 'apple pear apple pear', 'replace-all snapshot'),
      equal(findState.query, 'banana', 'synchronized query'),
      equal(findState.matches.length, 0, 'cleared match count'),
      equal(findState.currentIndex, -1, 'cleared current index')
    );
    controls.close.click();
    return result;
  });

  run('fresh find dialog Replace All works without Enter', () => {
    const controls = resetFindReplace('banana banana');
    controls.find.value = 'banana';
    controls.replacement.value = 'pear';
    controls.replaceAll.click();
    const result = all(
      equal(snapshot(), 'pear pear', 'replace-all snapshot'),
      equal(findState.query, 'banana', 'synchronized query'),
      equal(findState.matches.length, 0, 'cleared match count'),
      equal(findState.currentIndex, -1, 'cleared current index')
    );
    controls.close.click();
    return result;
  });

  run('find no-match state cannot mutate stale matches', () => {
    const controls = resetFindReplace('alpha beta');
    controls.find.value = 'alpha';
    searchFromField(controls);
    findState.currentIndex = 0;
    controls.find.value = 'gamma';
    searchFromField(controls);
    controls.replacement.value = 'wrong';
    controls.replace.click();
    controls.replaceAll.click();
    const result = all(
      equal(snapshot(), 'alpha beta', 'unchanged snapshot'),
      equal(findState.matches.length, 0, 'match count'),
      equal(findState.currentIndex, -1, 'no-match index'),
      equal(controls.status.textContent, '0 Ersetzungen', 'status')
    );
    controls.close.click();
    return result;
  });

  run('find Next advances and wraps with a consistent selection', () => {
    const controls = resetFindReplace('test test test');
    controls.find.value = 'test';
    searchFromField(controls);
    const first = all(equal(findState.currentIndex, 0, 'initial index'), equal(selectedSearchText(), 'test', 'initial selection'));
    controls.next.click();
    const second = all(equal(findState.currentIndex, 1, 'next index'), equal(selectedSearchText(), 'test', 'next selection'));
    controls.next.click();
    controls.next.click();
    const wrapped = all(equal(findState.currentIndex, 0, 'wrapped index'), equal(selectedSearchText(), 'test', 'wrapped selection'));
    controls.close.click();
    return all(first, second, wrapped);
  });

  run('find Next reports 1/3 2/3 3/3 and wraps to 1/3', () => {
    const controls = resetFindReplace('test test test');
    controls.find.value = 'test';
    searchFromField(controls);
    const first = equal(controls.status.textContent, '1 von 3 Treffern', 'initial status');
    controls.next.click();
    const second = equal(controls.status.textContent, '2 von 3 Treffern', 'second status');
    controls.next.click();
    const third = equal(controls.status.textContent, '3 von 3 Treffern', 'third status');
    controls.next.click();
    const wrapped = equal(controls.status.textContent, '1 von 3 Treffern', 'wrapped status');
    controls.close.click();
    return all(first, second, third, wrapped);
  });

  run('find Previous wraps from 1/3 to 3/3 then moves to 2/3', () => {
    const controls = resetFindReplace('test test test');
    controls.find.value = 'test';
    searchFromField(controls);
    controls.previous.click();
    const wrapped = all(
      equal(findState.currentIndex, 2, 'wrapped previous index'),
      equal(controls.status.textContent, '3 von 3 Treffern', 'wrapped previous status'),
      equal(selectedSearchText(), 'test', 'wrapped previous selection')
    );
    controls.previous.click();
    const second = all(
      equal(findState.currentIndex, 1, 'second previous index'),
      equal(controls.status.textContent, '2 von 3 Treffern', 'second previous status'),
      equal(selectedSearchText(), 'test', 'second previous selection')
    );
    controls.close.click();
    return all(wrapped, second);
  });

  run('find query switch keeps Previous navigation on the visible query', () => {
    const controls = resetFindReplace('apple apple banana banana banana');
    controls.find.value = 'apple';
    searchFromField(controls);
    controls.next.click();
    controls.find.value = 'banana';
    controls.previous.click();
    const result = all(
      equal(findState.query, 'banana', 'synchronized query'),
      equal(findState.matches.length, 3, 'banana match count'),
      equal(findState.currentIndex, 2, 'wrapped banana index'),
      equal(controls.status.textContent, '3 von 3 Treffern', 'banana status'),
      equal(selectedSearchText(), 'banana', 'banana selection')
    );
    controls.close.click();
    return result;
  });

  run('find Replace targets the match selected after Next and Previous', () => {
    const controls = resetFindReplace('apple middle apple');
    controls.find.value = 'apple';
    controls.replacement.value = 'orange';
    searchFromField(controls);
    controls.next.click();
    controls.previous.click();
    controls.replace.click();
    const result = all(
      equal(snapshot(), 'orange middle apple', 'replace snapshot'),
      equal(findState.currentIndex, 0, 'remaining selected index'),
      equal(selectedSearchText(), 'apple', 'remaining selection')
    );
    controls.close.click();
    return result;
  });

  run('find Previous and Next are safe with zero matches', () => {
    const controls = resetFindReplace('alpha beta');
    controls.find.value = 'gamma';
    searchFromField(controls);
    controls.previous.click();
    controls.next.click();
    const result = all(
      equal(snapshot(), 'alpha beta', 'unchanged snapshot'),
      equal(findState.matches.length, 0, 'match count'),
      equal(findState.currentIndex, -1, 'current index'),
      equal(controls.status.textContent, 'Keine Treffer', 'status')
    );
    controls.close.click();
    return result;
  });

  run('find Previous and Next keep one match selected', () => {
    const controls = resetFindReplace('alpha beta');
    controls.find.value = 'beta';
    searchFromField(controls);
    controls.previous.click();
    const previous = all(
      equal(findState.currentIndex, 0, 'previous index'),
      equal(controls.status.textContent, '1 von 1 Treffern', 'previous status'),
      equal(selectedSearchText(), 'beta', 'previous selection')
    );
    controls.next.click();
    const next = all(
      equal(findState.currentIndex, 0, 'next index'),
      equal(controls.status.textContent, '1 von 1 Treffern', 'next status'),
      equal(selectedSearchText(), 'beta', 'next selection')
    );
    controls.close.click();
    return all(previous, next);
  });

  run('find query change resets the old match state', () => {
    const controls = resetFindReplace('apple apple banana');
    controls.find.value = 'apple';
    searchFromField(controls);
    controls.next.click();
    controls.find.value = 'banana';
    searchFromField(controls);
    const result = all(
      equal(findState.query, 'banana', 'query'),
      equal(findState.matches.length, 1, 'match count'),
      equal(findState.currentIndex, 0, 'reset index'),
      equal(selectedSearchText(), 'banana', 'selection'),
      controls.status.textContent.includes('1') || ('status does not report one match: ' + controls.status.textContent)
    );
    controls.close.click();
    return result;
  });

  run('find Replace reduces the remaining match count', () => {
    const controls = resetFindReplace('apple apple apple');
    controls.find.value = 'apple';
    controls.replacement.value = 'orange';
    searchFromField(controls);
    controls.replace.click();
    const result = all(
      equal(snapshot(), 'orange apple apple', 'replace snapshot'),
      equal(findState.matches.length, 2, 'remaining match count'),
      equal(findState.currentIndex, 0, 'remaining current index'),
      equal(selectedSearchText(), 'apple', 'remaining selection')
    );
    controls.close.click();
    return result;
  });

  run('find dialog reopen keeps visible and internal state consistent', () => {
    let controls = resetFindReplace('apple banana');
    controls.find.value = 'banana';
    searchFromField(controls);
    controls.close.click();
    showFindReplace();
    controls = {
      find: document.getElementById('findInput'),
      close: document.getElementById('findClose')
    };
    const result = all(
      equal(controls.find.value, 'banana', 'reopened visible query'),
      equal(findState.query, 'banana', 'reopened internal query'),
      equal(findState.currentIndex, 0, 'reopened current index'),
      equal(findState.matches.length, 1, 'reopened match count')
    );
    controls.close.click();
    return result;
  });

  run('browser-native typing keeps undo and redo', () => {
    setMarkdown('', { focusWritingArea: true });
    focusElement('#editor p');
    const before = snapshot();
    const inserted = document.execCommand('insertText', false, 'Alpha');
    const after = snapshot();
    return all(equal(inserted, true, 'native insert result'), equal(after, 'Alpha', 'typed snapshot'), undoRedo(before, after).check);
  });

  run('programmatic bold is one undoable and redoable change', () => {
    setMarkdown('Alpha Beta', { focusWritingArea: true });
    const before = snapshot();
    selectText('#editor p', 'Alpha');
    runFormat('bold');
    const after = snapshot();
    return all(equal(after, '**Alpha** Beta', 'bold snapshot'), undoRedo(before, after).check);
  });

  run('programmatic italic is one undoable and redoable change', () => {
    setMarkdown('Alpha Beta', { focusWritingArea: true });
    const before = snapshot();
    selectText('#editor p', 'Beta');
    runFormat('italic');
    const after = snapshot();
    return all(equal(after, 'Alpha *Beta*', 'italic snapshot'), undoRedo(before, after).check);
  });

  run('programmatic H2 is one undoable and redoable change', () => {
    setMarkdown('Alpha', { focusWritingArea: true });
    const before = snapshot();
    focusElement('#editor p');
    runFormat('h2');
    const after = snapshot();
    return all(equal(after, '## Alpha', 'heading snapshot'), undoRedo(before, after).check);
  });

  run('Smart Tab code block is one undoable and redoable change', () => {
    setMarkdown('CODE', { focusWritingArea: true });
    const before = snapshot();
    focusElement('#editor p');
    selectionTrace('Smart Tab', 'before action');
    const handled = handleTab(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    const after = snapshot();
    selectionTrace('Smart Tab', 'after action');
    const history = undoRedo(before, after, 'Smart Tab');
    return all(
      equal(handled, true, 'Smart Tab handled'),
      after.includes('\`\`\`\ncode\n\`\`\`') || ('code block missing: ' + JSON.stringify(after)),
      history.check,
      isCollapsedEditorCaret(history.afterUndo) || 'undo left a non-collapsed/additional selection',
      isCollapsedEditorCaret(history.afterRedo) || 'redo left a non-collapsed/additional selection'
    );
  });

  run('Smart Tab code block keeps focus inside the live code element for immediate typing', () => {
    setMarkdown('Intro paragraph\n\nCODE', { focusWritingArea: true });
    const codeParagraph = Array.from(editor.querySelectorAll('p')).find(node => node.textContent.trim() === 'CODE');
    if (!codeParagraph) return 'CODE paragraph missing before Smart Tab: ' + editor.innerHTML;
    placeCaret(codeParagraph);
    rememberSelection();
    const handled = handleTab(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    const code = editor.querySelector('pre code');
    if (!code) return 'code element missing after Smart Tab: ' + editor.innerHTML;
    const selection = getSelection();
    if (!selection.rangeCount) return 'selection missing after Smart Tab';
    const range = selection.getRangeAt(0);
    const selectionInsideCode = code.contains(range.startContainer) && code.contains(range.endContainer);
    const inserted = document.execCommand('insertText', false, 'typed-in-code');
    const markdown = snapshot();
    return all(
      equal(handled, true, 'Smart Tab handled'),
      selectionInsideCode || ('selection not inside live code element: ' + JSON.stringify(selectionTrace('Smart Tab immediate typing', 'after action'))),
      equal(inserted, true, 'native insert result'),
      markdown.includes('Intro paragraph') || 'intro paragraph was replaced',
      markdown.includes('\`\`\`\ntyped-in-code\n\`\`\`') || ('typed text did not replace the code placeholder: ' + JSON.stringify(markdown))
    );
  });

  run('native history input events normalize Smart Tab undo and redo selection', () => {
    setMarkdown('CODE', { focusWritingArea: true });
    focusElement('#editor p');
    handleTab(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    const after = snapshot();
    const undoChanged = document.execCommand('undo');
    editor.dispatchEvent(new InputEvent('input', { inputType: 'historyUndo', bubbles: true }));
    const afterUndo = selectionTrace('Smart Tab native history', 'after undo input event');
    const redoChanged = document.execCommand('redo');
    editor.dispatchEvent(new InputEvent('input', { inputType: 'historyRedo', bubbles: true }));
    const afterRedo = selectionTrace('Smart Tab native history', 'after redo input event');
    return all(
      equal(undoChanged, true, 'native undo result'),
      equal(redoChanged, true, 'native redo result'),
      equal(snapshot(), after, 'native redo snapshot'),
      isCollapsedEditorCaret(afterUndo) || 'native undo input left a non-collapsed/additional selection',
      isCollapsedEditorCaret(afterRedo) || 'native redo input left a non-collapsed/additional selection'
    );
  });

  const tableMarkdown = '| A | B |\n| --- | --- |\n| 1 | 2 |\n| 3 | 4 |';
  run('table row add is undoable and redoable', () => {
    setMarkdown(tableMarkdown, { focusWritingArea: true });
    const before = snapshot();
    focusElement('#editor tbody td');
    selectionTrace('Table row add', 'before action');
    equal(addTableRow(), true, 'add row result');
    const after = snapshot();
    const tablePreservedAfterAction = !!editor.querySelector('table');
    selectionTrace('Table row add', 'after action');
    const history = undoRedo(before, after, 'Table row add');
    return all(
      after !== before || 'row add did not change markdown',
      tablePreservedAfterAction || 'row add flattened the table DOM',
      history.check,
      !!editor.querySelector('table') || 'redo did not restore a table DOM',
      isCollapsedEditorCaret(history.afterUndo) || 'undo left a non-collapsed/additional selection',
      isCollapsedEditorCaret(history.afterRedo) || 'redo left a non-collapsed/additional selection'
    );
  });

  run('table row delete is undoable and redoable', () => {
    setMarkdown(tableMarkdown, { focusWritingArea: true });
    const before = snapshot();
    focusElement('#editor tbody td');
    equal(deleteTableRow(), true, 'delete row result');
    const after = snapshot();
    return all(after !== before || 'row delete did not change markdown', undoRedo(before, after).check);
  });

  run('table column add is undoable and redoable', () => {
    setMarkdown(tableMarkdown, { focusWritingArea: true });
    const before = snapshot();
    focusElement('#editor tbody td');
    selectionTrace('Table column add', 'before action');
    equal(addTableColumn(), true, 'add column result');
    const after = snapshot();
    const tablePreservedAfterAction = !!editor.querySelector('table');
    selectionTrace('Table column add', 'after action');
    const history = undoRedo(before, after, 'Table column add');
    return all(
      after !== before || 'column add did not change markdown',
      tablePreservedAfterAction || 'column add flattened the table DOM',
      history.check,
      !!editor.querySelector('table') || 'redo did not restore a table DOM',
      isCollapsedEditorCaret(history.afterUndo) || 'undo left a non-collapsed/additional selection',
      isCollapsedEditorCaret(history.afterRedo) || 'redo left a non-collapsed/additional selection'
    );
  });

  run('table column delete is undoable and redoable', () => {
    setMarkdown(tableMarkdown, { focusWritingArea: true });
    const before = snapshot();
    focusElement('#editor tbody td');
    equal(deleteTableColumn(), true, 'delete column result');
    const after = snapshot();
    return all(after !== before || 'column delete did not change markdown', undoRedo(before, after).check);
  });

  run('redo selection cannot turn follow-up typing into formatted document replacement', () => {
    setMarkdown('Heading\n\nBold plain\n\nCODE\n\n' + tableMarkdown + '\n\nTail marker', { focusWritingArea: true });
    focusElement('#editor p');
    runFormat('h2');
    selectionTrace('Format leak sequence', 'after H2');
    selectText('#editor p', 'Bold');
    runFormat('bold');
    selectionTrace('Format leak sequence', 'after Bold');
    const codeParagraph = Array.from(editor.querySelectorAll('p')).find(node => node.textContent.trim() === 'CODE');
    if (!codeParagraph) return 'CODE paragraph missing before Smart Tab: ' + editor.innerHTML;
    placeCaret(codeParagraph);
    rememberSelection();
    handleTab(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    selectionTrace('Format leak sequence', 'after Smart Tab');
    focusElement('#editor tbody td');
    const beforeTable = snapshot();
    addTableRow();
    const afterTable = snapshot();
    const tablePreservedAfterAction = !!editor.querySelector('table');
    const history = undoRedo(beforeTable, afterTable, 'Format leak sequence');
    const beforeTyping = snapshot();
    const marker = 'plain-follow-up-marker';
    const inserted = document.execCommand('insertText', false, marker);
    const afterTyping = snapshot();
    const walker = document.createTreeWalker(editor, NodeFilter.SHOW_TEXT);
    let markerNode = null;
    let node;
    while ((node = walker.nextNode())) {
      if ((node.nodeValue || '').includes(marker)) { markerNode = node; break; }
    }
    const inherited = markerNode && markerNode.parentElement
      ? markerNode.parentElement.closest('h1,h2,h3,strong,b,em,i,code,pre,table,thead,tbody,tr,th,td')
      : null;
    selectionTrace('Format leak sequence', 'after plain typing');
    return all(
      history.check,
      tablePreservedAfterAction || 'table action flattened the table DOM',
      !!editor.querySelector('table') || 'table redo did not restore a table DOM',
      !editor.querySelector('h1 p,h1 table,h1 pre,h2 p,h2 table,h2 pre,h3 p,h3 table,h3 pre') || 'block content became nested inside a heading',
      isCollapsedEditorCaret(history.afterRedo) || 'redo left a non-collapsed/additional selection before typing',
      equal(inserted, true, 'native insert result'),
      beforeTyping.includes('Tail marker') || 'precondition lost tail marker',
      afterTyping.includes('Tail marker') || 'follow-up typing replaced an unexpectedly large document range',
      markerNode !== null || 'follow-up marker not found',
      inherited === null || 'follow-up marker inherited ' + describeNode(inherited) + ': ' + inherited.outerHTML.slice(0, 500)
    );
  });

  run('table dialog accepts only complete decimal integers in range', () => {
    const originalColsType = tableColsInput.type;
    const originalRowsType = tableRowsInput.type;
    tableColsInput.type = 'text';
    tableRowsInput.type = 'text';
    const cases = [
      ['1', '1', true], ['12', '50', true], [' 3 ', ' 4 ', true],
      ['0', '2', false], ['13', '2', false], ['1.5', '2', false],
      ['2', '2.5', false], ['1e1', '2', false], ['', '2', false],
      ['abc', '2', false], ['-1', '2', false], ['2', '51', false]
    ];
    try {
      for (const [cols, rows, expected] of cases) {
        tableColsInput.value = cols;
        tableRowsInput.value = rows;
        const actual = validateTableSize(false);
        if (actual !== expected) return 'values ' + JSON.stringify([cols, rows]) + ': expected ' + expected + ', got ' + actual;
      }
      return true;
    } finally {
      tableColsInput.type = originalColsType;
      tableRowsInput.type = originalRowsType;
    }
  });

  document.documentElement.setAttribute('data-interaction-results', encodeURIComponent(JSON.stringify(results)));
  document.documentElement.setAttribute('data-interaction-diagnostics', encodeURIComponent(JSON.stringify(diagnostics)));
})();
</script>`;

const tempRoot = mkdtempSync(join(tmpdir(), 'markdown-studio-interaction-'));
try {
  const source = readFileSync(editorPath, 'utf8');
  const bodyEnd = source.lastIndexOf('</body>');
  if (bodyEnd < 0) throw new Error(`No </body> marker in ${editorPath}`);
  const testPage = join(tempRoot, 'editor-interaction.test.html');
  writeFileSync(testPage, source.slice(0, bodyEnd) + injectedTestScript + '\n' + source.slice(bodyEnd), 'utf8');

  const dump = execFileSync(browser, [
    '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
    `--user-data-dir=${join(tempRoot, 'browser-profile')}`, '--dump-dom', pathToFileURL(testPage).href
  ], { encoding: 'utf8', timeout: 110_000, maxBuffer: 16 * 1024 * 1024, windowsHide: true });

  const match = dump.match(/data-interaction-results="([^"]+)"/);
  if (!match) throw new Error('Browser completed without embedded interaction results.');
  const results = JSON.parse(decodeURIComponent(match[1].replaceAll('&amp;', '&')));
  const diagnosticsMatch = dump.match(/data-interaction-diagnostics="([^"]+)"/);
  const diagnostics = diagnosticsMatch
    ? JSON.parse(decodeURIComponent(diagnosticsMatch[1].replaceAll('&amp;', '&')))
    : [];
  let failed = 0;
  for (const result of results) {
    if (result.pass) console.log(`PASS ${result.name}`);
    else {
      failed++;
      console.error(`FAIL ${result.name}: ${result.detail}`);
    }
  }
  if (diagnostics.length) {
    console.log('\nSELECTION DIAGNOSTICS');
    for (const entry of diagnostics) console.log(JSON.stringify(entry));
  }
  console.log(`\n${results.length - failed}/${results.length} passed`);
  process.exitCode = failed ? 1 : 0;
} finally {
  if (process.env.MSP_KEEP_TEST_TEMP) console.error(`Test files kept at ${tempRoot}`);
  else rmSync(tempRoot, { recursive: true, force: true });
}
