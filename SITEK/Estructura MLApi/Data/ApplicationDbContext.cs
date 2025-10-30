using Microsoft.EntityFrameworkCore;
using YourProject.Models.MercadoLibre;

namespace YourProject.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<MLToken> MLTokens { get; set; }
        public DbSet<MLProduct> MLProducts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configuración de MLToken
            modelBuilder.Entity<MLToken>(entity =>
            {
                entity.ToTable("MLTokens");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.UserId).IsUnique();
                entity.Property(e => e.AccessToken).IsRequired();
                entity.Property(e => e.RefreshToken).IsRequired();
                entity.Property(e => e.ExpiresAt).IsRequired();
            });

            // Configuración de MLProduct
            modelBuilder.Entity<MLProduct>(entity =>
            {
                entity.ToTable("MLProducts");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.ItemId).IsUnique();
                entity.HasIndex(e => e.UserId);
                entity.Property(e => e.Price).HasColumnType("decimal(18,2)");
            });
        }
    }
}