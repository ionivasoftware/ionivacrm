using System.Reflection;
using FluentValidation;
using IonCrm.Application.Common.Behaviours;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace IonCrm.Application;

/// <summary>
/// Extension methods for registering all Application-layer services into the DI container.
/// Call this from Program.cs: <c>services.AddApplicationServices()</c>.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers MediatR handlers, pipeline behaviours, and FluentValidation validators
    /// from the Application assembly.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // MediatR — scans this assembly for IRequestHandler<,> implementations
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            // Validation pipeline runs before every command/query handler
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
        });

        // FluentValidation — scans this assembly for AbstractValidator<T> implementations
        services.AddValidatorsFromAssembly(assembly);

        return services;
    }
}
