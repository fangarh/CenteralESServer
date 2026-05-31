namespace CenteralES.Storage;

public interface ITemporaryFileStore
{
    Task SaveAsync(string key, Stream content, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);

    Task DeleteIfExistsAsync(string key, CancellationToken cancellationToken);
}
