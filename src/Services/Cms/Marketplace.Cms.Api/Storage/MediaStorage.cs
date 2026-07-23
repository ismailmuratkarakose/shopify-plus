namespace Marketplace.Cms.Api.Storage;

/// <summary>
/// Medya deposu soyutlaması. Yerel dosya sistemi (geliştirme) ve ileride S3/MinIO
/// aynı arayüzün arkasında değiştirilebilir — `Media:Provider` ile seçilir.
/// </summary>
public interface IMediaStorage
{
    Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct);
    Task<Stream?> OpenAsync(string storagePath, CancellationToken ct);
    Task DeleteAsync(string storagePath, CancellationToken ct);
}

public sealed class LocalFileMediaStorage : IMediaStorage
{
    private readonly string _root;
    private readonly ILogger<LocalFileMediaStorage> _logger;

    public LocalFileMediaStorage(IConfiguration config, ILogger<LocalFileMediaStorage> logger)
    {
        _root = config["Media:LocalRoot"] ?? "/app/media";
        _logger = logger;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct)
    {
        // Ay bazlı klasör; dosya adı çakışmasın diye benzersiz önek.
        var safeName = Path.GetFileName(fileName);
        foreach (var c in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(c, '_');

        var relativeDir = DateTimeOffset.UtcNow.ToString("yyyy-MM");
        var relativePath = Path.Combine(relativeDir, $"{Guid.NewGuid():N}_{safeName}");
        var fullDir = Path.Combine(_root, relativeDir);
        Directory.CreateDirectory(fullDir);

        var fullPath = Path.Combine(_root, relativePath);
        await using (var fs = File.Create(fullPath))
            await content.CopyToAsync(fs, ct);

        _logger.LogInformation("Medya kaydedildi: {Path}", relativePath);
        return relativePath;
    }

    public Task<Stream?> OpenAsync(string storagePath, CancellationToken ct)
    {
        var fullPath = Path.Combine(_root, storagePath);
        if (!File.Exists(fullPath)) return Task.FromResult<Stream?>(null);
        return Task.FromResult<Stream?>(File.OpenRead(fullPath));
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct)
    {
        var fullPath = Path.Combine(_root, storagePath);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }
}
