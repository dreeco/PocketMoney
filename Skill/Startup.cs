using Domain.Repositories;
using Infrastructure.DataAccess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static CSharpFunctionalExtensions.Result;

namespace Skill;

/// <summary>
/// Handles dependency injection (domain - infra)
/// </summary>
public class Startup
{
    /// <summary>
    /// Defines the mapping between interfaces and implementations for dependency injection
    /// </summary>
    /// <returns></returns>
    public IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        RegisterConfiguration(services);

        RegisterInfrastructureImplementations(services);
        RegisterApplicationImplementations(services);

        return services.BuildServiceProvider();
    }

    private static void RegisterConfiguration(ServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
    }

    private static void RegisterApplicationImplementations(ServiceCollection services)
    {
    }

    private static void RegisterInfrastructureImplementations(ServiceCollection services)
    {
        services.AddSingleton<ICleaningTasksRepository, CleaningTasksRepository>();
    }
}
