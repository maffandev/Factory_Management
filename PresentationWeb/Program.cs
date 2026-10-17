using Application.Interfaces;
using Application.Services;
using Infrastructure.AppDbContext;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------- Database ----------------
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ---------------- Repositories ----------------
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IDepartmentRepository, DepartmentRepository>();
builder.Services.AddScoped<IFabricRepository, FabricRepository>();
builder.Services.AddScoped<IArticleRepository, ArticleRepository>();
builder.Services.AddScoped<IArticleDepartmentStatusRepository, ArticleDepartmentStatusRepository>();
builder.Services.AddScoped<IStatusLogRepository, StatusLogRepository>();
builder.Services.AddScoped<IPasswordResetOtpRepository, PasswordResetOtpRepository>();

// ---------------- Services ----------------
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IFabricService, FabricService>();
builder.Services.AddScoped<IArticleService, ArticleService>();
builder.Services.AddScoped<IWorkflowService, WorkflowService>();
builder.Services.AddScoped<IQualityService, QualityService>();
builder.Services.AddScoped<IDeliveryService, DeliveryService>();
builder.Services.AddScoped<IAdminImpersonationService, AdminImpersonationService>();
builder.Services.AddScoped<IExportService, ExportService>();

// ---------------- Razor Pages + Cookie Auth ----------------
builder.Services.AddRazorPages(options =>
{
    // Everything requires a signed-in user except the pages listed below.
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/ForgotPassword");
    options.Conventions.AllowAnonymousToPage("/ForgotPasswordOtp");
    options.Conventions.AllowAnonymousToPage("/ForgotPasswordContactAdmin");

    // The error page must stay anonymous, otherwise an unhandled exception for a
    // signed-out visitor would redirect to /Login and hide the real problem.
    options.Conventions.AllowAnonymousToPage("/Error");
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

var app = builder.Build();

// ---------------- Seed reference data on startup ----------------
// Idempotent: it only writes if the Departments table is empty.
using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    //await database.Database.MigrateAsync();
    await DbSeeder.SeedAsync(database);
}

// ---------------- Middleware pipeline ----------------
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseStatusCodePagesWithReExecute("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();
