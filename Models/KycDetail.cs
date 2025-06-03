using System.ComponentModel.DataAnnotations;

namespace blogapplication.Models
{
    public class KycDetail
    {
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public string IdCardImagePath { get; set; } = string.Empty;

        [Required]
        public string FaceImagePath { get; set; } = string.Empty;

        public string? ExtractedName { get; set; }

        public string? ExtractedCitizenshipNumber { get; set; }

        [Required]
        public string UserEnteredName { get; set; } = string.Empty;

        [Required]
        public string UserEnteredId { get; set; } = string.Empty;

        [Required]
        public string Status { get; set; } = "Pending"; // Pending, Half-Verified, Verified, Rejected

        public string? AdminComment { get; set; }

        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

        public bool OcrProcessed { get; set; } = false;

        public bool NameMatch { get; set; } = false;
        public DateTime? ProcessedAt { get; set; }
        public bool IdMatch { get; set; } = false;

        // Navigation property
        public virtual User User { get; set; } = null!;
    }
}
