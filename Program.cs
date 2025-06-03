using blogapplication.Data;
using blogapplication.Hubs;
using blogapplication.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace blogapplication
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllersWithViews();
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("dbConn")));
            builder.Services.AddSignalR();
            builder.Services.AddScoped<IOcrService, TesseractOcrService>();
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.LoginPath = "/User/Login";
                o.LogoutPath = "/User/Logout";
                //o.AccessDeniedPath = "/User/AccessDenied";
                o.ExpireTimeSpan = TimeSpan.FromMinutes(1);
                o.SlidingExpiration = true;
            });
            builder.Services.AddSession(o =>
            {
                o.IdleTimeout = TimeSpan.FromMinutes(1);
                o.Cookie.HttpOnly = true;
                o.Cookie.IsEssential = true;

            });
            var app = builder.Build();
            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseAuthentication(); 
            app.UseRouting();
            app.UseAuthorization();
            app.Use(async (context, next) =>
            {
                // Only intercept GET requests for the root path
                if (context.Request.Method == "GET" && context.Request.Path == "/"
                    && context.User?.Identity?.IsAuthenticated == true)
                {
                    string redirectTo = "/";

                    if (context.User.IsInRole("User"))
                        redirectTo = "/Blog/Index";
                    else if (context.User.IsInRole("Admin"))
                        redirectTo = "/Admin/Index";

                    context.Response.Redirect(redirectTo);
                    return;
                }

                await next();
            });
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");
            app.MapHub<NotificationHub>("/notificationHub");
            app.Run();
        }
    }
}
