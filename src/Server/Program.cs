using System;
using System.Threading.Tasks;
using BlazorHero.CleanArchitecture.Infrastructure.Contexts;
using BlazorHero.CleanArchitecture.Server.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlazorHero.CleanArchitecture.Server
{
    public class Program
    {
        public async static Task Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();

            using (var scope = host.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

                try
                {
                    var context = services.GetRequiredService<BlazorHeroContext>();

                    context.Database.EnsureCreated();

                    await MigrateDocumentColumnsAsync(context, logger);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An error occurred while creating/migrating the database.");
                    throw;
                }
            }

            await host.RunAsync();
        }

        private static async Task MigrateDocumentColumnsAsync(BlazorHeroContext context, ILogger<Program> logger)
        {
            var migrations = new (string Column, string Definition)[]
            {
                ("Status", "INTEGER NOT NULL DEFAULT 0"),
                ("ReviewerId", "NVARCHAR(128)"),
                ("RejectionReason", "TEXT"),
                ("ReviewedOn", "TEXT")
            };

            foreach (var (column, definition) in migrations)
            {
                try
                {
                    var sql = $"ALTER TABLE \"Documents\" ADD COLUMN \"{column}\" {definition}";
                    await context.Database.ExecuteSqlRawAsync(sql);
                    logger.LogInformation("Added column {Column} to Documents table.", column);
                }
                catch (Exception ex)
                {
                    logger.LogInformation("Column {Column} already exists in Documents table. Message: {Message}", column, ex.Message);
                }
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .UseSerilog()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStaticWebAssets();
                    webBuilder.UseStartup<Startup>();
                });
    }
}
