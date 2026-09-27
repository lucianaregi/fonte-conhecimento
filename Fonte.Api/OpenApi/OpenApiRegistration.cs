using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

namespace Fonte.Api.OpenApi;

/// <summary>
/// Especificação OpenAPI (<c>/openapi/v1.json</c>) e Swagger UI (<c>/swagger</c>), expostas apenas em
/// Development: fora dele, as rotas não existem e respondem 404.
/// </summary>
public static class OpenApiRegistration
{
    public const string DocumentUrl = "/openapi/v1.json";

    public static IServiceCollection AddFonteOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Fonte API";
                document.Info.Description =
                    "Responde perguntas com base em documentos Markdown indexados (RAG), informando as fontes usadas.";
                return Task.CompletedTask;
            });

            // O traceId é incluído em cada ProblemDetails (ProblemDetailsRegistration), mas não faz parte
            // do schema padrão. HttpValidationProblemDetails herda de ProblemDetails.
            options.AddSchemaTransformer((schema, context, _) =>
            {
                if (typeof(ProblemDetails).IsAssignableFrom(context.JsonTypeInfo.Type))
                {
                    schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
                    schema.Properties["traceId"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Description = "Identificador do trace da requisição (32 caracteres hexadecimais), o mesmo exportado para o Grafana e presente nos logs.",
                    };
                }

                return Task.CompletedTask;
            });
        });

    public static WebApplication MapFonteOpenApi(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        app.MapOpenApi();
        app.UseSwaggerUI(options => options.SwaggerEndpoint(DocumentUrl, "Fonte API v1"));

        return app;
    }
}
