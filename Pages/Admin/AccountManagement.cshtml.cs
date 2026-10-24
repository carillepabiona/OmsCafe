using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmsCafe;
using OmsCafe.Data;
using OmsCafe.Models;
using OmsCafe.Services;   // PasswordPolicy, ContactRules

public class AccountManagementModel : PageModel
{
    private readonly AppDbContext _db;

    public AccountManagementModel(AppDbContext db)
    {
        _db = db;
    }

    // ── Data for the page ───────────────────────────────────────
    public List<User> Users { get; set; } = new();          // rows of the table (active list OR archived list)
    public List<User> PendingUsers { get; set; } = new();   // waiting for approval
    public List<PasswordResetRequest> PendingResets { get; set; } = new();   // password reset requests
    public List<Role> Roles { get; set; } = new();

    // Stat cards (approved, not archived accounts only)
    public int TotalAccounts { get; set; }
    public int ActiveCount { get; set; }
    public int InactiveCount { get; set; }
    public int AdminCount { get; set; }
    public int NewThisMonth { get; set; }

    public int ArchivedCount { get; set; }
    public bool ShowArchived { get; set; }

    public string? AddError { get; set; }

    // Messages that survive a redirect
    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    // Paging (for whichever list is showing)
    public int ListTotal { get; set; }
    public int CurrentPage { get; set; } = 1;     // plain property: the value is bound by the "pageNumber" parameter of OnGetAsync
    public int TotalPages { get; set; }
    public int PageSize { get; set; } = 5;
    public int PageStart => ListTotal == 0 ? 0 : (CurrentPage - 1) * PageSize + 1;
    public int PageEnd => Math.Min(CurrentPage * PageSize, ListTotal);
    private static DateTime Today => PhTime.Today;

    // ── Admin-only guard ────────────────────────────────────────
    // Without this, anyone who can reach the page (including a freshly self-registered
    // account) could post to Approve / Edit / Archive. Remove it only if you already
    // protect /Admin/* somewhere else (middleware or a filter).
    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        var role = HttpContext.Session.GetString("Role");
        if (!string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new RedirectResult("/Login");
            return;
        }
        base.OnPageHandlerExecuting(context);
    }

    // ── GET ─────────────────────────────────────────────────────
    // The parameter is "pageNumber" (not "page"): "page" is a reserved route value in Razor Pages.
    public async Task OnGetAsync(int pageNumber = 1, string? view = null)
    {
        ShowArchived = string.Equals(view, "archived", StringComparison.OrdinalIgnoreCase);

        Roles = await _db.Roles
            .Where(r => !r.IsDeleted)
            .OrderBy(r => r.RoleName)
            .ToListAsync();

        // Live accounts = approved and not archived
        var live = _db.Users.Where(u => !u.IsArchived && u.ApprovalStatus == AccountApproval.Approved);

        var monthStart = new DateTime(Today.Year, Today.Month, 1);

        TotalAccounts = await live.CountAsync();
        ActiveCount = await live.CountAsync(u => u.IsActive);
        InactiveCount = TotalAccounts - ActiveCount;
        AdminCount = await live.CountAsync(u => u.Role!.RoleName == "Admin");
        NewThisMonth = await live.CountAsync(u => u.CreatedAt >= monthStart);
        ArchivedCount = await _db.Users.CountAsync(u => u.IsArchived);

        PendingUsers = await _db.Users
            .Include(u => u.Role)
            .Where(u => !u.IsArchived && u.ApprovalStatus == AccountApproval.Pending)
            .OrderBy(u => u.CreatedAt)
            .ToListAsync();

        // Password reset requests waiting for an administrator
        PendingResets = await _db.PasswordResetRequests
            .Include(r => r.User)
            .Where(r => r.Status == PasswordResetStatus.Pending
                     && r.User != null
                     && !r.User.IsArchived)
            .OrderBy(r => r.RequestedAt)
            .ToListAsync();

        // The table shows either the live accounts or the archive
        IQueryable<User> list = ShowArchived
            ? _db.Users.Where(u => u.IsArchived)
            : live;

        list = list.Include(u => u.Role);

        ListTotal = await list.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(ListTotal / (double)PageSize));
        CurrentPage = Math.Clamp(pageNumber, 1, TotalPages);   // handles ?pageNumber=0 or 999

        list = ShowArchived
            ? list.OrderByDescending(u => u.ArchivedAt)
            : list.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.FullName);

        Users = await list
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }

    // Returns null when the contact details are OK, otherwise a message for the first problem.
    // Rules live in ContactRules so the Login page and this page always agree:
    //   contact number = exactly 11 digits (numbers only), address = 5 to 250 characters.
    private static string? GetContactProblem(string contact, string address)
        => ContactRules.GetContactProblem(contact)
           ?? ContactRules.GetAddressProblem(address);

    // ── ADD ─────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostAddAsync(
        string FullName,
        string Username,
        Guid RoleId,
        bool IsActive,
        string? ContactNumber,
        string? Address,
        string? Password,
        string? ConfirmPassword)
    {
        FullName = (FullName ?? "").Trim();
        Username = (Username ?? "").Trim();
        var contact = (ContactNumber ?? "").Trim();
        var address = (Address ?? "").Trim();

        if (FullName.Length == 0 || Username.Length == 0)
            return await AddFailed("Full name and username are required.");

        var contactProblem = GetContactProblem(contact, address);
        if (contactProblem != null)
            return await AddFailed(contactProblem);

        var passwordProblem = PasswordPolicy.GetProblem(Password);
        if (passwordProblem != null)
            return await AddFailed(passwordProblem);

        if (Password != ConfirmPassword)
            return await AddFailed("Passwords do not match.");

        if (await _db.Users.AnyAsync(u => u.Username.ToLower() == Username.ToLower()))
            return await AddFailed("Username already exists.");

        if (!await _db.Roles.AnyAsync(r => r.RoleId == RoleId && !r.IsDeleted))
            return await AddFailed("Please select a valid role.");

        var user = new User
        {
            UserId = Guid.NewGuid(),
            FullName = FullName,
            Username = Username,
            RoleId = RoleId,
            IsActive = IsActive,
            ContactNumber = contact,
            Address = address,
            ApprovalStatus = AccountApproval.Approved,     // created by an admin = already approved
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
            CreatedAt = PhTime.Now
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        SuccessMessage = $"Account for {user.FullName} was created.";
        return RedirectToPage();
    }

    private async Task<IActionResult> AddFailed(string message)
    {
        AddError = message;
        await OnGetAsync();
        return Page();
    }

    // ── EDIT ────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostEditAsync(
        Guid UserId,
        string FullName,
        string Username,
        Guid RoleId,
        bool IsActive,
        string? ContactNumber,
        string? Address,
        string? Password,
        string? ConfirmPassword)
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user == null) return NotFound();

        if (user.IsArchived || user.ApprovalStatus != AccountApproval.Approved)
        {
            ErrorMessage = "Only approved, non-archived accounts can be edited.";
            return RedirectToPage();
        }

        FullName = (FullName ?? "").Trim();
        Username = (Username ?? "").Trim();
        var contact = (ContactNumber ?? "").Trim();
        var address = (Address ?? "").Trim();

        if (FullName.Length == 0 || Username.Length == 0)
        {
            ErrorMessage = "Full name and username are required.";
            return RedirectToPage();
        }

        var contactProblem = GetContactProblem(contact, address);
        if (contactProblem != null)
        {
            ErrorMessage = contactProblem;
            return RedirectToPage();
        }

        // Check username not taken by someone else
        if (await _db.Users.AnyAsync(u => u.Username.ToLower() == Username.ToLower() && u.UserId != UserId))
        {
            ErrorMessage = "Username already exists.";
            return RedirectToPage();
        }

        // Password is optional on edit, but if one is typed it must follow the rules
        var changingPassword = !string.IsNullOrEmpty(Password);
        if (changingPassword)
        {
            var problem = PasswordPolicy.GetProblem(Password);
            if (problem != null)
            {
                ErrorMessage = problem;
                return RedirectToPage();
            }

            if (Password != ConfirmPassword)
            {
                ErrorMessage = "Passwords do not match.";
                return RedirectToPage();
            }
        }

        user.FullName = FullName;
        user.Username = Username;
        user.RoleId = RoleId;
        user.IsActive = IsActive;
        user.ContactNumber = contact;
        user.Address = address;

        if (changingPassword)
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password);

        await _db.SaveChangesAsync();

        SuccessMessage = $"Changes to {user.FullName} were saved.";
        return RedirectToPage();
    }

    // ── APPROVE (new account) ───────────────────────────────────
    public async Task<IActionResult> OnPostApproveAsync(Guid UserId)
    {
        var user = await _db.Users.FindAsync(UserId);

        if (user == null || user.IsArchived || user.ApprovalStatus != AccountApproval.Pending)
        {
            ErrorMessage = "That account is no longer waiting for approval.";
            return RedirectToPage();
        }

        user.ApprovalStatus = AccountApproval.Approved;
        user.IsActive = true;
        user.ReviewedAt = PhTime.Now;
        user.ReviewedBy = HttpContext.Session.GetString("Username");

        await _db.SaveChangesAsync();

        SuccessMessage = $"{user.FullName} was approved and can now sign in.";
        return RedirectToPage();
    }

    // ── REJECT (moves the request to the archive) ───────────────
    public async Task<IActionResult> OnPostRejectAsync(Guid UserId)
    {
        var user = await _db.Users.FindAsync(UserId);

        if (user == null || user.IsArchived || user.ApprovalStatus != AccountApproval.Pending)
        {
            ErrorMessage = "That account is no longer waiting for approval.";
            return RedirectToPage();
        }

        user.ApprovalStatus = AccountApproval.Rejected;
        user.IsActive = false;
        user.ReviewedAt = PhTime.Now;
        user.ReviewedBy = HttpContext.Session.GetString("Username");
        user.IsArchived = true;
        user.ArchivedAt = PhTime.Now;

        await _db.SaveChangesAsync();

        SuccessMessage = $"{user.FullName}'s request was rejected and moved to the archive.";
        return RedirectToPage();
    }

    // ── PASSWORD RESET: APPROVE ─────────────────────────────────
    public async Task<IActionResult> OnPostApproveResetAsync(Guid RequestId)
    {
        var req = await _db.PasswordResetRequests
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.RequestId == RequestId);

        if (req == null || req.Status != PasswordResetStatus.Pending ||
            req.User == null || req.User.IsArchived)
        {
            ErrorMessage = "That reset request is no longer pending.";
            return RedirectToPage();
        }

        req.User.PasswordHash = req.NewPasswordHash;   // already a BCrypt hash
        req.Status = PasswordResetStatus.Approved;
        req.ReviewedAt = PhTime.Now;
        req.ReviewedBy = HttpContext.Session.GetString("Username");

        await _db.SaveChangesAsync();

        SuccessMessage = $"{req.User.FullName}'s new password is now active.";
        return RedirectToPage();
    }

    // ── PASSWORD RESET: REJECT ──────────────────────────────────
    public async Task<IActionResult> OnPostRejectResetAsync(Guid RequestId)
    {
        var req = await _db.PasswordResetRequests
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.RequestId == RequestId);

        if (req == null || req.Status != PasswordResetStatus.Pending)
        {
            ErrorMessage = "That reset request is no longer pending.";
            return RedirectToPage();
        }

        req.Status = PasswordResetStatus.Rejected;
        req.ReviewedAt = PhTime.Now;
        req.ReviewedBy = HttpContext.Session.GetString("Username");

        await _db.SaveChangesAsync();

        SuccessMessage = $"Password reset request for {req.User?.FullName ?? "that account"} was rejected.";
        return RedirectToPage();
    }

    // ── ARCHIVE (replaces delete: the record is kept) ───────────
    public async Task<IActionResult> OnPostArchiveAsync(Guid UserId)
    {
        var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == UserId);
        if (user == null || user.IsArchived) return RedirectToPage();

        // Don't let an admin archive themselves
        var currentUserId = HttpContext.Session.GetString("UserId");
        if (currentUserId == user.UserId.ToString())
        {
            ErrorMessage = "You can't archive your own account.";
            return RedirectToPage();
        }

        // Always keep at least one active administrator
        if (user.IsActive && user.Role?.RoleName == "Admin")
        {
            var otherAdmins = await _db.Users.CountAsync(u =>
                u.UserId != user.UserId &&
                !u.IsArchived &&
                u.IsActive &&
                u.ApprovalStatus == AccountApproval.Approved &&
                u.Role!.RoleName == "Admin");

            if (otherAdmins == 0)
            {
                ErrorMessage = "You can't archive the last active administrator.";
                return RedirectToPage();
            }
        }

        user.IsArchived = true;
        user.ArchivedAt = PhTime.Now;
        user.IsActive = false;       // archived accounts can never sign in

        await _db.SaveChangesAsync();

        SuccessMessage = $"{user.FullName} was archived. You can restore the account from the Archived tab.";
        return RedirectToPage();
    }

    // ── RESTORE ─────────────────────────────────────────────────
    public async Task<IActionResult> OnPostRestoreAsync(Guid UserId)
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user == null || !user.IsArchived) return RedirectToPage();

        user.IsArchived = false;
        user.ArchivedAt = null;

        if (user.ApprovalStatus == AccountApproval.Rejected)
        {
            // A rejected request goes back into the approval queue for a new decision
            user.ApprovalStatus = AccountApproval.Pending;
            user.IsActive = false;
            SuccessMessage = $"{user.FullName}'s request is back in the approval queue.";
        }
        else
        {
            // Stays inactive until you switch them to Active in Edit
            user.IsActive = false;
            SuccessMessage = $"{user.FullName} was restored. Set the status to Active in Edit so they can sign in again.";
        }

        await _db.SaveChangesAsync();
        return RedirectToPage();
    }
}