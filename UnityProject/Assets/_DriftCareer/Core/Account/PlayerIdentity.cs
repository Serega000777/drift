using System;
using System.Text;

namespace DriftCareer.Core.Account
{
    public enum NicknameError { None, TooShort, TooLong, InvalidCharacters, Reserved, LooksLikePersonalData }

    /// <summary>
    /// Public player name rules. Nicknames are shown to others later (leaderboards), so they must not carry
    /// personal data (e-mail, phone) and use a limited character set. Server-side moderation is still required.
    /// </summary>
    public static class PlayerIdentity
    {
        public const int MinLength = 3;
        public const int MaxLength = 20;
        private static readonly string[] Reserved = { "admin", "moderator", "support", "driftcareer", "system", "official", "админ", "модератор", "поддержка" };

        public static NicknameError Validate(string nick)
        {
            if (nick == null) return NicknameError.TooShort;
            string n = nick.Trim();
            if (n.Length < MinLength) return NicknameError.TooShort;
            if (n.Length > MaxLength) return NicknameError.TooLong;
            int digits = 0;
            foreach (char c in n)
            {
                bool ok = char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ' || c == '.';
                if (!ok) return NicknameError.InvalidCharacters;
                if (char.IsDigit(c)) digits++;
            }
            if (n.Contains("@") || digits >= 7) return NicknameError.LooksLikePersonalData; // e-mails, phone numbers
            string lower = n.ToLowerInvariant();
            foreach (var r in Reserved) if (lower.Contains(r)) return NicknameError.Reserved;
            return NicknameError.None;
        }

        /// <summary>Normalizes whitespace; returns null if invalid.</summary>
        public static string Sanitize(string nick)
        {
            if (Validate(nick) != NicknameError.None) return null;
            var sb = new StringBuilder();
            bool space = false;
            foreach (char c in nick.Trim())
            {
                if (c == ' ') { if (!space) sb.Append(c); space = true; }
                else { sb.Append(c); space = false; }
            }
            return sb.ToString();
        }

        /// <summary>Default nickname that reveals nothing about the player ("Drifter-4F2A").</summary>
        public static string DefaultFor(string installId)
        {
            string tail = string.IsNullOrEmpty(installId) ? "0000" : installId.Substring(Math.Max(0, installId.Length - 4)).ToUpperInvariant();
            return "Drifter-" + tail;
        }

        /// <summary>Level from lifetime drift points: each level needs 15% more than the previous (L1 = 5 000 pts).</summary>
        public static int Level(double lifetimePoints, out float progressToNext)
        {
            double need = 5000, acc = 0;
            int level = 1;
            while (lifetimePoints >= acc + need && level < 200)
            {
                acc += need;
                need *= 1.15;
                level++;
            }
            progressToNext = (float)MathUtil.Clamp01((float)((lifetimePoints - acc) / need));
            return level;
        }

        public static string Initials(string nick)
        {
            if (string.IsNullOrWhiteSpace(nick)) return "?";
            var parts = nick.Trim().Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
            return nick.Trim().Substring(0, Math.Min(2, nick.Trim().Length)).ToUpperInvariant();
        }
    }
}
