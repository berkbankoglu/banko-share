using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BankoShare.Core;

namespace BankoShare.App;

/// <summary>What was on this machine's clipboard: text formats, an image (PNG) and/or copied paths.</summary>
internal readonly record struct CopiedClipboard(string? Text, string? Html, string? Rtf, byte[]? Image, string[]? Paths)
{
    public bool IsEmpty => Text == null && Html == null && Rtf == null && Image == null && Paths == null;
}

/// <summary>Reads and writes the Windows clipboard. UI (STA) thread only. Never throws for clipboard trouble.</summary>
internal static class ClipboardIO
{
    public static CopiedClipboard? Read(bool includeFiles)
    {
        IDataObject? data = null;
        for (var attempt = 0; attempt < 5 && data == null; attempt++)
            try { data = Clipboard.GetDataObject(); }
            catch (Exception e) when (e is COMException or ExternalException) { Thread.Sleep(20); } // another program holds it for a moment
        if (data == null) return null;
        string? Text(string format)
        {
            try { return data.GetDataPresent(format) && data.GetData(format) is string s && s.Length is > 0 and <= ClipboardContent.MaxTextChars ? s : null; }
            catch (Exception e) when (e is COMException or ExternalException or OutOfMemoryException or InvalidOperationException or ArgumentException) { return null; }
        }
        string[]? paths = null;
        if (includeFiles)
            try { paths = data.GetDataPresent(DataFormats.FileDrop) ? data.GetData(DataFormats.FileDrop) as string[] : null; }
            catch (Exception e) when (e is COMException or ExternalException or InvalidOperationException) { }
        byte[]? image = null;
        if (paths == null)
            try { if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } bitmap) image = EncodePng(bitmap); }
            catch (Exception e) when (e is COMException or ExternalException or OutOfMemoryException or InvalidOperationException or NotSupportedException or ArgumentException) { }
        if (image is { Length: > (int)ClipboardContent.MaxImageBytes }) image = null;
        var copied = new CopiedClipboard(Text(DataFormats.UnicodeText), Text(DataFormats.Html), Text(DataFormats.Rtf), image, paths is { Length: > 0 } ? paths : null);
        return copied.IsEmpty ? null : copied;
    }

    /// <summary>Puts received content on the clipboard; files as a copy-to-paste file list. False if the clipboard stayed busy.</summary>
    public static bool Write(ReceivedClipboard received)
    {
        var c = received.Content;
        var data = new DataObject();
        if (c.Text != null) { data.SetData(DataFormats.UnicodeText, c.Text); data.SetData(DataFormats.Text, c.Text); }
        if (c.Html != null) data.SetData(DataFormats.Html, c.Html);
        if (c.Rtf != null) data.SetData(DataFormats.Rtf, c.Rtf);
        if (received.Image != null)
        {
            if (DecodePng(received.Image) is { } bitmap) data.SetImage(bitmap);
            data.SetData("PNG", new MemoryStream(received.Image));
        }
        var paths = received.RootPaths;
        if (paths.Length > 0)
        {
            var list = new StringCollection();
            list.AddRange(paths);
            data.SetFileDropList(list);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1))); // DROPEFFECT_COPY
        }
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try { Clipboard.SetDataObject(data, true); return true; }
            catch (Exception e) when (e is COMException or ExternalException) { Thread.Sleep(30); }
        }
        return false;
    }

    /// <summary>
    /// Screenshots arrive as 32-bit bitmaps whose alpha byte is often zero; kept as is they would paste as a fully
    /// transparent picture. Clipboard images are opaque, so the alpha channel is dropped.
    /// </summary>
    public static byte[] EncodePng(BitmapSource bitmap)
    {
        BitmapSource opaque = bitmap.Format == PixelFormats.Bgr24 || bitmap.Format == PixelFormats.Bgr32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(opaque));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static BitmapSource? DecodePng(byte[] png)
    {
        try
        {
            var decoder = new PngBitmapDecoder(new MemoryStream(png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or ArgumentException or IOException) { return null; }
    }
}
