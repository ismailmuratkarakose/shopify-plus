using Marketplace.BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using static Microsoft.AspNetCore.Http.Results;

namespace Marketplace.BuildingBlocks.Web;

/// <summary>Result/Error tiplerini standart HTTP sonuçlarına çevirir. Tüm servislerde ortak.</summary>
public static class ResultHttpExtensions
{
    public static IResult ToHttpResult<T>(
        this Result<T> result,
        bool created = false,
        Func<T, string>? location = null)
    {
        if (result.IsSuccess)
            return created && location is not null
                ? Created(location(result.Value), result.Value)
                : Ok(result.Value);

        return result.Error.ToProblem();
    }

    public static IResult ToHttpResult(this Result result, IResult? onSuccess = null)
        => result.IsSuccess ? (onSuccess ?? NoContent()) : result.Error.ToProblem();

    public static IResult ToProblem(this Error error) => error.Type switch
    {
        ErrorType.Validation => ValidationProblem(
            new Dictionary<string, string[]> { [error.Code] = [error.Message] }),
        ErrorType.NotFound => Problem(error.Message, statusCode: StatusCodes.Status404NotFound, title: error.Code),
        ErrorType.Conflict => Problem(error.Message, statusCode: StatusCodes.Status409Conflict, title: error.Code),
        ErrorType.Unauthorized => Problem(error.Message, statusCode: StatusCodes.Status403Forbidden, title: error.Code),
        _ => Problem(error.Message, statusCode: StatusCodes.Status500InternalServerError, title: error.Code)
    };
}
