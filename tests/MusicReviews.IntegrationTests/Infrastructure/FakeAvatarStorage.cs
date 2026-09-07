using MusicReviews.Application.Users;

namespace MusicReviews.IntegrationTests.Infrastructure;

/// <summary>
/// Guarda los avatares en memoria durante los tests.
/// </summary>
/// <remarks>
/// El almacenamiento real escribe archivos: en una suite eso deja basura en el disco de
/// quien corre los tests y hace que el resultado dependa de permisos y espacio libre.
/// Lo que se verifica en los tests de integracion es el flujo —validacion, tope de
/// tamanio, que el perfil quede apuntando al archivo nuevo—, no que File.Create funcione.
/// </remarks>
internal sealed class FakeAvatarStorage : IAvatarStorage
{
    public Task<string> SaveAsync(
        Guid userId,
        ImageFormat format,
        Stream content,
        CancellationToken cancellationToken = default) =>
        Task.FromResult($"/avatars/{userId:N}-{Guid.NewGuid():N}{ImageSignature.ExtensionFor(format)}");

    public void DeleteIfOwned(string? url)
    {
        // No hay nada que borrar.
    }
}
