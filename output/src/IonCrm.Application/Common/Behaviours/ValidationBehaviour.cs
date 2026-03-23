using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Common.Behaviours;

/// <summary>
/// MediatR pipeline behaviour that automatically runs FluentValidation validators
/// registered for the request type before the handler executes.
/// Throws <see cref="ValidationException"/> (caught by GlobalExceptionMiddleware)
/// when any validation rule fails. If no validators are registered the request passes through.
/// </summary>
/// <typeparam name="TRequest">The MediatR request type.</typeparam>
/// <typeparam name="TResponse">The MediatR response type.</typeparam>
public class ValidationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    private readonly ILogger<ValidationBehaviour<TRequest, TResponse>> _logger;

    /// <summary>Initialises a new instance of <see cref="ValidationBehaviour{TRequest,TResponse}"/>.</summary>
    public ValidationBehaviour(
        IEnumerable<IValidator<TRequest>> validators,
        ILogger<ValidationBehaviour<TRequest, TResponse>> logger)
    {
        _validators = validators;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var failures = _validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
        {
            _logger.LogWarning(
                "Validation failed for {RequestType}: {Errors}",
                typeof(TRequest).Name,
                string.Join("; ", failures.Select(f => f.ErrorMessage)));

            throw new ValidationException(failures);
        }

        return await next();
    }
}
