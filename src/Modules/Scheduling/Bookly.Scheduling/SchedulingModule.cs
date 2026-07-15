using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bookly.Scheduling;

/// <summary>
/// Intentionally empty in M0. The SchedulingDbContext and the first slices
/// (Services, Staff) arrive in M3.
/// </summary>
public static class SchedulingModule
{
    public static IServiceCollection AddSchedulingModule(this IServiceCollection services, IConfiguration configuration)
        => services;
}
