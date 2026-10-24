using BCrypt.Net;             // install BCrypt.Net-Next NuGet package
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OmsCafe;
using OmsCafe.Data;        // adjust to your namespace
using OmsCafe.Models;
using OmsCafe.Services;    // LoginAttemptService, ContactRules

namespace OmsCafe.Pages
{
    public class LoginModel : PageModel
    {
        // ── Registration settings ─────────────────────────────────
        // true  = new accounts are created INACTIVE; an administrator must activate them
        //         (your sign-in already blocks inactive users).
        // false = new accounts can sign in immediately.
        public const bool RequireApproval = true;

        // Role given to every self-registered account. Set on the SERVER only,
        // the browser can never choose its own role. This role must exist in your Roles table.
        private const string DefaultRoleName = "Cashier";

        private const int PasswordMinLength = 8;
        private const int PasswordMaxLength = 64;   // BCrypt only uses the first 72 bytes

        private const int UsernameMaxLength = 30;
        private static readonly Regex HasUpper = new("[A-Z]", RegexOptions.Compiled);
        private static readonly Regex HasLower = new("[a-z]", RegexOptions.Compiled);
        private static readonly Regex HasNumber = new("[0-9]", RegexOptions.Compiled);
        private static readonly Regex HasSpecial = new(@"[^A-Za-z0-9\s]", RegexOptions.Compiled);

        private readonly AppDbContext _db;
        private readonly LoginAttemptService _attempts;

        public LoginModel(AppDbContext db, LoginAttemptService attempts)
        {
            _db = db;
            _attempts = attempts;
        }

        public void OnGet() { }

        // ═════════════════════════════════════════════════════════
        //  SIGN IN  (3 attempts, then 30-minute lockout)
        // ═════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostLoginAsync(
            [FromBody] LoginRequest body)
        {
            if (body == null ||
                string.IsNullOrWhiteSpace(body.Username) ||
                string.IsNullOrWhiteSpace(body.Password))
            {
                return new JsonResult(new { success = false, message = "Please enter your username and password." });
            }

            var username = body.Username.Trim();

            // Lock key = username + IP
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var key = $"{username.ToLowerInvariant()}|{ip}";

            // 1) Already locked? Reject BEFORE touching the database / checking the password
            if (_attempts.IsLockedOut(key, out var remaining))
                return Locked(remaining);

            // 2) Look up the user and verify the password
            var user = await _db.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username == username);

            if (user == null || !BCrypt.Net.BCrypt.Verify(body.Password, user.PasswordHash))
            {
                // 3) Wrong credentials -> count the failure
                var result = _attempts.RegisterFailure(key);

                if (result.IsLockedOut)
                    return Locked(result.LockedFor);

                var left = result.AttemptsLeft;
                return new JsonResult(new
                {
                    success = false,
                    locked = false,
                    attemptsLeft = left,
                    message = $"Invalid username or password. {left} {(left == 1 ? "attempt" : "attempts")} left before sign-in is locked for 30 minutes."
                });
            }

            // Correct password but the account can't be used yet (not counted as a failed attempt)
            if (user.IsArchived)
            {
                return new JsonResult(new { success = false, message = "This account has been archived. Contact your administrator." });
            }

            if (user.ApprovalStatus == AccountApproval.Pending)
            {
                return new JsonResult(new { success = false, message = "Your account is waiting for administrator approval. You can sign in once it has been approved." });
            }

            if (user.ApprovalStatus == AccountApproval.Rejected)
            {
                return new JsonResult(new { success = false, message = "Your registration was not approved. Contact your administrator." });
            }

            if (!user.IsActive)
            {
                return new JsonResult(new { success = false, message = "Your account has been deactivated. Contact your administrator." });
            }

            // Success -> clear the failed-attempt counter
            _attempts.Reset(key);

            // Store session
            HttpContext.Session.SetString("UserId", user.UserId.ToString());
            HttpContext.Session.SetString("FullName", user.FullName);
            HttpContext.Session.SetString("Username", user.Username);
            HttpContext.Session.SetString("Role", user.Role?.RoleName ?? "");

            // Redirect based on role
            var redirect = (user.Role?.RoleName?.ToLower()) switch
            {
                "admin" => "/Admin/AccountManagement",
                "cashier" => "/Cashier/Dashboard",
                "barista" => "/Cashier/Dashboard",
                _ => "/Cashier/Dashboard"
            };

            return new JsonResult(new { success = true, redirect });
        }

        // ═════════════════════════════════════════════════════════
        //  FORGOT PASSWORD  (request goes to an administrator)
        // ═════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostForgotAsync([FromBody] ForgotRequest body)
        {
            if (body == null)
                return Fail("Please fill in the form.");

            var username = (body.Username ?? "").Trim();
            var password = body.Password ?? "";
            var confirm = body.ConfirmPassword ?? "";

            if (username.Length == 0)
                return Fail("Enter your username.", "username");

            var problem = GetPasswordProblem(password);
            if (problem != null)
                return Fail(problem, "password");

            if (password != confirm)
                return Fail("Passwords do not match.", "confirmPassword");

            // Hash first, so the response takes the same time whether or not the username exists
            var hash = BCrypt.Net.BCrypt.HashPassword(password);

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);

            if (user != null && !user.IsArchived && user.IsActive &&
                user.ApprovalStatus == AccountApproval.Approved)
            {
                // One open request per account; repeated clicks don't pile up
                var hasPending = await _db.PasswordResetRequests.AnyAsync(r =>
                    r.UserId == user.UserId && r.Status == PasswordResetStatus.Pending);

                if (!hasPending)
                {
                    _db.PasswordResetRequests.Add(new PasswordResetRequest
                    {
                        RequestId = Guid.NewGuid(),
                        UserId = user.UserId,
                        NewPasswordHash = hash,
                        RequestedAt = PhTime.Now,
                        Status = PasswordResetStatus.Pending
                    });
                    await _db.SaveChangesAsync();
                }
            }

            // Same answer in every case (no username guessing)
            return new JsonResult(new
            {
                success = true,
                message = "Request sent. If that username exists, an administrator will review it. Your new password works once it is approved. Until then, keep using your current one."
            });
        }

        // ═════════════════════════════════════════════════════════
        //  CREATE ACCOUNT
        // ═════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostRegisterAsync(
            [FromBody] RegisterRequest body)
        {
            if (body == null)
                return Fail("Please fill in the form.");

            var fullName = (body.FullName ?? "").Trim();
            var contact = (body.ContactNumber ?? "").Trim();
            var address = (body.Address ?? "").Trim();
            var password = body.Password ?? "";
            var confirm = body.ConfirmPassword ?? "";

            // ── Validate (the browser checks too, but the server is the real gate) ──
            if (fullName.Length < 2 || fullName.Length > 100)
                return Fail("Enter your full name (2 to 100 characters).", "fullName");

            // Contact number: exactly 11 digits, numbers only
            var contactProblem = ContactRules.GetContactProblem(contact);
            if (contactProblem != null)
                return Fail(contactProblem, "contactNumber");

            // Address: 5 to 250 characters
            var addressProblem = ContactRules.GetAddressProblem(address);
            if (addressProblem != null)
                return Fail(addressProblem, "address");

            var passwordProblem = GetPasswordProblem(password);
            if (passwordProblem != null)
                return Fail(passwordProblem, "password");

            if (password != confirm)
                return Fail("Passwords do not match.", "confirmPassword");

            // ── Role is decided by the server, never by the browser ──
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName.ToLower() == DefaultRoleName.ToLower());
            if (role == null)
                return Fail("Account creation is unavailable right now. Contact your administrator.");

            // ── Username is generated by the server (the browser can't choose or change it) ──
            User? user = null;
            for (var attempt = 0; attempt < 3 && user == null; attempt++)
            {
                var candidate = new User
                {
                    UserId = Guid.NewGuid(),
                    FullName = fullName,
                    ContactNumber = contact,
                    Address = address,
                    Username = await GenerateUsernameAsync(fullName),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                    IsActive = !RequireApproval,
                    ApprovalStatus = RequireApproval ? AccountApproval.Pending : AccountApproval.Approved,
                    CreatedAt = PhTime.Now,
                    RoleId = role.RoleId
                };

                _db.Users.Add(candidate);

                try
                {
                    await _db.SaveChangesAsync();
                    user = candidate;
                }
                catch (DbUpdateException)
                {
                    // Someone took the same username a moment ago -> generate the next free one and retry
                    _db.Entry(candidate).State = EntityState.Detached;
                }
            }

            if (user == null)
                return Fail("Could not create the account. Please try again.");

            return new JsonResult(new
            {
                success = true,
                username = user.Username,
                message = RequireApproval
                    ? $"Account created. Your username is {user.Username}. An administrator must approve it before you can sign in."
                    : $"Account created. Your username is {user.Username}. You can sign in now."
            });
        }

        // GET ?handler=Username&name=Juan Dela Cruz  -> live preview shown (read-only) on the form
        public async Task<IActionResult> OnGetUsernameAsync(string? name)
        {
            var clean = (name ?? "").Trim();
            if (clean.Length < 2)
                return new JsonResult(new { username = "" });

            return new JsonResult(new { username = await GenerateUsernameAsync(clean) });
        }

        // Format: firstname.lastname  (juan.cruz). If taken: juan.cruz2, juan.cruz3 ...
        private async Task<string> GenerateUsernameAsync(string fullName)
        {
            var baseName = BuildBaseUsername(fullName);

            var existing = await _db.Users
                .Where(u => u.Username.ToLower().StartsWith(baseName))
                .Select(u => u.Username.ToLower())
                .ToListAsync();

            var taken = new HashSet<string>(existing);

            var candidate = baseName;
            for (var n = 2; taken.Contains(candidate); n++)
                candidate = baseName + n;

            return candidate;
        }

        private static string BuildBaseUsername(string fullName)
        {
            var parts = fullName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(CleanLetters)
                .Where(p => p.Length > 0)
                .ToList();

            var name = parts.Count switch
            {
                0 => "user",
                1 => parts[0],
                _ => parts[0] + "." + parts[^1]      // first name + last word of the name
            };

            // leave room for a number suffix (juan.cruz12) within the 30-character limit
            if (name.Length > UsernameMaxLength - 4)
                name = name[..(UsernameMaxLength - 4)].TrimEnd('.');

            return name;
        }

        // "José Peña" -> "josepena": drops accents, spaces, symbols and digits
        private static string CleanLetters(string text)
        {
            var sb = new StringBuilder();
            foreach (var c in text.Normalize(NormalizationForm.FormD))
            {
                if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
                    sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        // Returns null when the password is OK, otherwise a message for the first broken rule
        private static string? GetPasswordProblem(string password)
        {
            if (password.Length < PasswordMinLength)
                return $"Password must be at least {PasswordMinLength} characters.";
            if (password.Length > PasswordMaxLength || Encoding.UTF8.GetByteCount(password) > 72)
                return $"Password is too long (maximum {PasswordMaxLength} characters).";
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

        private static JsonResult Fail(string message, string? field = null)
            => new(new { success = false, field, message });

        // HTTP 429 + Retry-After; the page script reads retryAfterSeconds to run the countdown
        private IActionResult Locked(TimeSpan remaining)
        {
            var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
            Response.Headers["Retry-After"] = seconds.ToString();

            return new JsonResult(new
            {
                success = false,
                locked = true,
                retryAfterSeconds = seconds,
                message = "Too many failed attempts. Sign-in is locked for security."
            })
            { StatusCode = StatusCodes.Status429TooManyRequests };
        }
    }

    public class LoginRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class ForgotRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string ConfirmPassword { get; set; } = "";
    }

    public class RegisterRequest
    {
        public string FullName { get; set; } = "";
        public string ContactNumber { get; set; } = "";
        public string Address { get; set; } = "";
        public string Password { get; set; } = "";
        public string ConfirmPassword { get; set; } = "";
    }
}