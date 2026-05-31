using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using WebDispositivosMqtt.Hubs;
using WebDispositivosMqtt.Identity;
using WebDispositivosMqtt.DataIdentity.Models;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.Services.Mqtt;
using WebDispositivosMqtt.Services.Devices;
using WebDispositivosMqtt.Services.Provisioning;
using WebDispositivosMqtt.Services.DeviceRequests;

namespace WebDispositivosMqtt
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            // MVC
            builder.Services.AddControllersWithViews();
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSession();

            // EF core + Identity
            builder.Services.AddDbContext<IdentityAppDbContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

            builder.Services
             .AddIdentity<ApplicationUser, IdentityRole>(options =>
             {
                 options.SignIn.RequireConfirmedAccount = false;
                 options.Password.RequireDigit = true;
                 options.Password.RequireUppercase = false;
                 options.Password.RequireNonAlphanumeric = false;
                 options.Password.RequiredLength = 6;
             })
             .AddEntityFrameworkStores<IdentityAppDbContext>()
             .AddDefaultTokenProviders();

            builder.Services.ConfigureApplicationCookie(options =>
            {
                options.LoginPath = "/Account/Login";
                options.AccessDeniedPath = "/Account/AccessDenied";
            });

            // DatabaseContext
            builder.Services.AddDbContext<DatabaseContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

            //SignalR
            builder.Services.AddSignalR();

            // Servicio mqtt
            builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection("Mqtt"));
            builder.Services.AddHostedService<MqttListenerService>();

            // Servicio de solicitudes de credenciales desde dispositivos ESP32
            builder.Services.Configure<DeviceRequestOptions>(builder.Configuration.GetSection("DeviceRequests"));
            builder.Services.AddSingleton<IDeviceRequestService, DeviceRequestService>();
            builder.Services.AddHostedService<DeviceRequestCleanupWorker>();

            // Servicio de dispositivos conectados
            builder.Services.AddSingleton<IDeviceConnectionService, DeviceConnectionService>();
            builder.Services.AddHostedService<DeviceConnectionCleanupWorker>();

            // Servicio de provisioning MQTT para ESP32
            builder.Services.AddScoped<IDeviceProvisioningService, DeviceProvisioningService>();


            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            if (!app.Environment.IsDevelopment())
                app.UseHttpsRedirection();

            app.UseRouting();
            app.UseSession();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapStaticAssets();

            // rutas MVC
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
                .WithStaticAssets();

            // rutas SignalR
            app.MapHub<NewDeviceConnectionsHub>("/Hubs/NewDeviceConnectionsHub");
            app.MapHub<DeviceConnectionsHub>("/Hubs/DeviceConnectionsHub");

            // Rutas API (controllers con [ApiController])
            app.MapControllers();

            if (app.Environment.IsDevelopment())
            {
                await SeedDevelopmentUser(app);
            }

            app.Run();
        }

        private static async Task SeedDevelopmentUser(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            const string adminRole = "Admin";
            const string email = "user@email.test";
            const string password = "password";

            if (!await roleManager.RoleExistsAsync(adminRole))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole(adminRole));
                if (!roleResult.Succeeded)
                {
                    var roleErrors = string.Join(", ", roleResult.Errors.Select(e => e.Description));
                    throw new InvalidOperationException($"No se pudo crear rol Admin: {roleErrors}");
                }
            }

            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true
                };

                var createResult = await userManager.CreateAsync(user, password);
                if (!createResult.Succeeded)
                {
                    var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                    throw new InvalidOperationException($"Seed falló: {errors}");
                }
            }

            if (!await userManager.IsInRoleAsync(user, adminRole))
            {
                var addRoleResult = await userManager.AddToRoleAsync(user, adminRole);
                if (!addRoleResult.Succeeded)
                {
                    var roleErrors = string.Join(", ", addRoleResult.Errors.Select(e => e.Description));
                    throw new InvalidOperationException($"No se pudo asignar rol Admin: {roleErrors}");
                }
            }
        }
    }
}
