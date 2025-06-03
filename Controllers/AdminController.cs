using blogapplication.Data;
using blogapplication.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace blogapplication.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ILogger<AdminController> _logger;

        public AdminController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment, ILogger<AdminController> logger)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _logger = logger;
        }

        // GET: Admin
        public async Task<IActionResult> Index()
        {
            var stats = new
            {
                TotalUsers = await _context.Users.CountAsync(),
                TotalBlogs = await _context.Blogs.CountAsync(),
                TotalComments = await _context.Comments.CountAsync(),
                BlogsThisMonth = await _context.Blogs.CountAsync(b => b.CreatedAt.Month == DateTime.Now.Month && b.CreatedAt.Year == DateTime.Now.Year),
                // KYC Statistics
                TotalKycSubmissions = await _context.KycDetails.CountAsync(),
                PendingKycSubmissions = await _context.KycDetails.CountAsync(k => k.Status == "Pending"),
                VerifiedKycSubmissions = await _context.KycDetails.CountAsync(k => k.Status == "Verified"),
                RejectedKycSubmissions = await _context.KycDetails.CountAsync(k => k.Status == "Rejected"),
                RecentBlogs = await _context.Blogs
                    .Include(b => b.User)
                    .Include(b => b.Comments)
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(5)
                    .ToListAsync(),
                RecentUsers = await _context.Users
                    .OrderByDescending(u => u.CreatedAt)
                    .Take(5)
                    .ToListAsync(),
                RecentKycSubmissions = await _context.KycDetails
                    .Include(k => k.User)
                    .OrderByDescending(k => k.SubmittedAt)
                    .Take(5)
                    .ToListAsync()
            };

            return View(stats);
        }

        // GET: Admin/ManageBlogs
        public async Task<IActionResult> ManageBlogs(string searchTerm = "", int page = 1, int pageSize = 10)
        {
            var query = _context.Blogs
                .Include(b => b.User)
                .Include(b => b.Comments)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(b => b.Title.Contains(searchTerm) ||
                                        b.Content.Contains(searchTerm) ||
                                        b.User.Name.Contains(searchTerm));
            }

            var totalBlogs = await query.CountAsync();
            var blogs = await query
                .OrderByDescending(b => b.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.SearchTerm = searchTerm;
            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalBlogs / pageSize);
            ViewBag.TotalBlogs = totalBlogs;

            return View(blogs);
        }

        // GET: Admin/BlogDetails/5
        public async Task<IActionResult> BlogDetails(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var blog = await _context.Blogs
                .Include(b => b.User)
                .Include(b => b.Comments)
                .ThenInclude(c => c.User)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (blog == null)
            {
                return NotFound();
            }

            return View(blog);
        }

        // POST: Admin/DeleteBlog/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteBlog(int id)
        {
            try
            {
                var blog = await _context.Blogs.FindAsync(id);
                if (blog == null)
                {
                    TempData["ErrorMessage"] = "Blog post not found.";
                    return RedirectToAction(nameof(ManageBlogs));
                }

                // Delete associated image
                if (!string.IsNullOrEmpty(blog.Image))
                {
                    var imagePath = Path.Combine(_webHostEnvironment.WebRootPath, blog.Image.TrimStart('/'));
                    if (System.IO.File.Exists(imagePath))
                    {
                        System.IO.File.Delete(imagePath);
                    }
                }

                _context.Blogs.Remove(blog);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Blog post deleted successfully.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "An error occurred while deleting the blog post.";
            }

            return RedirectToAction(nameof(ManageBlogs));
        }

        // GET: Admin/ManageUsers
        public async Task<IActionResult> ManageUsers(string searchTerm = "", int page = 1, int pageSize = 10)
        {
            var query = _context.Users.AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(u => u.Name.Contains(searchTerm) ||
                                        u.Email.Contains(searchTerm));
            }

            var totalUsers = await query.CountAsync();
            var users = await query
                .OrderByDescending(u => u.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Get blog and comment counts for each user
            var userStats = new Dictionary<int, (int BlogCount, int CommentCount)>();
            foreach (var user in users)
            {
                var blogCount = await _context.Blogs.CountAsync(b => b.UserId == user.Id);
                var commentCount = await _context.Comments.CountAsync(c => c.UserId == user.Id);
                userStats[user.Id] = (blogCount, commentCount);
            }

            ViewBag.UserStats = userStats;
            ViewBag.SearchTerm = searchTerm;
            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalUsers / pageSize);
            ViewBag.TotalUsers = totalUsers;

            return View(users);
        }

        // GET: Admin/UserDetails/5
        public async Task<IActionResult> UserDetails(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var userBlogs = await _context.Blogs
                .Include(b => b.Comments)
                .Where(b => b.UserId == id)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            var userComments = await _context.Comments
                .Include(c => c.Blog)
                .Where(c => c.UserId == id)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            var userDetails = new
            {
                User = user,
                Blogs = userBlogs,
                Comments = userComments,
                TotalBlogs = userBlogs.Count,
                TotalComments = userComments.Count,
                TotalBlogViews = userBlogs.Sum(b => b.Comments.Count) // Using comments as a proxy for engagement
            };

            return View(userDetails);
        }

        // POST: Admin/DeleteUser/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(int id)
        {
            try
            {
                var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                if (id == currentUserId)
                {
                    TempData["ErrorMessage"] = "You cannot delete your own account.";
                    return RedirectToAction(nameof(ManageUsers));
                }

                var user = await _context.Users.FindAsync(id);
                if (user == null)
                {
                    TempData["ErrorMessage"] = "User not found.";
                    return RedirectToAction(nameof(ManageUsers));
                }

                // Get user's blogs to delete associated images
                var userBlogs = await _context.Blogs.Where(b => b.UserId == id).ToListAsync();

                // Delete associated blog images
                foreach (var blog in userBlogs)
                {
                    if (!string.IsNullOrEmpty(blog.Image))
                    {
                        var imagePath = Path.Combine(_webHostEnvironment.WebRootPath, blog.Image.TrimStart('/'));
                        if (System.IO.File.Exists(imagePath))
                        {
                            System.IO.File.Delete(imagePath);
                        }
                    }
                }

                // Delete user (cascade delete will handle blogs and comments)
                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"User '{user.Name}' and all associated content deleted successfully.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "An error occurred while deleting the user.";
            }

            return RedirectToAction(nameof(ManageUsers));
        }

        // POST: Admin/DeleteComment/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteComment(int id, string returnUrl = "")
        {
            try
            {
                var comment = await _context.Comments.FindAsync(id);
                if (comment == null)
                {
                    TempData["ErrorMessage"] = "Comment not found.";
                }
                else
                {
                    _context.Comments.Remove(comment);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Comment deleted successfully.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "An error occurred while deleting the comment.";
            }

            if (!string.IsNullOrEmpty(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction(nameof(Index));
        }

        // ============ KYC MANAGEMENT ACTIONS ============

        // GET: Admin/ManageKyc
        public async Task<IActionResult> ManageKyc(string status = "", string searchTerm = "", int page = 1, int pageSize = 10)
        {
            var query = _context.KycDetails
                .Include(k => k.User)
                .AsQueryable();

            // Filter by status
            if (!string.IsNullOrEmpty(status) && status != "All")
            {
                query = query.Where(k => k.Status == status);
            }

            // Search functionality
            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(k => k.User.Name.Contains(searchTerm) ||
                                        k.User.Email.Contains(searchTerm) ||
                                        k.ExtractedName.Contains(searchTerm) ||
                                        k.ExtractedCitizenshipNumber.Contains(searchTerm));
            }

            var totalSubmissions = await query.CountAsync();
            var kycSubmissions = await query
                .OrderByDescending(k => k.SubmittedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Get statistics
            var stats = new
            {
                Total = await _context.KycDetails.CountAsync(),
                Pending = await _context.KycDetails.CountAsync(k => k.Status == "Pending"),
                Verified = await _context.KycDetails.CountAsync(k => k.Status == "Verified"),
                Rejected = await _context.KycDetails.CountAsync(k => k.Status == "Rejected")
            };

            ViewBag.Stats = stats;
            ViewBag.StatusFilter = status;
            ViewBag.SearchTerm = searchTerm;
            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalSubmissions / pageSize);
            ViewBag.TotalSubmissions = totalSubmissions;

            return View(kycSubmissions);
        }

        // GET: Admin/KycDetails/5
        public async Task<IActionResult> KycDetails(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var kycDetail = await _context.KycDetails
                .Include(k => k.User)
                .FirstOrDefaultAsync(k => k.Id == id);

            if (kycDetail == null)
            {
                return NotFound();
            }

            return View(kycDetail);
        }

        // POST: Admin/ApproveKyc/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveKyc(int id, string extractedName = "", string extractedCitizenshipNumber = "")
        {
            try
            {
                var kycDetail = await _context.KycDetails.FindAsync(id);
                if (kycDetail == null)
                {
                    TempData["ErrorMessage"] = "KYC submission not found.";
                    return RedirectToAction(nameof(ManageKyc));
                }

                if (kycDetail.Status == "Verified")
                {
                    TempData["InfoMessage"] = "This KYC submission is already verified.";
                    return RedirectToAction(nameof(KycDetails), new { id });
                }

                kycDetail.Status = "Verified";
                kycDetail.AdminComment = "Identity verification completed successfully.";
                kycDetail.ExtractedName = extractedName;
                kycDetail.ExtractedCitizenshipNumber = extractedCitizenshipNumber;

                _context.KycDetails.Update(kycDetail);
                await _context.SaveChangesAsync();

                _logger.LogInformation("KYC approved for user {UserId} by admin {AdminId}",
                    kycDetail.UserId, User.FindFirst(ClaimTypes.NameIdentifier)?.Value);

                TempData["SuccessMessage"] = "KYC submission approved successfully.";
                return RedirectToAction(nameof(KycDetails), new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving KYC submission {KycId}", id);
                TempData["ErrorMessage"] = "An error occurred while approving the KYC submission.";
                return RedirectToAction(nameof(KycDetails), new { id });
            }
        }

        // POST: Admin/RejectKyc/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectKyc(int id, string rejectionReason)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rejectionReason))
                {
                    TempData["ErrorMessage"] = "Rejection reason is required.";
                    return RedirectToAction(nameof(KycDetails), new { id });
                }

                var kycDetail = await _context.KycDetails.FindAsync(id);
                if (kycDetail == null)
                {
                    TempData["ErrorMessage"] = "KYC submission not found.";
                    return RedirectToAction(nameof(ManageKyc));
                }

                if (kycDetail.Status == "Verified")
                {
                    TempData["ErrorMessage"] = "Cannot reject a verified KYC submission.";
                    return RedirectToAction(nameof(KycDetails), new { id });
                }

                kycDetail.Status = "Rejected";
                kycDetail.AdminComment = rejectionReason;

                _context.KycDetails.Update(kycDetail);
                await _context.SaveChangesAsync();

                _logger.LogInformation("KYC rejected for user {UserId} by admin {AdminId}. Reason: {Reason}",
                    kycDetail.UserId, User.FindFirst(ClaimTypes.NameIdentifier)?.Value, rejectionReason);

                TempData["SuccessMessage"] = "KYC submission rejected successfully.";
                return RedirectToAction(nameof(KycDetails), new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting KYC submission {KycId}", id);
                TempData["ErrorMessage"] = "An error occurred while rejecting the KYC submission.";
                return RedirectToAction(nameof(KycDetails), new { id });
            }
        }

        // POST: Admin/BulkKycAction
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkKycAction(string action, int[] selectedIds, string bulkRejectionReason = "")
        {
            try
            {
                if (selectedIds == null || selectedIds.Length == 0)
                {
                    TempData["ErrorMessage"] = "Please select at least one KYC submission.";
                    return RedirectToAction(nameof(ManageKyc));
                }

                var kycSubmissions = await _context.KycDetails
                    .Where(k => selectedIds.Contains(k.Id))
                    .ToListAsync();

                int processedCount = 0;

                foreach (var kyc in kycSubmissions)
                {
                    switch (action.ToLower())
                    {
                        case "approve":
                            if (kyc.Status != "Verified")
                            {
                                kyc.Status = "Verified";
                                kyc.AdminComment = "Bulk approved by admin.";
                                processedCount++;
                            }
                            break;

                        case "reject":
                            if (string.IsNullOrWhiteSpace(bulkRejectionReason))
                            {
                                TempData["ErrorMessage"] = "Rejection reason is required for bulk rejection.";
                                return RedirectToAction(nameof(ManageKyc));
                            }
                            if (kyc.Status != "Verified")
                            {
                                kyc.Status = "Rejected";
                                kyc.AdminComment = bulkRejectionReason;
                                processedCount++;
                            }
                            break;

                        case "pending":
                            kyc.Status = "Pending";
                            kyc.AdminComment = "Reset to pending status by admin.";
                            processedCount++;
                            break;
                    }
                }

                if (processedCount > 0)
                {
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Successfully {action}d {processedCount} KYC submission(s).";
                }
                else
                {
                    TempData["InfoMessage"] = "No submissions were processed.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing bulk KYC action {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the bulk action.";
            }

            return RedirectToAction(nameof(ManageKyc));
        }

        // GET: Admin/KycStatistics
        public async Task<IActionResult> KycStatistics()
        {
            var stats = new
            {
                Overview = new
                {
                    Total = await _context.KycDetails.CountAsync(),
                    Pending = await _context.KycDetails.CountAsync(k => k.Status == "Pending"),
                    Verified = await _context.KycDetails.CountAsync(k => k.Status == "Verified"),
                    Rejected = await _context.KycDetails.CountAsync(k => k.Status == "Rejected")
                },
                MonthlySubmissions = await _context.KycDetails
                    .Where(k => k.SubmittedAt >= DateTime.UtcNow.AddMonths(-12))
                    .GroupBy(k => new { k.SubmittedAt.Year, k.SubmittedAt.Month })
                    .Select(g => new {
                        Year = g.Key.Year,
                        Month = g.Key.Month,
                        Count = g.Count()
                    })
                    .OrderBy(x => x.Year).ThenBy(x => x.Month)
                    .ToListAsync(),
                RecentActivity = await _context.KycDetails
                    .Include(k => k.User)
                    .OrderByDescending(k => k.SubmittedAt)
                    .Take(10)
                    .ToListAsync(),
                ProcessingTimes = await _context.KycDetails
                    .Where(k => k.Status != "Pending")
                    .Select(k => new {
                        Status = k.Status,
                        ProcessingDays = EF.Functions.DateDiffDay(k.SubmittedAt, DateTime.UtcNow)
                    })
                    .ToListAsync()
            };

            return View(stats);
        }
    }
}
