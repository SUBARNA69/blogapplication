using System.Reflection.Metadata;

namespace blogapplication.Models
{
    public class User
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string? Role { get; set; } // e.g., "Admin", "User"
        public DateTime CreatedAt { get; set; } // e.g., "Admin", "User"
        public ICollection<Blog> Blogs { get; set; }
        public ICollection<Comment> Comments { get; set; }
    }
}
