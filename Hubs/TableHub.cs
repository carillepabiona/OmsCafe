using Microsoft.AspNetCore.SignalR;

namespace OmsCafe.Hubs
{
    public class TableHub : Hub
    {
        // Called by the POS page to join the "tables" group
        public async Task JoinTablesDashboard()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "tables-dashboard");
        }

        // Called by the menu page to join a specific table group
        public async Task JoinTable(string tableNumber)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"table-{tableNumber}");
        }
    }
}