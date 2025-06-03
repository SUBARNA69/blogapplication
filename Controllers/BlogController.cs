using blogapplication.Data;
using blogapplication.Models;
using blogapplication.Models.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace blogapplication.Controllers
{
    [Authorize]
    public class BlogController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public BlogController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // GET: Blog
        public async Task<IActionResult> Index()
        {
            var blogs = await _context.Blogs
                .Include(b => b.User)
                .Include(b => b.Comments)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            return View(blogs);
        }

        // GET: Blog/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Blog/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BlogCreateViewModel model, IFormFile? imageFile)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var blog = new Blog
                    {
                        Title = model.Title,
                        Content = model.Content,
                        CreatedAt = DateTime.Now,
                        UserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0")
                    };

                    // Handle image upload
                    if (imageFile != null && imageFile.Length > 0)
                    {
                        // Validate file type
                        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                        var fileExtension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();

                        if (!allowedExtensions.Contains(fileExtension))
                        {
                            ModelState.AddModelError("ImageFile", "Please upload a valid image file (jpg, jpeg, png, gif, webp).");
                            return View(model);
                        }

                        // Check file size (max 5MB)
                        if (imageFile.Length > 5 * 1024 * 1024)
                        {
                            ModelState.AddModelError("ImageFile", "Image file size cannot exceed 5MB.");
                            return View(model);
                        }

                        // Create uploads directory if it doesn't exist
                        var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "blogs");
                        if (!Directory.Exists(uploadsFolder))
                        {
                            Directory.CreateDirectory(uploadsFolder);
                        }

                        // Generate unique filename
                        var uniqueFileName = Guid.NewGuid().ToString() + fileExtension;
                        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        // Save file
                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await imageFile.CopyToAsync(fileStream);
                        }

                        blog.Image = "/uploads/blogs/" + uniqueFileName;
                    }

                    _context.Blogs.Add(blog);
                    await _context.SaveChangesAsync();

                    TempData["SuccessMessage"] = "Blog post created successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", "An error occurred while creating the blog post. Please try again.");
                    // Log the exception here if you have logging configured
                }
            }

            return View(model);
        }

        // GET: Blog/Details/5
        public async Task<IActionResult> Details(int? id)
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

        // GET: Blog/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var blog = await _context.Blogs.FindAsync(id);
            if (blog == null)
            {
                return NotFound();
            }

            // Check if current user owns this blog
            var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            if (blog.UserId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            var model = new BlogEditViewModel
            {
                Id = blog.Id,
                Title = blog.Title,
                Content = blog.Content,
                ExistingImage = blog.Image
            };

            return View(model);
        }

        // POST: Blog/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, BlogEditViewModel model, IFormFile? imageFile)
        {
            if (id != model.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var blog = await _context.Blogs.FindAsync(id);
                    if (blog == null)
                    {
                        return NotFound();
                    }

                    // Check ownership
                    var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                    if (blog.UserId != currentUserId && !User.IsInRole("Admin"))
                    {
                        return Forbid();
                    }

                    blog.Title = model.Title;
                    blog.Content = model.Content;
                    blog.UpdatedAt = DateTime.Now;

                    // Handle new image upload
                    if (imageFile != null && imageFile.Length > 0)
                    {
                        // Validate and upload new image (same logic as Create)
                        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                        var fileExtension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();

                        if (!allowedExtensions.Contains(fileExtension))
                        {
                            ModelState.AddModelError("ImageFile", "Please upload a valid image file.");
                            model.ExistingImage = blog.Image;
                            return View(model);
                        }

                        if (imageFile.Length > 5 * 1024 * 1024)
                        {
                            ModelState.AddModelError("ImageFile", "Image file size cannot exceed 5MB.");
                            model.ExistingImage = blog.Image;
                            return View(model);
                        }

                        // Delete old image if exists
                        if (!string.IsNullOrEmpty(blog.Image))
                        {
                            var oldImagePath = Path.Combine(_webHostEnvironment.WebRootPath, blog.Image.TrimStart('/'));
                            if (System.IO.File.Exists(oldImagePath))
                            {
                                System.IO.File.Delete(oldImagePath);
                            }
                        }

                        // Save new image
                        var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "blogs");
                        if (!Directory.Exists(uploadsFolder))
                        {
                            Directory.CreateDirectory(uploadsFolder);
                        }

                        var uniqueFileName = Guid.NewGuid().ToString() + fileExtension;
                        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await imageFile.CopyToAsync(fileStream);
                        }

                        blog.Image = "/uploads/blogs/" + uniqueFileName;
                    }

                    _context.Update(blog);
                    await _context.SaveChangesAsync();

                    TempData["SuccessMessage"] = "Blog post updated successfully!";
                    return RedirectToAction(nameof(Details), new { id = blog.Id });
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!BlogExists(model.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (Exception)
                {
                    ModelState.AddModelError("", "An error occurred while updating the blog post.");
                }
            }

            model.ExistingImage = (await _context.Blogs.FindAsync(id))?.Image;
            return View(model);
        }

        // POST: Blog/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var blog = await _context.Blogs.FindAsync(id);
            if (blog == null)
            {
                return NotFound();
            }

            // Check ownership
            var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            if (blog.UserId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
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

            TempData["SuccessMessage"] = "Blog post deleted successfully!";
            return RedirectToAction(nameof(Index));
        }

        private bool BlogExists(int id)
        {
            return _context.Blogs.Any(e => e.Id == id);
        }
    }
}