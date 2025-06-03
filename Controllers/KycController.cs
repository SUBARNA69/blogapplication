using blogapplication.Data;
using blogapplication.Models;
using blogapplication.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.IO;

namespace blogapplication.Controllers
{
    [Authorize]
    public class KycController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IOcrService _ocrService;
        private readonly ILogger<KycController> _logger;

        public KycController(
            ApplicationDbContext context,
            IWebHostEnvironment webHostEnvironment,
            IOcrService ocrService,
            ILogger<KycController> logger)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _ocrService = ocrService;
            _logger = logger;
        }

        // GET: Kyc
        public async Task<IActionResult> Index()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            // Check if user already has a KYC submission
            var existingKyc = await _context.KycDetails.FirstOrDefaultAsync(k => k.UserId == userId);

            if (existingKyc != null)
            {
                return RedirectToAction(nameof(Status));
            }

            return View();
        }

        // POST: Kyc/Upload
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(IFormFile idCardImage, IFormFile faceImage, string userEnteredName, string userEnteredId)
        {
            try
            {
                // Validation
                if (idCardImage == null || faceImage == null)
                {
                    TempData["ErrorMessage"] = "Please upload both ID card and face images.";
                    return RedirectToAction(nameof(Index));
                }

                if (string.IsNullOrWhiteSpace(userEnteredName) || string.IsNullOrWhiteSpace(userEnteredId))
                {
                    TempData["ErrorMessage"] = "Please enter both your name and ID number.";
                    return RedirectToAction(nameof(Index));
                }

                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                // Check if user already has a KYC submission
                var existingKyc = await _context.KycDetails.FirstOrDefaultAsync(k => k.UserId == userId);
                if (existingKyc != null)
                {
                    TempData["ErrorMessage"] = "You have already submitted KYC documents.";
                    return RedirectToAction(nameof(Status));
                }

                // Validate file types and sizes
                var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png" };
                var maxSize = 5 * 1024 * 1024; // 5MB

                if (!allowedTypes.Contains(idCardImage.ContentType) || !allowedTypes.Contains(faceImage.ContentType))
                {
                    TempData["ErrorMessage"] = "Only JPG, JPEG, and PNG files are allowed.";
                    return RedirectToAction(nameof(Index));
                }

                if (idCardImage.Length > maxSize || faceImage.Length > maxSize)
                {
                    TempData["ErrorMessage"] = "File size must be less than 5MB.";
                    return RedirectToAction(nameof(Index));
                }

                // Create upload directory
                var uploadPath = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "kyc", userId.ToString());
                Directory.CreateDirectory(uploadPath);

                // Save ID card image
                var idCardFileName = $"id_card_{DateTime.UtcNow:yyyyMMddHHmmss}{Path.GetExtension(idCardImage.FileName)}";
                var idCardPath = Path.Combine(uploadPath, idCardFileName);
                using (var stream = new FileStream(idCardPath, FileMode.Create))
                {
                    await idCardImage.CopyToAsync(stream);
                }

                // Save face image
                var faceFileName = $"face_{DateTime.UtcNow:yyyyMMddHHmmss}{Path.GetExtension(faceImage.FileName)}";
                var facePath = Path.Combine(uploadPath, faceFileName);
                using (var stream = new FileStream(facePath, FileMode.Create))
                {
                    await faceImage.CopyToAsync(stream);
                }

                // Relative paths for database
                var idCardRelativePath = $"uploads/kyc/{userId}/{idCardFileName}";
                var faceRelativePath = $"uploads/kyc/{userId}/{faceFileName}";

                // Create initial KYC record with "Processing" status
                var kycDetail = new KycDetail
                {
                    UserId = userId,
                    IdCardImagePath = idCardRelativePath,
                    FaceImagePath = faceRelativePath,
                    UserEnteredName = userEnteredName,
                    UserEnteredId = userEnteredId,
                    Status = "Processing",
                    AdminComment = "Your KYC documents are being processed. This may take 10-20 minutes.",
                    SubmittedAt = DateTime.UtcNow,
                    OcrProcessed = false,
                    NameMatch = false,
                    IdMatch = false
                };

                _context.KycDetails.Add(kycDetail);
                await _context.SaveChangesAsync();

                // Schedule OCR processing after delay (10-20 minutes)
                var random = new Random();
                var delayMinutes = random.Next(10, 21); // Random between 10-20 minutes
                _logger.LogInformation("KYC processing scheduled for user {UserId} in {DelayMinutes} minutes", userId, delayMinutes);

                // Start background processing
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(delayMinutes));
                        await ProcessKycAsync(kycDetail.Id, idCardRelativePath, userEnteredName, userEnteredId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in background KYC processing for user {UserId}", userId);
                    }
                });

                TempData["SuccessMessage"] = $"KYC documents uploaded successfully! Your submission is being processed and will be reviewed within 10-20 minutes. You'll see the results on your status page.";
                return RedirectToAction(nameof(Status));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing KYC upload for user");
                TempData["ErrorMessage"] = "An error occurred while processing your submission. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        private async Task ProcessKycAsync(int kycId, string idCardRelativePath, string userEnteredName, string userEnteredId)
        {
            try
            {
                using var scope = HttpContext.RequestServices.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var ocrService = scope.ServiceProvider.GetRequiredService<IOcrService>();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<KycController>>();

                var kycDetail = await context.KycDetails.FindAsync(kycId);
                if (kycDetail == null || kycDetail.Status != "Processing")
                {
                    logger.LogWarning("KYC record {KycId} not found or not in processing status", kycId);
                    return;
                }

                logger.LogInformation("Starting delayed OCR processing for KYC {KycId}", kycId);

                // Perform OCR on ID card
                var (extractedName, extractedId) = await ocrService.ExtractNameAndIdAsync(idCardRelativePath);

                logger.LogInformation("OCR Results for KYC {KycId} - Extracted Name: {ExtractedName}, Extracted ID: {ExtractedId}", 
                    kycId, extractedName, extractedId);

                // Compare extracted data with user-entered data
                var nameMatch = CompareNames(extractedName, userEnteredName);
                var idMatch = CompareIds(extractedId, userEnteredId);

                logger.LogInformation("Comparison Results for KYC {KycId} - Name Match: {NameMatch}, ID Match: {IdMatch}", 
                    kycId, nameMatch, idMatch);

                string status;
                string adminComment;

                if (nameMatch && idMatch)
                {
                    status = "Half-Verified";
                    adminComment = "OCR verification passed. Name and ID match extracted data. Pending final admin approval.";
                    logger.LogInformation("KYC {KycId} half-verified", kycId);
                }
                else if (string.IsNullOrEmpty(extractedName) && string.IsNullOrEmpty(extractedId))
                {
                    status = "Pending";
                    adminComment = "OCR could not extract information from document. Pending manual admin review.";
                    logger.LogWarning("OCR failed to extract data for KYC {KycId}", kycId);
                }
                else
                {
                    status = "Rejected";
                    var mismatches = new List<string>();
                    if (!nameMatch) mismatches.Add("name");
                    if (!idMatch) mismatches.Add("ID number");

                    adminComment = $"OCR verification failed. Mismatch in {string.Join(" and ", mismatches)}. " +
                                 $"Extracted: Name='{extractedName}', ID='{extractedId}'. " +
                                 $"User entered: Name='{userEnteredName}', ID='{userEnteredId}'.";
                    logger.LogWarning("KYC {KycId} rejected due to OCR mismatch", kycId);
                }

                // Update KYC record
                kycDetail.ExtractedName = extractedName;
                kycDetail.ExtractedCitizenshipNumber = extractedId;
                kycDetail.Status = status;
                kycDetail.AdminComment = adminComment;
                kycDetail.OcrProcessed = true;
                kycDetail.NameMatch = nameMatch;
                kycDetail.IdMatch = idMatch;
                kycDetail.ProcessedAt = DateTime.UtcNow;

                await context.SaveChangesAsync();

                logger.LogInformation("KYC {KycId} processing completed with status: {Status}", kycId, status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessKycAsync for KYC {KycId}", kycId);
                
                // Update status to indicate processing error
                try
                {
                    using var scope = HttpContext.RequestServices.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    
                    var kycDetail = await context.KycDetails.FindAsync(kycId);
                    if (kycDetail != null)
                    {
                        kycDetail.Status = "Pending";
                        kycDetail.AdminComment = "An error occurred during processing. Your submission will be reviewed manually by an admin.";
                        kycDetail.OcrProcessed = false;
                        await context.SaveChangesAsync();
                    }
                }
                catch (Exception updateEx)
                {
                    _logger.LogError(updateEx, "Failed to update KYC status after processing error for KYC {KycId}", kycId);
                }
            }
        }

        // GET: Kyc/Status
        public async Task<IActionResult> Status()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            var kycDetail = await _context.KycDetails
                .Include(k => k.User)
                .FirstOrDefaultAsync(k => k.UserId == userId);

            if (kycDetail == null)
            {
                return RedirectToAction(nameof(Index));
            }

            // Add estimated completion time for processing status
            if (kycDetail.Status == "Processing")
            {
                var estimatedCompletionTime = kycDetail.SubmittedAt.AddMinutes(20);
                ViewBag.EstimatedCompletionTime = estimatedCompletionTime;
                ViewBag.IsProcessing = true;
            }

            return View(kycDetail);
        }

        // GET: Kyc/Details
        public async Task<IActionResult> Details()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            var kycDetail = await _context.KycDetails
                .Include(k => k.User)
                .FirstOrDefaultAsync(k => k.UserId == userId);

            if (kycDetail == null)
            {
                return RedirectToAction(nameof(Index));
            }

            return View(kycDetail);
        }

        // GET: Kyc/Resubmit
        public async Task<IActionResult> Resubmit()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            var kycDetail = await _context.KycDetails.FirstOrDefaultAsync(k => k.UserId == userId);

            if (kycDetail == null || kycDetail.Status != "Rejected")
            {
                return RedirectToAction(nameof(Status));
            }

            // Delete old files
            DeleteKycFiles(kycDetail);

            // Remove old record
            _context.KycDetails.Remove(kycDetail);
            await _context.SaveChangesAsync();

            TempData["InfoMessage"] = "You can now resubmit your KYC documents.";
            return RedirectToAction(nameof(Index));
        }

        // API endpoint to check KYC status (for real-time updates)
        [HttpGet]
        public async Task<IActionResult> CheckStatus()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            var kycDetail = await _context.KycDetails.FirstOrDefaultAsync(k => k.UserId == userId);

            if (kycDetail == null)
            {
                return Json(new { status = "not_found" });
            }

            return Json(new { 
                status = kycDetail.Status,
                adminComment = kycDetail.AdminComment,
                submittedAt = kycDetail.SubmittedAt,
                processedAt = kycDetail.ProcessedAt
            });
        }

        private bool CompareNames(string? extractedName, string userEnteredName)
        {
            if (string.IsNullOrWhiteSpace(extractedName) || string.IsNullOrWhiteSpace(userEnteredName))
                return false;

            // Normalize names for comparison
            var normalizedExtracted = NormalizeName(extractedName);
            var normalizedEntered = NormalizeName(userEnteredName);

            // Check for exact match
            if (normalizedExtracted.Equals(normalizedEntered, StringComparison.OrdinalIgnoreCase))
                return true;

            // Check for partial match (at least 70% similarity)
            var similarity = CalculateStringSimilarity(normalizedExtracted, normalizedEntered);
            return similarity >= 0.7;
        }

        private bool CompareIds(string? extractedId, string userEnteredId)
        {
            if (string.IsNullOrWhiteSpace(extractedId) || string.IsNullOrWhiteSpace(userEnteredId))
                return false;

            // Normalize IDs (remove spaces, dashes, etc.)
            var normalizedExtracted = extractedId.Replace("-", "").Replace("/", "").Replace(" ", "");
            var normalizedEntered = userEnteredId.Replace("-", "").Replace("/", "").Replace(" ", "");

            return normalizedExtracted.Equals(normalizedEntered, StringComparison.OrdinalIgnoreCase);
        }

        private string NormalizeName(string name)
        {
            return name.Trim()
                      .Replace("  ", " ")
                      .Replace(".", "")
                      .Replace(",", "");
        }

        private double CalculateStringSimilarity(string str1, string str2)
        {
            if (str1 == str2) return 1.0;

            var maxLength = Math.Max(str1.Length, str2.Length);
            if (maxLength == 0) return 1.0;

            var distance = LevenshteinDistance(str1, str2);
            return 1.0 - (double)distance / maxLength;
        }

        private int LevenshteinDistance(string str1, string str2)
        {
            var matrix = new int[str1.Length + 1, str2.Length + 1];

            for (int i = 0; i <= str1.Length; i++)
                matrix[i, 0] = i;

            for (int j = 0; j <= str2.Length; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= str1.Length; i++)
            {
                for (int j = 1; j <= str2.Length; j++)
                {
                    var cost = str1[i - 1] == str2[j - 1] ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[str1.Length, str2.Length];
        }

        private void DeleteKycFiles(KycDetail kycDetail)
        {
            try
            {
                var idCardPath = Path.Combine(_webHostEnvironment.WebRootPath, kycDetail.IdCardImagePath);
                var facePath = Path.Combine(_webHostEnvironment.WebRootPath, kycDetail.FaceImagePath);

                if (System.IO.File.Exists(idCardPath))
                    System.IO.File.Delete(idCardPath);
                if (System.IO.File.Exists(facePath))
                    System.IO.File.Delete(facePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting KYC files for user {UserId}", kycDetail.UserId);
            }
        }
    }
}