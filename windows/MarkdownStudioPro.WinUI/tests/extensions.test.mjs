import { execFileSync } from 'node:child_process';
import { cpSync, existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
const here=dirname(fileURLToPath(import.meta.url));
const app=resolve(here,'..','App');
const sources=[
 process.env.MSP_TEST_BROWSER,
 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
 'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'
].filter(Boolean);
const browser=sources.find(existsSync);
if(!browser){console.error('Missing Edge/Chrome');process.exit(2);}
const script=[
 '<script id="msp-extensions-test">',
 '(() => {',
 ' const fence=String.fromCharCode(96).repeat(3);',
 ' const input=["Text[^one] and $x^2$", "", "[^one]: Erklärung", "", "$$", "\\\\frac{1}{2}", "$$", "", fence+"mermaid", "flowchart TD", " A-->B", fence, "", fence+"js", "const x = 2;", fence].join("\\n");',
 ' window.markdownStudio.setMarkdown(input,{});',
 ' setTimeout(()=>{',
 ' const root=document.getElementById("editor");',
 ' const preview=root.querySelector(".md-mermaid-preview");',
 ' const result={',
 '   katexLoaded:typeof window.katex?.renderToString==="function",',
 '   mermaidLoaded:typeof window.MSPMermaid?.render==="function",',
 '   mathRendered:root.querySelectorAll(".md-inline-math math, .md-math-block math").length>=2 && !root.querySelector(".md-inline-math .katex-html, .md-math-block .katex-html"),',
 '   mermaidState:preview?.dataset.state||"missing",',
 '   mermaidHasDiagram:Boolean(preview?.querySelector("svg")),',
 '   syntaxRanges:CSS.highlights?.get("msp-code-keyword")?.size||0,',
 '   markdownRetained:window.markdownStudio.toMarkdown().includes("flowchart TD") && window.markdownStudio.toMarkdown().includes("const x = 2;") && window.markdownStudio.toMarkdown().includes("Text[^one] and $x^2$"),',
 '   error:preview?.textContent?.slice(0,150)||""',
 ' };',
 ' document.documentElement.setAttribute("data-extension-results",encodeURIComponent(JSON.stringify(result)));',
 ' },2500);',
 '})();',
 '</script>'
].join('\n');
const tmp=mkdtempSync(join(tmpdir(),'msp-extensions-'));
try{
 cpSync(join(app,'vendor'),join(tmp,'vendor'),{recursive:true});
 const html=readFileSync(join(app,'editor.html'),'utf8');
 const at=html.lastIndexOf('</body>');
 const file=join(tmp,'extensions.html');
 writeFileSync(file,html.slice(0,at)+script+html.slice(at),'utf8');
 const output=execFileSync(browser,[
  '--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check',
  '--virtual-time-budget=7500','--user-data-dir='+join(tmp,'profile'),
  '--dump-dom',pathToFileURL(file).href
 ],{encoding:'utf8',timeout:110000,maxBuffer:16*1024*1024,windowsHide:true});
 const matched=output.match(/data-extension-results="([^"]+)"/);
 if(!matched) throw Error('No extension result from browser');
 const result=JSON.parse(decodeURIComponent(matched[1]));
 console.log(JSON.stringify(result));
 if(!result.katexLoaded || !result.mermaidLoaded || !result.mathRendered || !result.mermaidHasDiagram || result.syntaxRanges===0 || !result.markdownRetained) process.exitCode=1;
}finally{rmSync(tmp,{recursive:true,force:true});}
