using Microsoft.Extensions.DependencyInjection;

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


    RegisterInfrastructureImplementations(services);
    RegisterApplicationImplementations(services);

    return services.BuildServiceProvider();
  }

  private static void RegisterApplicationImplementations(ServiceCollection services)
  {
  }


  private static void RegisterInfrastructureImplementations(ServiceCollection services)
  {
  }
}
