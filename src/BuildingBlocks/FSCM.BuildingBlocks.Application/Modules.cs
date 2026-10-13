using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.BuildingBlocks.Application;

/// <summary>Each module exposes one installer; the host calls Register for every module.</summary>
public interface IModuleInstaller
{
    string Name { get; }
    void Register(IServiceCollection services, IConfiguration configuration);
}
