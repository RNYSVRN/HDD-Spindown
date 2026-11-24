// Draws the HDD-Spindown app icon in Windows 11 style and writes an .ico
// with all sizes from 16 to 256 px. Each size is drawn with 4x supersampling
// and then downscaled with high quality, so the icon stays crisp at large sizes too.
//
//   dotnet run --project tools/IconGen [output.ico] [preview-folder]

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

string output = args.Length > 0 ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/HddSpindown/Assets/app.ico"));
string? previewDir = args.Length > 1 ? args[1] : null;

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
var images = sizes.Select(s => (Size: s, Bitmap: Render(s))).ToList();

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using (var fs = File.Create(output)) WriteIco(fs, images);
Console.WriteLine($"geschrieben: {output}");

if (previewDir is not null)
{
    Directory.CreateDirectory(previewDir);
    foreach (var (s, bmp) in images) bmp.Save(Path.Combine(previewDir, $"icon_{s}.png"), ImageFormat.Png);
}

// ------------------------------------------------------------------

static Bitmap Render(int size)
{
    const int ss = 4; // supersampling
    int big = size * ss;
    using var hi = new Bitmap(big, big, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(hi))
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        g.ScaleTransform(big, big); // from here on: coordinates 0..1
        Draw(g, simple: size <= 24);
    }

    var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(result))
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.Transparent);
        g.DrawImage(hi, new Rectangle(0, 0, size, size));
    }
    return result;
}

static void Draw(Graphics g, bool simple)
{
    // --- Shadow ---
    using (var shadow = RoundRect(0.15f, 0.10f, 0.64f, 0.84f, 0.10f))
    using (var b = new SolidBrush(Color.FromArgb(46, 0, 0, 0)))
    {
        var m = new Matrix(); m.Translate(0.012f, 0.025f); shadow.Transform(m);
        g.FillPath(b, shadow);
    }

    // --- Case ---
    var body = new RectangleF(0.14f, 0.07f, 0.64f, 0.84f);
    using (var path = RoundRect(body.X, body.Y, body.Width, body.Height, 0.10f))
    {
        using var fill = new LinearGradientBrush(body, Color.FromArgb(0xF6, 0xF8, 0xFB), Color.FromArgb(0xC3, 0xCC, 0xD8), 90f);
        g.FillPath(fill, path);
        using var pen = new Pen(Color.FromArgb(150, 0x6E, 0x7A, 0x8A), simple ? 0.035f : 0.014f);
        g.DrawPath(pen, path);
    }
    // top highlight edge
    if (!simple)
        using (var hl = new Pen(Color.FromArgb(200, 255, 255, 255), 0.010f))
            g.DrawLine(hl, body.X + 0.10f, body.Y + 0.018f, body.Right - 0.10f, body.Y + 0.018f);

    // --- Platter ---
    var c = new PointF(0.46f, 0.40f);
    float r = 0.235f;
    var disk = new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r);
    using (var fill = new LinearGradientBrush(disk, Color.FromArgb(0xEE, 0xF2, 0xF7), Color.FromArgb(0xA7, 0xB3, 0xC3), 60f))
        g.FillEllipse(fill, disk);
    using (var pen = new Pen(Color.FromArgb(0x8E, 0x9A, 0xAA), simple ? 0.03f : 0.013f))
        g.DrawEllipse(pen, disk);
    if (!simple)
    {
        float r2 = 0.16f;
        using var ring = new Pen(Color.FromArgb(110, 0x5E, 0x6A, 0x7A), 0.009f);
        g.DrawEllipse(ring, c.X - r2, c.Y - r2, 2 * r2, 2 * r2);
    }
    float hub = simple ? 0.065f : 0.05f;
    using (var b = new SolidBrush(Color.FromArgb(0x6F, 0x7B, 0x8A)))
        g.FillEllipse(b, c.X - hub, c.Y - hub, 2 * hub, 2 * hub);

    // --- Read/write arm (pivot at the top right) ---
    if (!simple)
    {
        var pivot = new PointF(0.675f, 0.165f);
        var tip = new PointF(0.42f, 0.25f);
        using var arm = new Pen(Color.FromArgb(0x5F, 0x6B, 0x7A), 0.042f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(arm, pivot, tip);
        using var pb = new SolidBrush(Color.FromArgb(0x4E, 0x59, 0x67));
        g.FillEllipse(pb, pivot.X - 0.045f, pivot.Y - 0.045f, 0.09f, 0.09f);
    }

    // Variant without the badge (e.g. tray icon "all awake"): ICONGEN_NOBADGE=1
    if (Environment.GetEnvironmentVariable("ICONGEN_NOBADGE") == "1") return;

    // --- Badge: moon = asleep ---
    var bc = new PointF(0.715f, 0.715f);
    float br = 0.235f;
    var badge = new RectangleF(bc.X - br, bc.Y - br, 2 * br, 2 * br);
    float ringW = 0.035f;
    using (var white = new SolidBrush(Color.White))
        g.FillEllipse(white, RectangleF.Inflate(badge, ringW, ringW)); // separating ring
    using (var fill = new LinearGradientBrush(badge, Color.FromArgb(0x4C, 0xC2, 0xFF), Color.FromArgb(0x00, 0x5F, 0xB8), 90f))
        g.FillEllipse(fill, badge);

    // Crescent: large circle minus an offset circle (alternate fill inside the large circle)
    float mr = 0.125f;
    var moon = new RectangleF(bc.X - mr - 0.01f, bc.Y - mr + 0.005f, 2 * mr, 2 * mr);
    float cr = 0.105f;
    var cut = new RectangleF(moon.X + 0.085f, moon.Y - 0.035f, 2 * cr, 2 * cr);
    using (var crescent = new GraphicsPath(FillMode.Alternate))
    {
        crescent.AddEllipse(moon);
        crescent.AddEllipse(cut);
        var state = g.Save();
        using (var clip = new GraphicsPath()) { clip.AddEllipse(moon); g.SetClip(clip); }
        using var wb = new SolidBrush(Color.White);
        g.FillPath(wb, crescent);
        g.Restore(state);
    }
}

static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
{
    var p = new GraphicsPath();
    float d = 2 * r;
    p.AddArc(x, y, d, d, 180, 90);
    p.AddArc(x + w - d, y, d, d, 270, 90);
    p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
    p.AddArc(x, y + h - d, d, d, 90, 90);
    p.CloseFigure();
    return p;
}

// ICO: 256 px as PNG, smaller sizes as 32-bit DIB (maximum compatibility).
static void WriteIco(Stream s, List<(int Size, Bitmap Bitmap)> images)
{
    var blobs = images.Select(i => i.Size >= 256 ? Png(i.Bitmap) : Dib(i.Bitmap)).ToList();
    using var w = new BinaryWriter(s);
    w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)images.Count);
    int offset = 6 + 16 * images.Count;
    for (int i = 0; i < images.Count; i++)
    {
        int sz = images[i].Size;
        w.Write((byte)(sz >= 256 ? 0 : sz)); w.Write((byte)(sz >= 256 ? 0 : sz));
        w.Write((byte)0); w.Write((byte)0);
        w.Write((ushort)1); w.Write((ushort)32);
        w.Write(blobs[i].Length); w.Write(offset);
        offset += blobs[i].Length;
    }
    foreach (var b in blobs) w.Write(b);
}

static byte[] Png(Bitmap bmp)
{
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
}

static byte[] Dib(Bitmap bmp)
{
    int n = bmp.Width;
    int maskRow = ((n + 31) / 32) * 4;
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write(40); w.Write(n); w.Write(n * 2); w.Write((ushort)1); w.Write((ushort)32);
    w.Write(0); w.Write(n * n * 4 + maskRow * n); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
    for (int y = n - 1; y >= 0; y--)          // bottom-up, BGRA
        for (int x = 0; x < n; x++)
        {
            var c = bmp.GetPixel(x, y);
            w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
        }
    w.Write(new byte[maskRow * n]);           // AND mask empty (alpha handles transparency)
    return ms.ToArray();
}
