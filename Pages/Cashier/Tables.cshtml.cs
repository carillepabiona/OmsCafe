using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;
using OmsCafe.Models;

namespace OmsCafe.Pages.Cashier
{
    public class TablesModel : PageModel
    {
        private readonly AppDbContext _db;
        public TablesModel(AppDbContext db) => _db = db;

        public List<CafeTable> Tables { get; set; } = new();

        public async Task OnGetAsync()
        {
            Tables = await _db.Tables
                               .OrderBy(t => t.TableNumber)
                               .ToListAsync();
        }
    }
}