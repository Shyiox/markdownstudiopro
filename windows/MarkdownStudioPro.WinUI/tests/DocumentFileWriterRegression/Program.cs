using System.Security.Cryptography;
using System.Text;
using MarkdownStudioPro.WinUI;

var tests = new (string Name, Func<Task> Test)[] {
 ("first write is UTF8 and returns a content fingerprint", WritesFreshFile),
 ("external change blocks overwrite without corrupting document", BlocksExternalChange),
 ("missing tracked file blocks recreation", BlocksMissingFile),
 ("cancelled save keeps original document and cleans scratch file", CancelPreservesOriginal),
 ("completed save leaves no intermediate file", NoLeftoverFiles)
};
var failed = 0;
foreach(var test in tests) {
 try { await test.Test(); Console.WriteLine("PASS " + test.Name); }
 catch(Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static string CreateRoot() {
 var path = Path.Combine(Path.GetTempPath(), "msp-writer-" + Guid.NewGuid().ToString("N"));
 Directory.CreateDirectory(path); return path;
}
static void Same(object? expected, object? actual) {
 if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
}
static async Task WritesFreshFile() {
 var dir=CreateRoot(); try {
   var path=Path.Combine(dir,"new.md");
   var hash=await DocumentFileWriter.WriteAsync(path,"# Grüße 📝\n",null);
   Same("# Grüße 📝\n",await File.ReadAllTextAsync(path));
   Same(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("# Grüße 📝\n"))),hash);
 } finally { Directory.Delete(dir,true); }
}
static async Task BlocksExternalChange() {
 var dir=CreateRoot(); try {
   var path=Path.Combine(dir,"doc.md");
   var fingerprint=await DocumentFileWriter.WriteAsync(path,"First",null);
   await File.WriteAllTextAsync(path,"Changed outside");
   try { await DocumentFileWriter.WriteAsync(path,"Mine",fingerprint); throw new Exception("Lost external modification!"); }
   catch(DocumentWriteConflictException) {}
   Same("Changed outside",await File.ReadAllTextAsync(path));
   Same(1,Directory.GetFiles(dir).Length);
 } finally { Directory.Delete(dir,true); }
}
static async Task BlocksMissingFile() {
 var dir=CreateRoot(); try {
   var path=Path.Combine(dir,"doc.md");
   var fingerprint=await DocumentFileWriter.WriteAsync(path,"first",null);
   File.Delete(path);
   try { await DocumentFileWriter.WriteAsync(path,"mine",fingerprint); throw new Exception("Missing tracked file silently recreated"); }
   catch(DocumentWriteConflictException) {}
   Same(false,File.Exists(path));
 } finally { Directory.Delete(dir,true); }
}
static async Task CancelPreservesOriginal() {
 var dir=CreateRoot(); try {
   var path=Path.Combine(dir,"doc.md");
   var fingerprint=await DocumentFileWriter.WriteAsync(path,"original",null);
   using var cts=new CancellationTokenSource(); cts.Cancel();
   try { await DocumentFileWriter.WriteAsync(path,"new value",fingerprint,cts.Token); throw new Exception("Canceled write did not cancel"); }
   catch(OperationCanceledException) {}
   Same("original",await File.ReadAllTextAsync(path));
   Same(1,Directory.GetFiles(dir).Length);
 } finally { Directory.Delete(dir,true); }
}
static async Task NoLeftoverFiles() {
 var dir=CreateRoot(); try {
   var path=Path.Combine(dir,"doc.md");
   var fingerprint=await DocumentFileWriter.WriteAsync(path,"original",null);
   await DocumentFileWriter.WriteAsync(path,"second",fingerprint);
   Same("second",await File.ReadAllTextAsync(path));
   Same(1,Directory.GetFiles(dir).Length);
 } finally { Directory.Delete(dir,true); }
}
