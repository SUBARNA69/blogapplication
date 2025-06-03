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

        public AdminController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
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
                RecentBlogs = await _context.Blogs
                    .Include(b => b.User)
                    .Include(b => b.Comments)
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(5)
                    .ToListAsync(),
                RecentUsers = await _context.Users
                    .OrderByDescending(u => u.CreatedAt)
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
    }
}