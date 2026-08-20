using FluentValidation;
using MediatR;
using RiuTek.Core.Common;

namespace RiuTek.Application.Common.Behaviors;

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Count != 0)
        {
            var errorMessage = string.Join("; ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}"));
            var error = Error.Validation("Validation.Failed", errorMessage);

            return CreateFailureResult(error);
        }

        return await next();
    }

    private static TResponse CreateFailureResult(Error error)
    {
        // If TResponse is non-generic Result
        if (typeof(TResponse) == typeof(Result))
        {
            return (Result.Failure(error) as TResponse)!;
        }

        // If TResponse is generic Result<TValue>
        var resultType = typeof(TResponse);
        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var valueType = resultType.GetGenericArguments()[0];
            var failureMethod = typeof(Result)
                .GetMethods()
                .First(m => m.Name == nameof(Result.Failure) && m.IsGenericMethod)
                .MakeGenericMethod(valueType);

            return (TResponse)failureMethod.Invoke(null, [error])!;
        }

        throw new InvalidOperationException($"Cannot create failure result for type {typeof(TResponse).Name}");
    }
}
