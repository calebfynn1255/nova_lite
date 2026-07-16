using Microsoft.EntityFrameworkCore;

namespace NovaLite.Database.Factories;

public class NovaDbContextFactory : IDbContextFactory<NovaDbContext>
{
    public NovaDbContext CreateDbContext()
    {
        return new NovaDbContext();
    }
}
