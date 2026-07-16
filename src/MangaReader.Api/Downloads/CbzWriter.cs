using System.IO.Compression;

namespace MangaReader.Api.Downloads;

// Escreve páginas num arquivo temporário .cbz.tmp e move para o path final
// só no Finalize — se o processo cair no meio, não sobra um .cbz "válido".
// Sem compressão: imagens JPG/PNG/WebP já vêm comprimidas (é uma das
// decisões documentadas em docs/ARQUITETURA.md §3).
public sealed class CbzWriter : IDisposable
{
    public string FinalPath { get; }
    public string TempPath { get; }

    private readonly FileStream _file;
    private readonly ZipArchive _zip;
    private bool _finalized;

    public CbzWriter(string finalPath)
    {
        FinalPath = finalPath;
        TempPath = finalPath + ".tmp";

        var dir = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _file = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None);
        _zip = new ZipArchive(_file, ZipArchiveMode.Create, leaveOpen: false);
    }

    public void AddPage(int oneBasedIndex, string extension, ReadOnlySpan<byte> bytes)
    {
        var name = $"{oneBasedIndex:D3}{NormalizeExtension(extension)}";
        var entry = _zip.CreateEntry(name, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    public long FinalizeAndMove()
    {
        _zip.Dispose();     // fecha o zip (grava central directory) e o file (leaveOpen: false)
        _finalized = true;

        if (File.Exists(FinalPath)) File.Delete(FinalPath);
        File.Move(TempPath, FinalPath);
        return new FileInfo(FinalPath).Length;
    }

    public void Dispose()
    {
        if (_finalized) return;
        try { _zip.Dispose(); } catch { /* ignore */ }
        try { if (File.Exists(TempPath)) File.Delete(TempPath); } catch { /* ignore */ }
    }

    private static string NormalizeExtension(string ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) return ".jpg";
        return ext.StartsWith('.') ? ext : "." + ext;
    }
}
