using WebDispositivosMqtt.Hubs;
using WebDispositivosMqtt.Services;

namespace WebDispositivosMqtt
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            // MVC
            builder.Services.AddControllersWithViews();
            
            //SignalR
            builder.Services.AddSignalR();

            builder.Services.AddSingleton<ConnectionTracker>();


            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseRouting();

            app.UseAuthorization();

            app.MapStaticAssets();

            // rutas MVC
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
                .WithStaticAssets();

            // rutas SignalR
            app.MapHub<EchoHub>("/Hubs/EchoHub");


            app.Run();
        }
    }
}
