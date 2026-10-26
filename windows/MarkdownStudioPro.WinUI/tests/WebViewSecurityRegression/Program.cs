using MarkdownStudioPro.WinUI;
var doc = new Uri(Path.GetFullPath(Path.Combine(Path.GetTempPath(),"msp-editor","editor.html"))).AbsoluteUri;
var checks = new (string Name, string? Candidate, bool Expected)[] {
 ("exact editor document",doc,true),
 ("query string cannot substitute editor",doc+"?inject=1",false),
 ("fragment on same document allowed",doc+"#heading",true),
 ("other file forbidden",new Uri(Path.Combine(Path.GetTempPath(),"other.html")).AbsoluteUri,false),
 ("remote HTTP forbidden","https://example.org",false),
 ("remote https forbidden","https://example.org/?a=b",false),
 ("javascript forbidden","javascript:alert(1)",false),
 ("empty forbidden",null,false),
 ("case folding path",doc.ToUpperInvariant(),true)
};
var failures=0;
foreach(var c in checks) {
 var got=WebViewSecurityPolicy.IsTrustedDocumentUri(doc,c.Candidate);
 if (got!=c.Expected) { failures++; Console.Error.WriteLine($"FAIL {c.Name}: expected={c.Expected} actual={got}"); }
 else Console.WriteLine("PASS "+c.Name);
}
Console.WriteLine($"{checks.Length-failures}/{checks.Length} passed");
return failures==0?0:1;
