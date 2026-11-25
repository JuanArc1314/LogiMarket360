using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LogiMarket.Infrastructure.Persistencia;

//usada solo por las herramientas de EF Core (dotnet ef migrations ...)
//no afecta la ejecucion
internal sealed class FabricaContextoDisenio : IDesignTimeDbContextFactory<LogiMarketDbContext>
{
    public LogiMarketDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<LogiMarketDbContext>()
            .UseSqlite("Data Source=logimarket_disenio.db")
            .Options;
        return new LogiMarketDbContext(opciones);
    }
}
