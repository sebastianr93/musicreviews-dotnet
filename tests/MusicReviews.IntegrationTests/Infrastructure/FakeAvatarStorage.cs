using MusicReviews.Application.Users;

namespace MusicReviews.IntegrationTests.Infrastructure;

/// <summary>
/// Guarda los avatares en memoria durante los tests.
/// </summary>
/// <remarks>
/// El almacenamiento real escribe filas con hasta dos megas de binario cada una. Lo que
/// se verifica en los tests de integracion es el flujo —validacion por bytes magicos,
/// tope de tamanio, que el perfil quede apuntando a la imagen nueva—, no que EF sepa
/// guardar un bytea.
/// </remarks>
internal sealed class FakeAvatarStorage : IAvatarStorage
{
    public Task<string> SaveAsync(
        Guid userId,
        ImageFormat format,
        Stream content,
        CancellationToken cancellationToken = default) =>
        Task.FromResult($"/avatars/{userId:N}-{Guid.NewGuid():N}{ImageSignature.ExtensionFor(format)}");

    public Task DeleteIfOwnedAsync(string? url, CancellationToken cancellationToken = default) =>
        // No hay nada que borrar.
        Task.CompletedTask;
}
