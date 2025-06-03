using Microsoft.EntityFrameworkCore;
using blogapplication.Models; // Assuming you have a User model in this namespace
namespace blogapplication.Data
{
    public class ApplicationDbContext : DbContext 
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) 
        { 
        }
        public DbSet<User> Users { get; set; } // Assuming you have a User model
        public DbSet<Blog> Blogs { get; set; } // Assuming you have a Blog model
        public DbSet<Comment> Comments { get; set; } // Assuming you have a Post model
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Blog → User (keep cascade delete if you want blogs deleted with user)
            modelBuilder.Entity<Blog>()
                .HasOne(b => b.User)
                .WithMany(u => u.Blogs)
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.Cascade); // or restrict if preferred

            // Comment → Blog (keep cascade if you want comments deleted with blog)
            modelBuilder.Entity<Comment>()
                .HasOne(c => c.Blog)
                .WithMany(b => b.Comments)
                .HasForeignKey(c => c.BlogId)
                .OnDelete(DeleteBehavior.Cascade);

            // Comment → User (NO cascade delete to prevent multiple cascade path)
            modelBuilder.Entity<Comment>()
                .HasOne(c => c.User)
                .WithMany(u => u.Comments)
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Restrict); // ← This line fixes the issue
        }

    }
}
