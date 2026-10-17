using LearnSphere.Api.Configuration;
using LearnSphere.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LearnSphere.Api.Hosting;

public static class DatabaseInitialization
{
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
        using (var scope = app.Services.CreateScope())
        {
            _ = scope.ServiceProvider.GetRequiredService<IOptions<LlmOptions>>().Value;
            _ = scope.ServiceProvider.GetRequiredService<IOptions<GenerationOptions>>().Value;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            // Additive upgrade for existing POC volumes; no existing transcript/result columns are changed.
            await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "GenerationIntents" (
          "GenerationId" TEXT NOT NULL CONSTRAINT "PK_GenerationIntents" PRIMARY KEY,
          "Provider" TEXT NOT NULL, "Model" TEXT NOT NULL, "UseChunking" INTEGER NOT NULL,
          FOREIGN KEY ("GenerationId") REFERENCES "Generations" ("Id") ON DELETE CASCADE);
        """);
        }

    }
}
