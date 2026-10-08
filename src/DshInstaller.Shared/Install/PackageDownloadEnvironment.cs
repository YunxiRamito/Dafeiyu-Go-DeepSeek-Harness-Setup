using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace DshInstaller.Shared.Install
{
    public static class PackageDownloadEnvironment
    {
        public static string ResolvePackageSpecifier(string source, bool backend)
        {
            if (!backend || String.IsNullOrWhiteSpace(source)) return source;
            string prefix = String.Empty;
            string value = source.Trim();
            int alias = value.IndexOf("@github:", StringComparison.OrdinalIgnoreCase);
            if (alias < 1) alias = value.IndexOf("@git+https://github.com/", StringComparison.OrdinalIgnoreCase);
            if (alias < 1) alias = value.IndexOf("@https://github.com/", StringComparison.OrdinalIgnoreCase);
            if (alias > 0) { prefix = value.Substring(0, alias + 1); value = value.Substring(alias + 1); }
            string repository = null;
            if (value.StartsWith("github:", StringComparison.OrdinalIgnoreCase)) repository = value.Substring(7);
            else if (value.StartsWith("git+https://github.com/", StringComparison.OrdinalIgnoreCase)) repository = value.Substring(23);
            else if (value.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) repository = value.Substring(19);
            else if (value.IndexOf(':') < 0 && !value.StartsWith("@", StringComparison.Ordinal) && value.Split('#')[0].Split('/').Length == 2)
                repository = value;
            if (repository == null) return source;
            int hash = repository.IndexOf('#');
            string reference = hash < 0 ? String.Empty : repository.Substring(hash);
            string path = (hash < 0 ? repository : repository.Substring(0, hash)).Trim('/');
            if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path.Substring(0, path.Length - 4);
            if (!System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z0-9_-]+/[A-Za-z0-9_.-]+$")) return source;
            return prefix + "git+" + BackendDownloadSource.BaseUrl + "/api/git/" + path + ".git" + reference;
        }

        public static Dictionary<string, string> Create(string sourcePreference)
        {
            if (!BackendDownloadSource.IsSelected(sourcePreference)) return null;
            ProcessStartInfo process = new ProcessStartInfo();
            process.Environment.Clear();
            ApplyBackend(process, BackendDownloadSource.BaseUrl, BackendDownloadSource.CertificateFile());
            return new Dictionary<string, string>(process.Environment, StringComparer.OrdinalIgnoreCase);
        }

        public static void ApplyBackend(ProcessStartInfo process, string baseUrl, string certificateFile)
        {
            string endpoint = baseUrl.TrimEnd('/');
            string[] secrets = { "GITHUB_TOKEN", "GH_TOKEN", "GITHUB_PAT", "GITHUB_AUTH_TOKEN",
                "NODE_AUTH_TOKEN", "NPM_TOKEN", "GIT_ASKPASS", "SSH_ASKPASS", "GIT_CONFIG_PARAMETERS",
                "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy",
                "npm_config_proxy", "npm_config_https_proxy" };
            var inherited = new List<string>(process.Environment.Keys);
            foreach (string key in inherited)
            {
                bool remove = key.StartsWith("GIT_CONFIG_", StringComparison.OrdinalIgnoreCase)
                    && (key.StartsWith("GIT_CONFIG_KEY_", StringComparison.OrdinalIgnoreCase)
                        || key.StartsWith("GIT_CONFIG_VALUE_", StringComparison.OrdinalIgnoreCase)
                        || String.Equals(key, "GIT_CONFIG_COUNT", StringComparison.OrdinalIgnoreCase));
                foreach (string secret in secrets)
                    remove |= String.Equals(key, secret, StringComparison.OrdinalIgnoreCase);
                remove |= key.StartsWith("npm_config_", StringComparison.OrdinalIgnoreCase)
                    && (key.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0
                        || key.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0
                        || key.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0);
                if (remove) process.Environment.Remove(key);
            }
            process.Environment["npm_config_registry"] = endpoint + "/api/npm/";
            process.Environment["npm_config_proxy"] = "";
            process.Environment["npm_config_https_proxy"] = "";
            process.Environment["npm_config_noproxy"] = "202.189.21.218,api.ramirinko.top";
            process.Environment["NO_PROXY"] = "202.189.21.218,api.ramirinko.top";
            process.Environment["npm_config_cafile"] = certificateFile;
            process.Environment["npm_config_strict_ssl"] = "true";
            process.Environment["npm_config_always_auth"] = "false";
            process.Environment["NODE_EXTRA_CA_CERTS"] = certificateFile;
            process.Environment.Remove("NODE_TLS_REJECT_UNAUTHORIZED");
            process.Environment.Remove("GIT_SSL_NO_VERIFY");
            process.Environment["GIT_TERMINAL_PROMPT"] = "0";
            string[] keys = {
                "http." + endpoint + "/.sslCAInfo", "http." + endpoint + "/.sslVerify",
                "url." + endpoint + "/api/git/.insteadOf", "http." + endpoint + "/.extraHeader",
                "credential." + endpoint + "/.helper", "core.askPass"
                , "http." + endpoint + "/.proxy"
            };
            string[] values = { certificateFile, "true", "https://github.com/", "", "", "", "" };
            process.Environment["GIT_CONFIG_COUNT"] = keys.Length.ToString(CultureInfo.InvariantCulture);
            for (int index = 0; index < keys.Length; index++)
            {
                process.Environment["GIT_CONFIG_KEY_" + index] = keys[index];
                process.Environment["GIT_CONFIG_VALUE_" + index] = values[index];
            }
        }

        public static void Apply(ProcessStartInfo process, IDictionary<string, string> environment)
        {
            if (environment == null || !environment.TryGetValue("npm_config_registry", out string registry)
                || !String.Equals(registry.TrimEnd('/'), BackendDownloadSource.BaseUrl + "/api/npm", StringComparison.OrdinalIgnoreCase))
                return;
            // The caller's dictionary cannot delete secrets inherited by ProcessStartInfo itself.
            ApplyBackend(process, BackendDownloadSource.BaseUrl, environment["npm_config_cafile"]);
        }
    }
}
