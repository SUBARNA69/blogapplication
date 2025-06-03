using System.ComponentModel.DataAnnotations;

namespace blogapplication.Models.DTO
{
    public class BlogCreateViewModel
    {
        [Required(ErrorMessage = "Title is required")]
        [StringLength(200, ErrorMessage = "Title cannot exceed 200 characters")]
        public string Title { get; set; }

        [Required(ErrorMessage = "Content is required")]
        [StringLength(10000, ErrorMessage = "Content cannot exceed 10,000 characters")]
        public string Content { get; set; }

        [Display(Name = "Blog Image")]
        public IFormFile? ImageFile { get; set; }
    }

    public class BlogEditViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Title is required")]
        [StringLength(200, ErrorMessage = "Title cannot exceed 200 characters")]
        public string Title { get; set; }

        [Required(ErrorMessage = "Content is required")]
        [StringLength(10000, ErrorMessage = "Content cannot exceed 10,000 characters")]
        public string Content { get; set; }

        [Display(Name = "Blog Image")]
        public IFormFile? ImageFile { get; set; }

        public string? ExistingImage { get; set; }
    }
}