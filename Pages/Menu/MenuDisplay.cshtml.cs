using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using OmsCafe.Data;
using OmsCafe.Models;

namespace OmsCafe.Pages.Menu
{
    public class MenuDisplayModel : PageModel
    {
        private readonly AppDbContext _db;

        // In MenuDisplayModel.cshtml.cs
        [BindProperty(SupportsGet = true)]
        public required string Table { get; set; }


        public MenuDisplayModel(AppDbContext db)
        {
            _db = db;
        }

        [BindProperty(SupportsGet = true)]
        public string Token { get; set; } = "";
      
        public List<Category> Categories { get; set; } = new();
        public List<MenuItem> MenuItems { get; set; } = new();

        public async Task OnGetAsync(string table, string token)
        {
            Table = table;
            Token = token;
            // If no token in URL and no session cookie fallback, 
            // we let the JS handle it client-side.
            // But store the token in a TempData so JS can read it.
            if (!string.IsNullOrEmpty(Token))
            {
                TempData["qr_token"] = Token;
            }

            Categories = await _db.Categories
                .Where(c => c.IsActive && !c.IsDeleted)
                .OrderBy(c => c.DisplayOrder)
                .ToListAsync();

            MenuItems = await _db.MenuItems
                .Where(m => !m.Category.IsDeleted && m.Category.IsActive)
                .Include(m => m.Category)
                .Include(m => m.MenuItemCustomizations)
                    .ThenInclude(mic => mic.Customization)
                        .ThenInclude(c => c.Options)
                .Include(m => m.MenuItemAddons)
                    .ThenInclude(mia => mia.Addon)
                .OrderBy(m => m.Category.DisplayOrder)
                    .ThenBy(m => m.DisplayOrder)
                .ToListAsync();

            return;
        }
    }
}
