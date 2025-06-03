using System.Drawing;

namespace blogapplication.Services
{
    public interface IOcrService
    {
        Task<OcrResult> ExtractTextFromImageAsync(string imagePath);
        Task<(string? name, string? idNumber)> ExtractNameAndIdAsync(string imagePath);
    }

    public class OcrResult
    {
        public string ExtractedText { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public float Confidence { get; set; }
        public bool IsSuccessful { get; set; }
    }
}
