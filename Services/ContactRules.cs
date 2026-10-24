using System.Text.RegularExpressions;

namespace OmsCafe.Services
{
    // One place for the contact number / address rules.
    // Use it from the Login page AND from AccountManagement so both enforce the same thing.
    public static class ContactRules
    {
        public const int ContactLength = 11;
        public const int AddressMinLength = 5;
        public const int AddressMaxLength = 250;

        // [0-9] on purpose: \d also matches non-ASCII digits in .NET
        private static readonly Regex ElevenDigits =
            new("^[0-9]{11}$", RegexOptions.Compiled);

        public static bool IsValidContact(string? value)
            => value != null && ElevenDigits.IsMatch(value);

        // Returns null when OK, otherwise the message to show
        public static string? GetContactProblem(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Enter your contact number.";
            if (!IsValidContact(value))
                return "Contact number must be exactly 11 digits (numbers only).";
            return null;
        }

        // Returns null when OK, otherwise the message to show
        public static string? GetAddressProblem(string? value)
        {
            var text = (value ?? "").Trim();
            if (text.Length == 0)
                return "Enter your address.";
            if (text.Length < AddressMinLength)
                return $"Address must be at least {AddressMinLength} characters.";
            if (text.Length > AddressMaxLength)
                return $"Address is too long (maximum {AddressMaxLength} characters).";
            return null;
        }
    }
}