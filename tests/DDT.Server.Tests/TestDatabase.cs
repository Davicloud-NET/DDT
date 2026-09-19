using System.Net.Http.Json;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// Direct access to what the server stored, for states no request can produce quickly, such as a machine last
// seen an hour ago.
internal static class TestDatabase
{
    public static async Task<T> QueryAsync<T>(this DdtApplication application, Func<DdtDbContext, Task<T>> query)
    {
        using IServiceScope scope = application.Services.CreateScope();

        return await query(scope.ServiceProvider.GetRequiredService<DdtDbContext>());
    }

    public static async Task ChangeMachineAsync(this DdtApplication application, Guid machineId, Action<Machine> change)
    {
        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        Machine machine = await database.Machines.SingleAsync(m => m.Id == machineId, TestContext.Current.CancellationToken);

        change(machine);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public static Task<Machine> MachineAsync(this DdtApplication application, Guid machineId) =>
        application.QueryAsync(database => database.Machines.AsNoTracking().SingleAsync(m => m.Id == machineId, TestContext.Current.CancellationToken));

    public static async Task<string?> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))?.Title;
}
