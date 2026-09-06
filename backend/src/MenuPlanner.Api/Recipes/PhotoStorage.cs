namespace MenuPlanner.Api.Recipes;

public sealed class PhotoStorage
{
    private readonly string _root;

    public PhotoStorage(IConfiguration configuration)
    {
        var dir = configuration["PHOTOS_DIR"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(dir)
            ? Path.Combine(Directory.GetCurrentDirectory(), "photos")
            : dir);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Guid recipeId, string extension, Stream stream)
    {
        var fileName = $"{recipeId:N}-{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(_root, fileName);
        await using var fileStream = new FileStream(fullPath, FileMode.CreateNew);
        await stream.CopyToAsync(fileStream);
        return fileName;
    }

    public void Delete(string? storedName)
    {
        var fullPath = SafePath(storedName);
        if (fullPath is null || !File.Exists(fullPath)) return;
        File.Delete(fullPath);
    }

    public string? ResolveReadPath(string fileName)
    {
        var fullPath = SafePath(fileName);
        return fullPath is not null && File.Exists(fullPath) ? fullPath : null;
    }

    private string? SafePath(string? storedName)
    {
        if (string.IsNullOrWhiteSpace(storedName)) return null;
        var name = Path.GetFileName(storedName);
        if (name != storedName) return null;
        return Path.Combine(_root, name);
    }
}