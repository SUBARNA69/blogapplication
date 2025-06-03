using blogapplication.Services;
using Tesseract;
using System.Text.RegularExpressions;

namespace blogapplication.Services
{
    public class TesseractOcrService : IOcrService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<TesseractOcrService> _logger;
        private readonly string _tessDataPath;

        public TesseractOcrService(IWebHostEnvironment environment, ILogger<TesseractOcrService> logger)
        {
            _environment = environment;
            _logger = logger;
            _tessDataPath = Path.Combine(_environment.ContentRootPath, "tessdata");
        }

        public async Task<OcrResult> ExtractTextFromImageAsync(string imagePath)
        {
            try
            {
                var fullImagePath = Path.Combine(_environment.WebRootPath, imagePath.TrimStart('/'));

                if (!File.Exists(fullImagePath))
                {
                    _logger.LogError("Image file not found: {ImagePath}", fullImagePath);
                    return new OcrResult { IsSuccessful = false };
                }

                // Try both languages
                var results = new List<OcrResult>();

                // Try English first
                var englishResult = await ExtractWithLanguageAsync(fullImagePath, "eng");
                if (englishResult.IsSuccessful)
                    results.Add(englishResult);

                // Try Nepali
                var nepaliResult = await ExtractWithLanguageAsync(fullImagePath, "nep");
                if (nepaliResult.IsSuccessful)
                    results.Add(nepaliResult);

                // Return the result with highest confidence
                var bestResult = results.OrderByDescending(r => r.Confidence).FirstOrDefault();
                return bestResult ?? new OcrResult { IsSuccessful = false };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during OCR processing for image: {ImagePath}", imagePath);
                return new OcrResult { IsSuccessful = false };
            }
        }

        private async Task<OcrResult> ExtractWithLanguageAsync(string imagePath, string language)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var engine = new TesseractEngine(_tessDataPath, language, EngineMode.Default);
                    using var img = Pix.LoadFromFile(imagePath);
                    using var page = engine.Process(img);

                    var text = page.GetText();
                    var confidence = page.GetMeanConfidence();

                    return new OcrResult
                    {
                        ExtractedText = text,
                        Language = language,
                        Confidence = confidence,
                        IsSuccessful = !string.IsNullOrWhiteSpace(text) && confidence > 0.3f
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error extracting text with language {Language}", language);
                    return new OcrResult { IsSuccessful = false };
                }
            });
        }

        public async Task<(string? name, string? idNumber)> ExtractNameAndIdAsync(string imagePath)
        {
            var ocrResult = await ExtractTextFromImageAsync(imagePath);

            if (!ocrResult.IsSuccessful)
                return (null, null);

            var extractedText = ocrResult.ExtractedText;
            _logger.LogInformation("Extracted text: {Text}", extractedText);

            // Extract name and ID based on language
            if (ocrResult.Language == "nep")
            {
                return ExtractNepaliNameAndId(extractedText);
            }
            else
            {
                return ExtractEnglishNameAndId(extractedText);
            }
        }

        private (string? name, string? idNumber) ExtractEnglishNameAndId(string text)
        {
            string? name = null;
            string? idNumber = null;

            // Clean and normalize the text
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                           .Select(line => line.Trim())
                           .Where(line => !string.IsNullOrWhiteSpace(line))
                           .ToArray();

            // Extract ID Number - Keep your existing logic as it works well
            var citizenshipPatterns = new[]
            {
                @"(?:Citizenship\s*(?:Certificate\s*)?(?:No|Number|#)?[:\s]*)?(\d{2,3}[-/]?\d{2,3}[-/]?\d{2,8}[-/]?\d{0,8})",
                @"(?:ID\s*(?:No|Number|#)?[:\s]*)?(\d{2,3}[-/]?\d{2,3}[-/]?\d{2,8}[-/]?\d{0,8})",
                @"(\d{2,3}[-/]?\d{2,3}[-/]?\d{2,8}[-/]?\d{2,8})",
                @"(\d{2,3}[-/]?\d{2,3}[-/]?\d{2,8})"
            };

            foreach (var pattern in citizenshipPatterns)
            {
                var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);
                if (matches.Count > 0)
                {
                    var longestMatch = matches.Cast<Match>()
                                            .OrderByDescending(m => m.Groups[1].Value.Length)
                                            .First();
                    idNumber = longestMatch.Groups[1].Value.Trim();
                    break;
                }
            }

            // Improved Name Extraction
            name = ExtractNameFromText(text, lines);

            _logger.LogInformation("OCR Extraction - Raw text lines: {Lines}", string.Join(" | ", lines.Take(10)));
            _logger.LogInformation("OCR Extraction - Found name: '{Name}', ID: '{Id}'", name, idNumber);

            return (name, idNumber);
        }

        private string? ExtractNameFromText(string fullText, string[] lines)
        {
            // Method 1: Look for "Full Name:" pattern first
            var fullNamePatterns = new[]
            {
                @"Full\s*Name\s*[:\s]+([A-Za-z\s]{2,50})",
                @"Name\s*[:\s]+([A-Za-z\s]{2,50})",
                @"पूरा\s*नाम\s*[:\s]+([A-Za-z\s]{2,50})" // Nepali "Full Name"
            };

            foreach (var pattern in fullNamePatterns)
            {
                var match = Regex.Match(fullText, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                if (match.Success)
                {
                    var extractedName = CleanName(match.Groups[1].Value);
                    if (IsValidName(extractedName))
                    {
                        return extractedName;
                    }
                }
            }

            // Method 2: Look for names in typical positions (often after header info)
            var potentialNames = new List<(string name, int score)>();

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Skip obvious non-name lines
                if (ShouldSkipLine(line))
                    continue;

                // Look for name patterns in each line
                var namePatterns = new[]
                {
                    @"^([A-Z][A-Z\s]{5,50})$", // All caps: "SUBARNA GURAGAI"
                    @"^([A-Z][a-z]+(?:\s+[A-Z][a-z]+)+)$", // Title case: "Subarna Guragai"
                    @"([A-Z][A-Z\s]+[A-Z])(?:\s|$)", // Caps with possible trailing text
                    @"([A-Z][a-z]+\s+[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)" // Mixed case names
                };

                foreach (var pattern in namePatterns)
                {
                    var matches = Regex.Matches(line, pattern);
                    foreach (Match match in matches)
                    {
                        var candidateName = CleanName(match.Groups[1].Value);
                        if (IsValidName(candidateName))
                        {
                            int score = CalculateNameScore(candidateName, line, i, lines.Length);
                            potentialNames.Add((candidateName, score));
                        }
                    }
                }
            }

            // Method 3: If no clear patterns found, look for any sequence of capitalized words
            if (potentialNames.Count == 0)
            {
                var allCapitalizedWords = Regex.Matches(fullText, @"\b[A-Z][A-Za-z]*\b")
                    .Cast<Match>()
                    .Select(m => m.Value)
                    .Where(word => word.Length > 2 && !IsCommonNonNameWord(word))
                    .ToArray();

                // Try to find sequences of 2-3 capitalized words that could be names
                for (int i = 0; i < allCapitalizedWords.Length - 1; i++)
                {
                    var twoWordName = $"{allCapitalizedWords[i]} {allCapitalizedWords[i + 1]}";
                    if (IsValidName(twoWordName))
                    {
                        potentialNames.Add((twoWordName, 3)); // Lower score for this method
                    }

                    if (i < allCapitalizedWords.Length - 2)
                    {
                        var threeWordName = $"{allCapitalizedWords[i]} {allCapitalizedWords[i + 1]} {allCapitalizedWords[i + 2]}";
                        if (IsValidName(threeWordName))
                        {
                            potentialNames.Add((threeWordName, 4)); // Slightly higher for three words
                        }
                    }
                }
            }

            // Return the highest scored name
            return potentialNames.OrderByDescending(x => x.score).FirstOrDefault().name;
        }

        private string CleanName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // Remove extra whitespace and normalize
            name = Regex.Replace(name.Trim(), @"\s+", " ");

            // Remove common OCR artifacts
            name = name.Replace(":", "").Replace(",", "").Replace(".", "").Trim();

            return name;
        }

        private bool IsValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length < 4)
                return false;

            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Must have at least 2 words (first and last name)
            if (words.Length < 2)
                return false;

            // Each word should be reasonable length
            if (words.Any(w => w.Length < 2 || w.Length > 25))
                return false;

            // Should not contain numbers
            if (name.Any(char.IsDigit))
                return false;

            // Should not be common non-name phrases
            var upperName = name.ToUpper();
            var blacklistedPhrases = new[]
            {
                "GOVERNMENT", "CERTIFICATE", "CITIZENSHIP", "NEPAL", "ISSUED",
                "FULL NAME", "DATE", "BIRTH", "ADDRESS", "DISTRICT", "MUNICIPALITY",
                "WARD", "SEX", "MALE", "FEMALE", "FOLLOWING", "DETAILS", "HAS ISSUED"
            };

            return !blacklistedPhrases.Any(phrase => upperName.Contains(phrase));
        }

        private bool ShouldSkipLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 4)
                return true;

            var lowerLine = line.ToLower();
            var skipPhrases = new[]
            {
                "government", "certificate", "citizenship", "issued", "following",
                "details", "date", "birth", "address", "district", "municipality",
                "ward no", "sex:", "male", "female", "permanent address"
            };

            return skipPhrases.Any(phrase => lowerLine.Contains(phrase));
        }

        private bool IsCommonNonNameWord(string word)
        {
            var commonWords = new[]
            {
                "GOVERNMENT", "OF", "NEPAL", "HAS", "ISSUED", "THIS", "CITIZENSHIP",
                "CERTIFICATE", "WITH", "FOLLOWING", "DETAILS", "FULL", "NAME",
                "DATE", "BIRTH", "SEX", "MALE", "FEMALE", "ADDRESS", "DISTRICT",
                "MUNICIPALITY", "WARD", "NO", "THE", "AND", "FOR", "FROM"
            };

            return commonWords.Contains(word.ToUpper());
        }

        private int CalculateNameScore(string name, string line, int lineIndex, int totalLines)
        {
            int score = 0;

            // Prefer names that appear in middle sections of the document
            if (lineIndex > 0 && lineIndex < totalLines - 1)
                score += 2;

            // Prefer all-caps names (common in official documents)
            if (name == name.ToUpper())
                score += 3;

            // Prefer names with 2-3 words
            var wordCount = name.Split(' ').Length;
            if (wordCount == 2)
                score += 4;
            else if (wordCount == 3)
                score += 3;

            // Prefer names that are the main content of their line
            if (line.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                score += 3;

            // Prefer longer names (more specific)
            if (name.Length > 10)
                score += 1;

            return score;
        }

        private (string? name, string? idNumber) ExtractNepaliNameAndId(string text)
        {
            string? name = null;
            string? idNumber = null;

            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                           .Select(line => line.Trim())
                           .Where(line => !string.IsNullOrWhiteSpace(line))
                           .ToArray();

            // Extract ID (keep existing logic)
            var citizenshipPatterns = new[]
            {
                @"(\d{2,3}[-/]?\d{2,3}[-/]?\d{2,8}[-/]?\d{2,8})",
                @"(\d{2,3}[-/]?\d{2,3}[-/]?\d{2,8})"
            };

            foreach (var pattern in citizenshipPatterns)
            {
                var matches = Regex.Matches(text, pattern);
                if (matches.Count > 0)
                {
                    var longestMatch = matches.Cast<Match>()
                                            .OrderByDescending(m => m.Groups[1].Value.Length)
                                            .First();
                    idNumber = longestMatch.Groups[1].Value.Trim();
                    break;
                }
            }

            // For Nepali documents, try to extract English names first (they're often present)
            name = ExtractNameFromText(text, lines);

            // If no English name found, look for Devanagari script
            if (name == null)
            {
                foreach (var line in lines)
                {
                    if (line.Any(c => c >= 'A' && c <= 'Z') ||
                        line.Contains("Government") ||
                        line.Contains("Nepal") ||
                        line.Contains("Certificate") ||
                        line.Length < 3)
                    {
                        continue;
                    }

                    var nepaliNamePattern = @"[\u0900-\u097F\s]{3,50}";
                    var nepaliMatch = Regex.Match(line, nepaliNamePattern);
                    if (nepaliMatch.Success)
                    {
                        var extractedName = nepaliMatch.Value.Trim();
                        if (extractedName.Length > 2 && extractedName.Split(' ').Length >= 2)
                        {
                            name = extractedName;
                            break;
                        }
                    }
                }
            }

            return (name, idNumber);
        }
    }
}