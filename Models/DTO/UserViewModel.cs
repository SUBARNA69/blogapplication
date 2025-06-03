using System.ComponentModel.DataAnnotations;

namespace blogapplication.Models.DTO
{
    public class UserViewModel
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Enter a valid email address")]
        public string Email { get; set; }

        public string? Role { get; set; } // Optional

        [Required(ErrorMessage = "Password is required")]
        public string Password { get; set; }
        public DateTime CreatedAt { get; set; } // Optional, can be set by the server
    }
}
