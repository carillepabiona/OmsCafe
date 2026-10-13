using BCrypt.Net;             // install BCrypt.Net-Next NuGet package
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using OmsCafe.Data;        // adjust to your namespace

namespace OmsCafe.Pages
{
    public class LoginModel : PageModel
    {
        private readonly AppDbContext _db;

        public LoginModel(AppDbContext db)
        {
            _db = db;
        }

        public void OnGet() { }

        public async Task<IActionResult> OnPostLoginAsync(
            [FromBody] LoginRequest body)
        {
            if (string.IsNullOrWhiteSpace(body.Username) ||
                string.IsNullOrWhiteSpace(body.Password))
            {
                return new JsonResult(new { success = false, message = "Please enter your username and password." });
            }

            var user = await _db.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username == body.Username);

            if (user == null || !BCrypt.Net.BCrypt.Verify(body.Password, user.PasswordHash))
            {
                return new JsonResult(new { success = false, message = "Invalid username or password." });
            }

            if (!user.IsActive)
            {
                return new JsonResult(new { success = false, message = "Your account has been deactivated. Contact your administrator." });
            }

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
    }

    public class LoginRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }
}