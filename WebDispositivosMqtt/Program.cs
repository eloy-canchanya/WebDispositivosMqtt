using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using WebDispositivosMqtt.Hubs;
using WebDispositivosMqtt.Identity;
using WebDispositivosMqtt.DataIdentity.Models;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.Services.Mqtt;
using WebDispositivosMqtt.Services.Devices;
using WebDispositivosMqtt.Services.Provisioning;
using WebDispositivosMqtt.Services.DeviceRequests;
using WebDispositivosMqtt.Services.Dynsec;
using WebDispositivosMqtt.Services.Commands;
using WebDispositivosMqtt.Services.Telemetria;
using WebDispositivosMqtt.Services.Auth;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;

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
            builder.Services.AddSingleton<MqttPublisherService>();
            builder.Services.AddSingleton<IMqttPublisherService>(sp => sp.GetRequiredService<MqttPublisherService>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttPublisherService>());

            // Tracking de comandos y acks
            builder.Services.AddSingleton<ICommandAckService, CommandAckService>();

            // Opciones de la terminal web
            builder.Services.Configure<TerminalOptions>(builder.Configuration.GetSection("Terminal"));

            // Servicio de solicitudes de credenciales desde dispositivos ESP32
            builder.Services.Configure<DeviceRequestOptions>(builder.Configuration.GetSection("DeviceRequests"));
            builder.Services.AddSingleton<IDeviceRequestService, DeviceRequestService>();
            builder.Services.AddHostedService<DeviceRequestCleanupWorker>();

            // Servicio de dispositivos conectados
            builder.Services.AddSingleton<IDeviceConnectionService, DeviceConnectionService>();
            builder.Services.AddHostedService<DeviceConnectionCleanupWorker>();

            // Servicio de provisioning MQTT para ESP32
            builder.Services.AddScoped<IDeviceProvisioningService, DeviceProvisioningService>();

            // Servicio de gestión de usuarios dynsec en Mosquitto
            builder.Services.AddScoped<IDynsecService, DynsecService>();

            // Servicio de telemetría de dispositivos
            builder.Services.AddScoped<ITelemetriaService, TelemetriaService>();

            // JWT Auth para API móvil
            builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
            builder.Services.AddScoped<ITokenService, TokenService>();
            builder.Services.AddAuthentication()
                .AddJwtBearer(options =>
                {
                    var jwt = builder.Configuration.GetSection("Jwt");
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwt["Issuer"],
                        ValidAudience = jwt["Audience"],
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(jwt["Key"]!))
                    };
                });

            // Firebase FCM
            var firebaseCredential = GoogleCredential
                .FromFile("clorador-alertas-firebase-adminsdk-fbsvc-e0bb8f1385.json")
                .CreateScoped(
                    "https://www.googleapis.com/auth/cloud-platform",
                    "https://www.googleapis.com/auth/firebase.messaging"
                );
            FirebaseApp.Create(new AppOptions
            {
                Credential = firebaseCredential,
                ProjectId = "clorador-alertas"
            });


            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            if (!app.Environment.IsDevelopment())
            {
                app.UseWhen(
                    ctx => !ctx.Request.Path.StartsWithSegments("/api/devices"),
                    appBuilder => appBuilder.UseHttpsRedirection()
                );
            }

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
            const string email = "admin@coyllor.net";
            const string password = "Admin123";

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
