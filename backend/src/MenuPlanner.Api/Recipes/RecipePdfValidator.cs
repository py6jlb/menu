namespace MenuPlanner.Api.Recipes;

/// <summary>
/// Проверяет, что загруженный файл — действительно PDF, по сигнатуре в начале
/// содержимого (а не по присланному ContentType/имени). Полноценный разбор PDF
/// не нужен: тип выдачи всегда один — application/pdf.
/// </summary>
public static class RecipePdfValidator
{
    public const string NotPdfError = "Файл не является PDF-документом.";

    private static readonly byte[] Signature = { 0x25, 0x50, 0x44, 0x46, 0x2D }; // "%PDF-"

    /// <summary>Поток должен поддерживать поиск (перед чтением выполняется pos=0).</summary>
    public static bool TryValidate(Stream stream)
    {
        if (!stream.CanSeek) return false;

        stream.Position = 0;
        Span<byte> header = stackalloc byte[Signature.Length];
        var read = stream.Read(header);
        stream.Position = 0;
        return read == Signature.Length && header.SequenceEqual(Signature);
    }
}
