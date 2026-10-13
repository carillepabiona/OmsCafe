using Microsoft.EntityFrameworkCore;
using OmsCafe.Models;

namespace OmsCafe.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }

        public DbSet<Category> Categories => Set<Category>();
        public DbSet<MenuItem> MenuItems => Set<MenuItem>();
        public DbSet<Customization> Customizations => Set<Customization>();
        public DbSet<CustomizationOption> CustomizationOptions => Set<CustomizationOption>();
        public DbSet<Addon> Addons => Set<Addon>();
        public DbSet<MenuItemCustomization> MenuItemCustomizations => Set<MenuItemCustomization>();
        public DbSet<MenuItemAddon> MenuItemAddons => Set<MenuItemAddon>();


        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }

        public DbSet<StaffNotification> StaffNotifications { get; set; }

        public DbSet<CafeTable> Tables { get; set; }

        public DbSet<Payment> Payments { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // MenuItemCustomization composite key
            modelBuilder.Entity<MenuItemCustomization>()
                .HasKey(x => new { x.MenuItemId, x.CustomizationId });

            modelBuilder.Entity<MenuItemCustomization>()
                .HasOne(x => x.MenuItem)
                .WithMany(m => m.MenuItemCustomizations)
                .HasForeignKey(x => x.MenuItemId);

            modelBuilder.Entity<MenuItemCustomization>()
                .HasOne(x => x.Customization)
                .WithMany(c => c.MenuItemCustomizations)
                .HasForeignKey(x => x.CustomizationId);

            // MenuItemAddon composite key
            modelBuilder.Entity<MenuItemAddon>()
                .HasKey(x => new { x.MenuItemId, x.AddonId });

            modelBuilder.Entity<MenuItemAddon>()
                .HasOne(x => x.MenuItem)
                .WithMany(m => m.MenuItemAddons)
                .HasForeignKey(x => x.MenuItemId);

            modelBuilder.Entity<MenuItemAddon>()
                .HasOne(x => x.Addon)
                .WithMany(a => a.MenuItemAddons)
                .HasForeignKey(x => x.AddonId);

            // CustomizationOption FK
            modelBuilder.Entity<CustomizationOption>()
                .HasOne(o => o.Customization)
                .WithMany(c => c.Options)
                .HasForeignKey(o => o.CustomizationId);

            // MenuItem → Category
            modelBuilder.Entity<MenuItem>()
                .HasOne(m => m.Category)
                .WithMany(c => c.MenuItems)
                .HasForeignKey(m => m.CategoryId);

            // Decimal precision
            modelBuilder.Entity<MenuItem>()
                .Property(m => m.Price).HasColumnType("decimal(10,2)");
            modelBuilder.Entity<MenuItem>()
                .Property(m => m.Cost).HasColumnType("decimal(10,2)");
            modelBuilder.Entity<CustomizationOption>()
                .Property(o => o.PriceModifier).HasColumnType("decimal(10,2)");
            modelBuilder.Entity<Addon>()
                .Property(a => a.Price).HasColumnType("decimal(10,2)");

           /* modelBuilder.Entity<Order>()
                .Property(o => o.DiscountAmount).HasColumnType("decimal(10,2)");*/

            // Soft-delete filter for Categories
            modelBuilder.Entity<Category>()
                .HasQueryFilter(c => !c.IsDeleted);

            modelBuilder.Entity<StaffNotification>().ToTable("StaffNotifications");
        }
    }

}
