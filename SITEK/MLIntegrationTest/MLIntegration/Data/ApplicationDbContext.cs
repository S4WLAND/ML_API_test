using Microsoft.EntityFrameworkCore;
using MLIntegration.Models.MercadoLibre;

namespace MLIntegration.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<MLToken> MLTokens { get; set; }
        public DbSet<MLProduct> MLProducts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<MLToken>(entity =>
            {
                entity.ToTable("MLTokens");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.UserId).IsUnique();
                entity.Property(e => e.AccessToken).IsRequired();
                entity.Property(e => e.RefreshToken).IsRequired();
            });

            modelBuilder.Entity<MLProduct>(entity =>
            {
                entity.ToTable("MLProducts");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.ItemId).IsUnique();
                entity.Property(e => e.Price).HasColumnType("decimal(18,2)");
            });
        }
    }
}