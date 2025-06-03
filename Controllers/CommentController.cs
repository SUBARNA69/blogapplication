using blogapplication.Data;
using blogapplication.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using blogapplication.Hubs; // You'll need to create this

namespace blogapplication.Controllers
{
    [Authorize]
    public class CommentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;

        public CommentController(ApplicationDbContext context, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        // POST: Comment/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int BlogId, string Content)
        {
            if (string.IsNullOrWhiteSpace(Content))
            {
                TempData["ErrorMessage"] = "Comment content cannot be empty.";
                return RedirectToAction("Details", "Blog", new { id = BlogId });
            }

            if (Content.Length > 1000) // Set a reasonable limit
            {
                TempData["ErrorMessage"] = "Comment is too long. Please keep it under 1000 characters.";
                return RedirectToAction("Details", "Blog", new { id = BlogId });
            }

            try
            {
                // Verify the blog exists and get blog owner info
                var blog = await _context.Blogs
                    .Include(b => b.User)
                    .FirstOrDefaultAsync(b => b.Id == BlogId);

                if (blog == null)
                {
                    TempData["ErrorMessage"] = "Blog post not found.";
                    return RedirectToAction("Index", "Blog");
                }

                // Get current user ID and user info
                var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                if (currentUserId == 0)
                {
                    TempData["ErrorMessage"] = "You must be logged in to comment.";
                    return RedirectToAction("Details", "Blog", new { id = BlogId });
                }

                // Get commenter info
                var commenter = await _context.Users.FindAsync(currentUserId);
                if (commenter == null)
                {
                    TempData["ErrorMessage"] = "User not found.";
                    return RedirectToAction("Details", "Blog", new { id = BlogId });
                }

                // Create the comment
                var comment = new Comment
                {
                    Content = Content.Trim(),
                    CreatedAt = DateTime.Now,
                    BlogId = BlogId,
                    UserId = currentUserId
                };

                _context.Comments.Add(comment);
                await _context.SaveChangesAsync();

                // Send SignalR notification to blog owner (only if commenter is not the blog owner)
                if (blog.UserId != currentUserId)
                {
                    var notification = new
                    {
                        type = "new_comment",
                        message = $"{commenter.Name} commented on your blog post '{blog.Title}'",
                        blogId = BlogId,
                        blogTitle = blog.Title,
                        commenterName = commenter.Name,
                        commentContent = Content.Length > 50 ? Content.Substring(0, 50) + "..." : Content,
                        timestamp = DateTime.Now.ToString("MMM dd, yyyy • h:mm tt"),
                        url = Url.Action("Details", "Blog", new { id = BlogId })
                    };

                    // Send to specific user (blog owner)
                    await _hubContext.Clients.User(blog.UserId.ToString())
                        .SendAsync("ReceiveNotification", notification);
                }

                TempData["SuccessMessage"] = "Your comment has been posted successfully!";
            }
            catch (Exception ex)
            {
                // Log the exception here if you have logging configured
                TempData["ErrorMessage"] = "An error occurred while posting your comment. Please try again.";
            }

            return RedirectToAction("Details", "Blog", new { id = BlogId });
        }

        // POST: Comment/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, int blogId)
        {
            try
            {
                var comment = await _context.Comments
                    .Include(c => c.Blog)
                    .FirstOrDefaultAsync(c => c.Id == id);

                if (comment == null)
                {
                    TempData["ErrorMessage"] = "Comment not found.";
                    return RedirectToAction("Details", "Blog", new { id = blogId });
                }

                // Check if current user owns this comment or is an admin
                var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                if (comment.UserId != currentUserId && !User.IsInRole("Admin"))
                {
                    TempData["ErrorMessage"] = "You don't have permission to delete this comment.";
                    return RedirectToAction("Details", "Blog", new { id = blogId });
                }

                _context.Comments.Remove(comment);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Comment deleted successfully.";
            }
            catch (Exception ex)
            {
                // Log the exception here if you have logging configured
                TempData["ErrorMessage"] = "An error occurred while deleting the comment. Please try again.";
            }

            return RedirectToAction("Details", "Blog", new { id = blogId });
        }

        // GET: Comment/Edit/5 (Optional - for editing comments)
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var comment = await _context.Comments
                .Include(c => c.Blog)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (comment == null)
            {
                return NotFound();
            }

            // Check if current user owns this comment
            var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            if (comment.UserId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            return View(comment);
        }

        // POST: Comment/Edit/5 (Optional - for editing comments)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, string Content)
        {
            if (string.IsNullOrWhiteSpace(Content))
            {
                TempData["ErrorMessage"] = "Comment content cannot be empty.";
                return RedirectToAction("Edit", new { id = id });
            }

            if (Content.Length > 1000)
            {
                TempData["ErrorMessage"] = "Comment is too long. Please keep it under 1000 characters.";
                return RedirectToAction("Edit", new { id = id });
            }

            try
            {
                var comment = await _context.Comments
                    .Include(c => c.Blog)
                    .FirstOrDefaultAsync(c => c.Id == id);

                if (comment == null)
                {
                    return NotFound();
                }

                // Check ownership
                var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                if (comment.UserId != currentUserId && !User.IsInRole("Admin"))
                {
                    return Forbid();
                }

                comment.Content = Content.Trim();
                // You might want to add an UpdatedAt field to track edits

                _context.Update(comment);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Comment updated successfully!";
                return RedirectToAction("Details", "Blog", new { id = comment.BlogId });
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CommentExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "An error occurred while updating the comment.";
                return RedirectToAction("Edit", new { id = id });
            }
        }

        // GET: Comment/GetCommentsForBlog/5 (AJAX endpoint for dynamic loading)
        [HttpGet]
        public async Task<IActionResult> GetCommentsForBlog(int blogId)
        {
            try
            {
                var commentsData = await _context.Comments
                    .Include(c => c.User)
                    .Where(c => c.BlogId == blogId)
                    .OrderByDescending(c => c.CreatedAt)
                    .ToListAsync();

                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var isAdmin = User.IsInRole("Admin");

                var comments = commentsData.Select(c => new
                {
                    id = c.Id,
                    content = c.Content,
                    createdAt = c.CreatedAt.ToString("MMM dd, yyyy • h:mm tt"),
                    userName = c.User?.Name ?? "Unknown User",
                    userInitial = !string.IsNullOrEmpty(c.User?.Name) ? c.User.Name.Substring(0, 1).ToUpper() : "U",
                    userId = c.UserId,
                    canDelete = c.UserId == userId || isAdmin
                });

                return Json(comments);
            }
            catch (Exception ex)
            {
                return Json(new { error = "Failed to load comments" });
            }
        }

        private bool CommentExists(int id)
        {
            return _context.Comments.Any(e => e.Id == id);
        }
    }
}