using System.Text;
using System.Text.RegularExpressions;

namespace OmsCafe.Services
{
    /// <summary>One place for the password rules (used by Add, Edit and Create account).</summary>
    public static class PasswordPolicy
    {
        public const int MinLength = 8;
        public const int MaxLength = 64;   // BCrypt only uses the first 72 bytes

        private static readonly Regex HasUpper = new("[A-Z]", RegexOptions.Compiled);
        private static readonly Regex HasLower = new("[a-z]", RegexOptions.Compiled);
        private static readonly Regex HasNumber = new("[0-9]", RegexOptions.Compiled);
        private static readonly Regex HasSpecial = new(@"[^A-Za-z0-9\s]", RegexOptions.Compiled);

        /// <summary>Returns null when the password is valid, otherwise a message for the first broken rule.</summary>
        public static string? GetProblem(string? password)
        {
            password ??= "";

            if (password.Length < MinLength)
                return $"Password must be at least {MinLength} characters.";
            if (password.Length > MaxLength || Encoding.UTF8.GetByteCount(password) > 72)
                return $"Password is too long (maximum {MaxLength} characters).";
            if (!HasUpper.IsMatch(password))
                return "Password needs at least one uppercase letter (A-Z).";
            if (!HasLower.IsMatch(password))
                return "Password needs at least one lowercase letter (a-z).";
            if (!HasNumber.IsMatch(password))
                return "Password needs at least one number (0-9).";
            if (!HasSpecial.IsMatch(password))
                return "Password needs at least one special character (for example ! @ # $ %).";

            return null;
        }
    }
}