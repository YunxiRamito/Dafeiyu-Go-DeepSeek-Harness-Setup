using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DshInstaller;
using DshInstaller.Shared.Backup.Official;
using DeepSeekHarnessLauncher.Backup;

string root = Environment.GetEnvironmentVariable("DAFEIYU_IMPORT_TEST_ROOT") ?? throw new InvalidOperationException("Set isolated test root.");
root = Path.GetFullPath(root);
if (!root.Contains("Temp_SetupandLauncher", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Use the G: QA root.");
string fixture = Path.Combine(root, "source-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
void Write(string relative, string text) { string path = Path.Combine(fixture, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text); }
void Reject(Action action, string name) { try { action(); } catch (Exception e) when (e is IOException || e is InvalidDataException || e is ArgumentException || e is OperationCanceledException) { Check(true, name); return; } throw new Exception("Expected rejection: " + name); }
Dictionary<string,string> Snapshot() => Directory.EnumerateFiles(fixture, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(fixture,p), p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
string MakeArchive(string name, Dictionary<string, (string Group,string Content)> files)
{
    using var payload = new MemoryStream();
    var manifest = new PayloadManifest();
    using (var zip = new ZipArchive(payload, ZipArchiveMode.Create, true, Encoding.UTF8))
    {
        foreach (var (target, data) in files)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(data.Content);
            using (var entry = zip.CreateEntry("data/"+target).Open()) entry.Write(bytes);
            manifest.Files.Add(new PayloadFile { Path="data/"+target, Target=target, Group=data.Group, Length=bytes.Length, Hash=Protocol.Hex(SHA256.HashData(bytes)) });
        }
        using var output = zip.CreateEntry("manifest.json").Open();
        Protocol.ManifestWrite(output, manifest);
    }
    byte[] packed = payload.ToArray(); string path = Path.Combine(fixture,name);
    using var file = File.Create(path); file.Write(new byte[] { (byte)'M',(byte)'Z' }); file.Write(packed); Protocol.Footer(file,packed.Length,SHA256.HashData(packed)); return path;
}
try
{
    string staging = Path.Combine(fixture,"stage");
    foreach (string path in new[] { "plain", "install/.dsh", "extracted/home", "expanded/data/home" })
    {
        Write(path+"/sessions/chat/session.jsonl", """{"title":"retained"}""");
        var before=Snapshot();
        string selected = path=="install/.dsh" ? Path.Combine(fixture,"install") : path.Split('/')[0];
        using (var source=ImportSource.Open(Path.Combine(fixture,selected), staging, CancellationToken.None))
        {
            Check(source.Home==Path.Combine(fixture,path.Replace('/',Path.DirectorySeparatorChar)),"recognizes "+path);
            Check(DshDataImportService.Preview(source.ImportRoot,Path.Combine(fixture,"target-"+checks),"desktop","web",CancellationToken.None).Files.Count==1,"previews "+path);
        }
        Check(before.OrderBy(x=>x.Key).SequenceEqual(Snapshot().OrderBy(x=>x.Key)),"source unchanged "+path);
    }
    Directory.CreateDirectory(Path.Combine(fixture,"empty"));
    Reject(()=>ImportSource.Open(Path.Combine(fixture,"empty"),staging,CancellationToken.None),"unknown folder rejects without changing it");
    string archive=MakeArchive("migration.exe", new() {
        ["home/sessions/chat/a.jsonl"]=("sessions","chat fixture"),
        ["home/attachments/v1/item.txt"]=("official-attachments","attachment bytes"),
        ["home/.credentials.yaml"]=("official-credentials","private fixture"),
        ["home/profiles/desktop/package.json"]=("plugins","""{"dependencies":{},"dsh":{"profile":{"bundles":[]}}}"""),
        ["agents/skills/shared/SKILL.md"]=("official-shared-skills","shared skill") });
    string archiveHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))), stagedHome;
    using(var source=ImportSource.Open(archive,staging,CancellationToken.None))
    {
        stagedHome=source.Home;
        Check(source.DisplayPath==archive && File.Exists(Path.Combine(source.Home,"sessions/chat/a.jsonl")),"migration EXE is validated and read as data");
        Check(!File.Exists(Path.Combine(source.Home,".credentials.yaml")),"root credentials are excluded from staging");
        Check(source.Warnings.Any(w => w.Contains("credentials")),"excluded credentials are explicitly explained to the user");
        Check(File.ReadAllText(Path.Combine(source.Home,"skills/shared/SKILL.md"))=="shared skill","shared skills map into importable home");
        var plan=DshDataImportService.Preview(source.ImportRoot,Path.Combine(fixture,"import-target"),"desktop","web",CancellationToken.None);
        var result=DshDataImportService.Import(plan,new[]{"sessions","skills","plugins"},null,CancellationToken.None);
        Check(result.Ok && result.Imported==3,"staged data imports using existing safe core");
    }
    Check(!Directory.Exists(stagedHome),"staged source is cleaned on dispose");
    Check(archiveHash==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))),"selected EXE remains byte-for-byte unchanged");
    Write("ordinary.exe","not a migration");
    Reject(()=>ImportSource.Open(Path.Combine(fixture,"ordinary.exe"),staging,CancellationToken.None),"ordinary EXE is rejected and never executed");
    byte[] damaged=File.ReadAllBytes(archive); damaged[10]^=1; File.WriteAllBytes(Path.Combine(fixture,"damaged.exe"),damaged);
    Reject(()=>ImportSource.Open(Path.Combine(fixture,"damaged.exe"),staging,CancellationToken.None),"tampered archive rejects before staging");
    Reject(()=>ImportSource.Open(archive,staging,new CancellationToken(true)),"pre-cancel is honored");
    Check(!Directory.EnumerateDirectories(staging).Any(),"rejections leave no staged source");
    double reported = 0;
    var progressPlan = DshDataImportService.Preview(Path.Combine(fixture,"plain"),Path.Combine(fixture,"progress-target"),"desktop","web",CancellationToken.None,
        progress: (_, percent) => reported = percent);
    Check(Double.IsNaN(reported) && progressPlan.Files.Count==1,"production preview reports NaN for byte totals that are not known yet");
    Console.WriteLine($"PASS {checks} import-source checks; only synthetic fixtures, no user data or executable launch.");
}
finally { if(Directory.Exists(fixture)) Directory.Delete(fixture,true); }
