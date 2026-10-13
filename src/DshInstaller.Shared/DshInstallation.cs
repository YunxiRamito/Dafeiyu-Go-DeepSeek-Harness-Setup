namespace DshInstaller.Shared
{
    public enum DshInstallationKind
    {
        Npm,
        OfficialDesktop,
    }

    /// <summary>A validated installation. Official desktop roots require a separate migration flow.</summary>
    public sealed class DshInstallation
    {
        public DshInstallationKind Kind { get; init; }
        public string Version { get; init; }
        public string Root { get; init; }
        public string DataHome { get; init; }

        /// <summary>Npm: JavaScript entry point; official desktop: bundled dsh.cmd shim.</summary>
        public string CliPath { get; init; }
    }
}
