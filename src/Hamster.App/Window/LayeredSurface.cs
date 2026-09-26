using System.Drawing;
using System.Drawing.Imaging;
using static Hamster.App.Window.Native;

namespace Hamster.App.Window;

/// <summary>
/// La surface de la fenetre : une DIB 32 bits top-down en ARGB premultiplie,
/// poussee telle quelle par UpdateLayeredWindow. On ecrit les pixels a la main
/// plutot que de passer par GDI+ : c'est le seul moyen d'etre sur qu'aucun
/// filtrage bilineaire ne vienne flouter le pixel art.
/// </summary>
internal sealed class LayeredSurface : IDisposable
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public IntPtr Bits { get; private set; }

    IntPtr _dc, _bitmap, _oldBitmap;
    Bitmap? _gdiView;

    public unsafe Span<uint> Pixels => new((void*)Bits, Width * Height);

    /// <summary>Vue GDI+ sur la meme memoire, pour le seul texte de l'overlay de debug.</summary>
    public Bitmap GdiView => _gdiView ??= new Bitmap(Width, Height, Width * 4,
        PixelFormat.Format32bppPArgb, Bits);

    public void Ensure(int width, int height)
    {
        if (width == Width && height == Height && _dc != IntPtr.Zero) return;
        Release();

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = width;
        bmi.bmiHeader.biHeight = -height;     // negatif = top-down
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = BI_RGB;

        IntPtr screen = GetDC(IntPtr.Zero);
        _dc = CreateCompatibleDC(screen);
        ReleaseDC(IntPtr.Zero, screen);

        _bitmap = CreateDIBSection(_dc, ref bmi, DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
        if (_bitmap == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection a echoue");
        Bits = bits;
        _oldBitmap = SelectObject(_dc, _bitmap);
        Width = width;
        Height = height;
    }

    public void Clear() => Pixels.Clear();

    public void Present(IntPtr hwnd, int x, int y, byte alpha)
    {
        var dst = new POINT(x, y);
        var src = new POINT(0, 0);
        var size = new SIZE(Width, Height);
        var blend = new BLENDFUNCTION
        {
            BlendOp = AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = alpha,
            AlphaFormat = AC_SRC_ALPHA,
        };
        UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, _dc, ref src, 0, ref blend, ULW_ALPHA);
    }

    void Release()
    {
        _gdiView?.Dispose();
        _gdiView = null;
        if (_dc != IntPtr.Zero)
        {
            if (_oldBitmap != IntPtr.Zero) SelectObject(_dc, _oldBitmap);
            DeleteDC(_dc);
            _dc = IntPtr.Zero;
        }
        if (_bitmap != IntPtr.Zero) { DeleteObject(_bitmap); _bitmap = IntPtr.Zero; }
        Bits = IntPtr.Zero;
        Width = Height = 0;
    }

    public void Dispose() => Release();
}
