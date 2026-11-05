using System.Text.RegularExpressions;

namespace DriftCareer.Core.Security
{
    /// <summary>
    /// Removes secrets and personal data from log lines before they reach the console, crash reports or files:
    /// bearer/JWT tokens, key=value secrets, e-mail addresses, long hex/base64 blobs and user home paths.
    /// </summary>
    public static class LogRedactor
    {
        private static readonly Regex Jwt = new Regex(@"eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+", RegexOptions.Compiled);
        private static readonly Regex Bearer = new Regex(@"(?i)bearer\s+[A-Za-z0-9\-\._~\+/]+=*", RegexOptions.Compiled);
        private static readonly Regex KeyValue = new Regex(@"(?i)\b(token|access_token|refresh_token|password|passwd|secret|api[_-]?key|session|ticket|auth[_-]?code)\b\s*[:=]\s*[""']?[^\s""'&,;]+", RegexOptions.Compiled);
        private static readonly Regex Email = new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
        private static readonly Regex LongHex = new Regex(@"\b[a-fA-F0-9]{32,}\b", RegexOptions.Compiled);
        private static readonly Regex LongB64 = new Regex(@"(?<![A-Za-z0-9+/])[A-Za-z0-9+/]{40,}={0,2}", RegexOptions.Compiled);
        private static readonly Regex HomePath = new Regex(@"(?i)(/Users/|/home/|C:\\Users\\)[^/\\\s]+", RegexOptions.Compiled);

        public static string Redact(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;
            string s = Jwt.Replace(line, "[jwt]");
            s = Bearer.Replace(s, "Bearer [redacted]");
            s = KeyValue.Replace(s, m => m.Groups[1].Value + "=[redacted]");
            s = Email.Replace(s, "[email]");
            s = LongHex.Replace(s, "[hex]");
            s = LongB64.Replace(s, "[blob]");
            s = HomePath.Replace(s, m => m.Groups[1].Value + "[user]");
            return s;
        }
    }
}
