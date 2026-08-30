using System.Runtime.InteropServices;
using Avalonia;

namespace Moovie.App.Web;

/// <summary>
/// Keeps the last frame sent to the browser and works out what part of a new one actually differs.
///
/// This is the difference between a usable remote window and a slideshow. Avalonia hands over a
/// whole repainted window every time anything changes, and most of those changes are tiny: a
/// caret blinking, a button lighting up under the cursor, one character appearing in a field.
/// Re-encoding and resending a megapixel for a blinking caret costs about 90 KB twice a second
/// and stalls everything behind it, so only the changed rectangle is sent.
/// </summary>
internal sealed class FrameDiffer
{
    private byte[] _previous = [];

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>
    /// How many unchanged rows may sit inside one reported band. Typing a character changes the
    /// field being typed in and, far away, the row in the file list that names it; one rectangle
    /// around both would cover most of the window and cost as much as sending all of it. Bands
    /// are kept separate unless the gap between them is too small to be worth another PNG.
    /// </summary>
    private const int MaxGap = 12;

    /// <summary>Past this many bands the saving stops paying for the per-patch overhead.</summary>
    private const int MaxBands = 12;

    /// <summary>
    /// Folds a new frame in and returns the parts that changed, empty if nothing did. A frame of a
    /// different size is reported whole, since the browser has to clear and resize its canvas for
    /// one and would otherwise be patching a picture it no longer holds.
    /// </summary>
    public IReadOnlyList<PixelRect> Absorb(byte[] data, int width, int height, int stride)
    {
        var rowBytes = width * 4;

        if (Width != width || Height != height || _previous.Length != rowBytes * height)
        {
            _previous = new byte[rowBytes * height];
            for (var y = 0; y < height; y++)
                Array.Copy(data, y * stride, _previous, y * rowBytes, rowBytes);

            Width = width;
            Height = height;
            return [new PixelRect(0, 0, width, height)];
        }

        var bands = new List<PixelRect>();
        int top = -1, bottom = -1, left = width, right = -1;

        void CloseBand()
        {
            if (top >= 0)
                bands.Add(new PixelRect(left, top, right - left + 1, bottom - top + 1));
            top = -1;
            left = width;
            right = -1;
        }

        for (var y = 0; y < height; y++)
        {
            var incoming = data.AsSpan(y * stride, rowBytes);
            var held = _previous.AsSpan(y * rowBytes, rowBytes);
            if (incoming.SequenceEqual(held))
            {
                if (top >= 0 && y - bottom > MaxGap)
                    CloseBand();
                continue;
            }

            if (top < 0)
                top = y;
            bottom = y;

            // Narrowing the columns is only worth doing on rows already known to differ, and it is
            // what makes a caret cost a sliver rather than the full width of the window.
            var incomingPixels = MemoryMarshal.Cast<byte, uint>(incoming);
            var heldPixels = MemoryMarshal.Cast<byte, uint>(held);

            var first = 0;
            while (incomingPixels[first] == heldPixels[first])
                first++;

            var last = width - 1;
            while (incomingPixels[last] == heldPixels[last])
                last--;

            if (first < left)
                left = first;
            if (last > right)
                right = last;

            incoming.CopyTo(held);
        }

        CloseBand();

        if (bands.Count <= MaxBands)
            return bands;

        var whole = bands[0];
        foreach (var band in bands)
            whole = whole.Union(band);
        return [whole];
    }

    /// <summary>Lifts a rectangle out of the frame as packed rows, ready to encode.</summary>
    public byte[] Crop(PixelRect rect)
    {
        var sourceRow = Width * 4;
        var targetRow = rect.Width * 4;
        var crop = new byte[targetRow * rect.Height];

        for (var y = 0; y < rect.Height; y++)
            Array.Copy(_previous, (rect.Y + y) * sourceRow + rect.X * 4, crop, y * targetRow, targetRow);

        return crop;
    }

    public PixelRect Whole => new(0, 0, Width, Height);
}
