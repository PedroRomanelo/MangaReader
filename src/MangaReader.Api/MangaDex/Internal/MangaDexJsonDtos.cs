namespace MangaReader.Api.MangaDex.Internal;

// DTOs internos que casam com o envelope JSON da MangaDex.
// Nomes em PascalCase; System.Text.Json (Web defaults) faz o match case-insensitive.

internal sealed class ListEnvelope<T>
{
    public string? Result { get; set; }
    public T[] Data { get; set; } = Array.Empty<T>();
    public int Limit { get; set; }
    public int Offset { get; set; }
    public int Total { get; set; }
}

internal sealed class MangaResource
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public MangaAttributes? Attributes { get; set; }
    public Relationship[] Relationships { get; set; } = Array.Empty<Relationship>();
}

internal sealed class MangaAttributes
{
    public Dictionary<string, string>? Title { get; set; }
    public int? Year { get; set; }
    public string? Status { get; set; }
    public string? ContentRating { get; set; }
}

internal sealed class ChapterResource
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public ChapterAttributes? Attributes { get; set; }
    public Relationship[] Relationships { get; set; } = Array.Empty<Relationship>();
}

internal sealed class ChapterAttributes
{
    public string? Volume { get; set; }
    public string? Chapter { get; set; }
    public string? Title { get; set; }
    public string TranslatedLanguage { get; set; } = "";
    public int Pages { get; set; }
    public DateTimeOffset? PublishAt { get; set; }
}

internal sealed class Relationship
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public RelationshipAttributes? Attributes { get; set; }
}

internal sealed class RelationshipAttributes
{
    public string? Name { get; set; }
    public string? FileName { get; set; }
}

internal sealed class AtHomeServerResponse
{
    public string? Result { get; set; }
    public string BaseUrl { get; set; } = "";
    public AtHomeChapter? Chapter { get; set; }
}

internal sealed class AtHomeChapter
{
    public string Hash { get; set; } = "";
    public string[] Data { get; set; } = Array.Empty<string>();
    public string[] DataSaver { get; set; } = Array.Empty<string>();
}
