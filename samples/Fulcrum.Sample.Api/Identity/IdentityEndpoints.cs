using Microsoft.AspNetCore.Mvc;

namespace Fulcrum.Sample.Api.Identity;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentity(this IEndpointRouteBuilder app)
    {
        app.MapPost("/applicants/{id}/id-document", async (string id, IFormFile photo, [FromForm] string declaredType, IdCheck idCheck, CancellationToken ct) =>
        {
            using var bytes = new MemoryStream();
            await photo.CopyToAsync(bytes, ct);
            var result = await idCheck.RunAsync(new IdDocument(id, bytes.ToArray(), photo.ContentType, declaredType), ct);
            return Results.Ok(new { outcome = result.Outcome.ToString(), result.MatchedRule });
        }).DisableAntiforgery();
        return app;
    }
}
