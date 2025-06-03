using BCrypt.Net;
using blogapplication.Data;
using blogapplication.Models;
using blogapplication.Models.DTO;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace blogapplication.Controllers
{

    public class UserController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration configuration;
        public UserController(ApplicationDbContext _context, IConfiguration configuration)
        {
            this._context = _context;
            this.configuration = configuration;
        }
        public IActionResult Index()
        {
            return View();
        }
        [HttpGet]
        public IActionResult Register()
        {
            // Return an empty AddUser model for the form
            return View(new UserViewModel());
        }
        [HttpPost]
        public async Task<IActionResult> Register(UserViewModel user)
        {
            if (ModelState.IsValid)
            {
                if (_context.Users.Any(u => u.Email.ToUpper() == user.Email.ToUpper()))
                {
                    ModelState.AddModelError("Email", "This email is already registered.");
                    return View(user);
                }

                var verificationToken = Guid.NewGuid().ToString();

                string hashedPassword = BCrypt.Net.BCrypt.HashPassword(user.Password);

                var newUser = new User
                {
                    Name = user.Name,
                    Email = user.Email,
                    Role = "User",
                    CreatedAt = DateTime.UtcNow,
                    Password = hashedPassword
                };

                _context.Users.Add(newUser);
                await _context.SaveChangesAsync();

                return RedirectToAction("Login");
            }

            // If ModelState is invalid, return the same form with validation errors
            return View(user);
        }

        public IActionResult Login()
        {
            // Return an empty Login model for the form
            return View();
        }
        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> Login(UserViewModel userViewModel)
        {
            if (!ModelState.IsValid)
            {
                return View(userViewModel);
            }

            // Fetch user by email (case-insensitive)
            var user = _context.Users.FirstOrDefault(u => u.Email.ToUpper() == userViewModel.Email.ToUpper());

            if (user != null && BCrypt.Net.BCrypt.Verify(userViewModel.Password, user.Password))
            {
                var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name ?? user.Email),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role ?? "User")
        };

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);

                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = false, // always session-only
                    ExpiresUtc = DateTime.UtcNow.AddMinutes(30) // 30-minute session
                };

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

                if (user.Role?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true)
                    return RedirectToAction("Index", "Admin");
                else if (user.Role?.Equals("User", StringComparison.OrdinalIgnoreCase) == true)
                    return RedirectToAction("Index", "Blog");

            }

            ModelState.AddModelError("", "Invalid email or password.");
            return View(userViewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [Authorize]
        [HttpGet]
        public IActionResult Profile()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
            {
                return RedirectToAction("Login");
            }

            var user = _context.Users
                .Include(u => u.KycDetail)
                .FirstOrDefault(u => u.Id == int.Parse(userId));

            if (user == null)
            {
                return NotFound();
            }

            return View(user);
        }

        [Authorize]
        [HttpGet]
        public IActionResult EditProfile()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
            {
                return RedirectToAction("Login");
            }

            var user = _context.Users.FirstOrDefault(u => u.Id == int.Parse(userId));
            if (user == null)
            {
                return NotFound();
            }

            var profileViewModel = new UserViewModel
            {
                Name = user.Name,
                Email = user.Email
            };

            return View(profileViewModel);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> EditProfile(UserViewModel model)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
            {
                return RedirectToAction("Login");
            }

            if (ModelState.IsValid)
            {
                var user = _context.Users.FirstOrDefault(u => u.Id == int.Parse(userId));
                if (user == null)
                {
                    return NotFound();
                }

                // Check if email is being changed and if it already exists
                if (user.Email.ToUpper() != model.Email.ToUpper())
                {
                    if (_context.Users.Any(u => u.Email.ToUpper() == model.Email.ToUpper()))
                    {
                        ModelState.AddModelError("Email", "This email is already registered.");
                        return View(model);
                    }
                }

                user.Name = model.Name;
                user.Email = model.Email;

                // Update password if provided
                if (!string.IsNullOrEmpty(model.Password))
                {
                    user.Password = BCrypt.Net.BCrypt.HashPassword(model.Password);
                }

                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Profile updated successfully!";
                return RedirectToAction("Profile");
            }

            return View(model);
        }
    }
}