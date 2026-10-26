import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here=dirname(fileURLToPath(import.meta.url));
const editorPath=resolve(here,'..','App','editor.html');
const browsers=[
 process.env.MSP_TEST_BROWSER,
 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
 'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'
].filter(Boolean);
const browser=browsers.find(existsSync);
if(!browser){ console.error('Edge or Chrome required'); process.exit(2); }
const script=[
 '<script id="perf-test">',
 '(() => {',
 ' const api=window.markdownStudio;',
 ' const rows=["# Benchmark"];',
 ' for(let i=0;i<1200;i++)rows.push("Paragraph "+i+" with **bold**, [link](https://example.org) and text. ");',
 ' const source=rows.join("\\n\\n");',
 ' const t0=performance.now();',
 ' api.setMarkdown(source,{});',
 ' const render=performance.now()-t0;',
 ' const t1=performance.now();',
 ' const result=api.toMarkdown();',
 ' const serialize=performance.now()-t1;',
 ' const passed=result.includes("Paragraph 0 with **bold**") && result.includes("Paragraph 1199 with **bold**") && document.querySelectorAll("#editor p").length>=1200 && render+serialize<15000;',
 ' document.documentElement.setAttribute("data-perf-results",encodeURIComponent(JSON.stringify({passed,renderMs:Math.round(render),serializeMs:Math.round(serialize),paragraphs:document.querySelectorAll("#editor p").length,chars:result.length})));',
 '})();',
 '</script>'
].join('\n');
const temp=mkdtempSync(join(tmpdir(),'msp-perf-'));
try{
 const source=readFileSync(editorPath,'utf8');
 const offset=source.lastIndexOf('</body>');
 if(offset<0) throw Error('no body closing');
 const testPage=join(temp,'performance.html');
 writeFileSync(testPage,source.slice(0,offset)+script+source.slice(offset),'utf8');
 const dump=execFileSync(browser,[
 '--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check',
 '--user-data-dir='+join(temp,'profile'),'--dump-dom',pathToFileURL(testPage).href
 ],{encoding:'utf8',timeout:110000,maxBuffer:16*1024*1024,windowsHide:true});
 const match=dump.match(/data-perf-results="([^"]+)"/);
 if(!match)throw Error('no performance result from browser');
 const result=JSON.parse(decodeURIComponent(match[1]));
 console.log(JSON.stringify(result));
 if(!result.passed)process.exitCode=1;
}finally{ rmSync(temp,{recursive:true,force:true}); }
