using System.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace Fonte.Api.ErrorHandling;

/// <summary>
/// Contrato de erro da aplicação, igual em Development, Testing e produção: toda resposta de erro é
/// <c>application/problem+json</c> com <c>traceId</c> (o trace da requisição, correlacionável no Grafana/Tempo).
/// A Developer Exception Page não define esse contrato.
/// </summary>
public static class ProblemDetailsRegistration
{
    public const string InternalServerErrorTitle = "Erro interno do servidor";
    public const string InternalServerErrorDetail = "Ocorreu um erro inesperado ao processar a requisição.";
    public const string BadRequestTitle = "Requisição inválida";
    public const string NotFoundTitle = "Recurso não encontrado";
    public const string MethodNotAllowedTitle = "Método não permitido";

    public static IServiceCollection AddFonteProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;

            // Só o trace id (32 hex), pesquisável direto no Tempo; o padrão do ASP.NET é o traceparent inteiro.
            problem.Extensions["traceId"] = Activity.Current?.TraceId.ToHexString() ?? context.HttpContext.TraceIdentifier;

            switch (problem.Status)
            {
                case StatusCodes.Status500InternalServerError:
                    // Textos fixos: context.Exception nunca é lido, então nada da exceção chega à resposta.
                    problem.Title = InternalServerErrorTitle;
                    problem.Detail = InternalServerErrorDetail;
                    break;
                case StatusCodes.Status400BadRequest:
                    problem.Title = BadRequestTitle;
                    break;
                case StatusCodes.Status404NotFound:
                    problem.Title = NotFoundTitle;
                    break;
                case StatusCodes.Status405MethodNotAllowed:
                    problem.Title = MethodNotAllowedTitle;
                    break;
            }
        });

        // Corpo ausente ou JSON malformado viram 400 em todos os ambientes; o padrão do ASP.NET é lançar
        // exceção só em Development, o que o tratamento de exceções transformaria em 500.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

        return services;
    }

    /// <summary>Deve ser o primeiro middleware registrado pela aplicação.</summary>
    public static IApplicationBuilder UseFonteErrorResponses(this IApplicationBuilder app) =>
        app.UseExceptionHandler()
            // Respostas de erro sem corpo (400 nativo, 404, 405) também recebem ProblemDetails.
            .UseStatusCodePages();
}
