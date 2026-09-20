# Builds Resources\AppIcon\arctrix.ico from the wallet mark.
#
# The .ico is committed because the project references it as ApplicationIcon, which is what gives
# the executable, the taskbar and alt-tab their icon. Re-run this only if the mark changes.
#
# Small sizes are written as classic uncompressed DIB entries rather than PNG: Windows still has
# surfaces that refuse PNG-compressed entries and fall back to a blank document icon, which is what
# a PNG-only file looked like on the desktop. Only 128 and 256 are PNG, where it is universally
# supported and keeps the file small.
#
# The file is assembled in C# rather than PowerShell because BinaryWriter through PowerShell does
# not reliably write byte[] arrays whole, which silently produced a truncated icon.
[CmdletBinding()]
param(
    [string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent)
)
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class ArctrixIcon
{
    private static readonly Color Tile = Color.FromArgb(255, 8, 10, 14);   // #080A0E, the app's tile

    public static string Build(string markPath, string destination)
    {
        int[] dibSizes = { 16, 24, 32, 48 };
        int[] pngSizes = { 128, 256 };
        var images = new List<KeyValuePair<int, byte[]>>();
        var report = new List<string>();

        using (var mark = Image.FromFile(markPath))
        {
            foreach (var size in dibSizes)
                using (var tile = Render(mark, size))
                {
                    var bytes = ToDib(tile);
                    images.Add(new KeyValuePair<int, byte[]>(size, bytes));
                    report.Add(string.Format("  {0,3}px  DIB  {1,7:N0} bytes", size, bytes.Length));
                }

            foreach (var size in pngSizes)
                using (var tile = Render(mark, size))
                using (var buffer = new MemoryStream())
                {
                    tile.Save(buffer, ImageFormat.Png);
                    var bytes = buffer.ToArray();
                    images.Add(new KeyValuePair<int, byte[]>(size, bytes));
                    report.Add(string.Format("  {0,3}px  PNG  {1,7:N0} bytes", size, bytes.Length));
                }
        }

        using (var file = File.Create(destination))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write((ushort)0);              // reserved
            writer.Write((ushort)1);              // type: icon
            writer.Write((ushort)images.Count);

            var offset = 6 + (16 * images.Count);
            foreach (var image in images)
            {
                var dimension = image.Key >= 256 ? 0 : image.Key;   // 256 is recorded as 0
                writer.Write((byte)dimension);
                writer.Write((byte)dimension);
                writer.Write((byte)0);            // palette size: none
                writer.Write((byte)0);            // reserved
                writer.Write((ushort)1);          // colour planes
                writer.Write((ushort)32);         // bits per pixel
                writer.Write((uint)image.Value.Length);
                writer.Write((uint)offset);
                offset += image.Value.Length;
            }

            foreach (var image in images)
                writer.Write(image.Value);
        }

        return string.Join(Environment.NewLine, report);
    }

    /// <summary>The mark on the dark rounded tile, at one size.</summary>
    private static Bitmap Render(Image mark, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var radius = Math.Max(2, (int)(size * 0.18));
            var d = radius * 2;
            using (var path = new GraphicsPath())
            {
                path.AddArc(0, 0, d, d, 180, 90);
                path.AddArc(size - d, 0, d, d, 270, 90);
                path.AddArc(size - d, size - d, d, d, 0, 90);
                path.AddArc(0, size - d, d, d, 90, 90);
                path.CloseFigure();
                using (var brush = new SolidBrush(Tile))
                    g.FillPath(brush, path);
            }

            // The mark at 80% of the tile, centred - matching MauiIcon's ForegroundScale.
            var inset = (int)(size * 0.10);
            g.DrawImage(mark, inset, inset, size - (2 * inset), size - (2 * inset));
        }
        return bmp;
    }

    /// <summary>One image as an uncompressed 32-bit DIB: header, bottom-up BGRA rows, then the AND mask.</summary>
    private static byte[] ToDib(Bitmap bmp)
    {
        var size = bmp.Width;
        var maskStride = ((size + 31) / 32) * 4;    // 1bpp rows padded to 4 bytes

        using (var stream = new MemoryStream())
        using (var w = new BinaryWriter(stream))
        {
            w.Write((uint)40);                      // biSize
            w.Write(size);                          // biWidth
            w.Write(size * 2);                      // biHeight: colour data plus mask
            w.Write((ushort)1);                     // biPlanes
            w.Write((ushort)32);                    // biBitCount
            w.Write((uint)0);                       // biCompression: BI_RGB
            w.Write((uint)((size * size * 4) + (maskStride * size)));
            w.Write(0); w.Write(0); w.Write((uint)0); w.Write((uint)0);

            var data = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[size * 4];
                for (var y = size - 1; y >= 0; y--)
                {
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, row.Length);
                    w.Write(row);                   // already BGRA on little-endian Windows
                }
            }
            finally { bmp.UnlockBits(data); }

            // AND mask: all zero, because transparency comes from the alpha channel above.
            w.Write(new byte[maskStride * size]);
            w.Flush();
            return stream.ToArray();
        }
    }
}
'@

$markPath = Join-Path $ProjectRoot 'Resources\Images\arctrix_wallet_mark.png'
$destination = Join-Path $ProjectRoot 'Resources\AppIcon\arctrix.ico'
New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null

$report = [ArctrixIcon]::Build($markPath, $destination)
"Wrote $destination"
$report
"  total {0:N1} KB" -f ((Get-Item $destination).Length / 1KB)
