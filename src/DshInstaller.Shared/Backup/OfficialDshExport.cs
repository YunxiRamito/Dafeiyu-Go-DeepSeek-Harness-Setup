using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DshInstaller.Shared.Backup.Official;

namespace DshInstaller.Shared.Backup
{
    public sealed class OfficialDshExportResult
    {
        public bool Ok { get; set; }
        public bool Canceled { get; set; }
        public string OutputPath { get; set; }
        public string Error { get; set; }
        public string Summary { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public static class OfficialDshExport
    {
        public const string GroupCredentials = "official-credentials";
        public const string GroupAttachments = "official-attachments";
        public const string GroupCompatibility = "official-compatibility";
        public const string GroupSharedSkills = "official-shared-skills";
        public static string ResolveHome(string dshRoot, string dshHome = null) => Migration.ResolveHome(dshRoot, dshHome);

        public static List<BackupGroup> Describe(string dshRoot, string dshHome = null)
        {
            return Migration.Describe(dshRoot, dshHome).Select(g => new BackupGroup
            {
                Id = g.Id, NameZh = g.Name, NameEn = g.EnglishName, SourcePath = g.Path,
                ArchivePrefix = g.Id, ArchivePrefixes = new[] { g.Id }, Exists = true, SelectedByDefault = g.Default
            }).ToList();
        }

        public static OfficialDshExportResult Export(string dshRoot, string dshHome, IList<BackupGroup> chosen,
            string outputExe, Action<string, double> progress, Action<string> log, CancellationToken token)
        {
            Outcome r = Migration.Export(dshRoot, dshHome,
                chosen == null ? null : chosen.Where(g => g != null).Select(g => g.Id).ToList(), outputExe, progress, log, token);
            return new OfficialDshExportResult { Ok = r.Ok, Canceled = r.Canceled, OutputPath = r.OutputPath,
                Error = r.Error, Summary = r.Summary, Warnings = r.Warnings };
        }
    }
}
