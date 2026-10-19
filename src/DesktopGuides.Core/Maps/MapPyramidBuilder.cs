namespace DesktopGuides.Core.Maps;

/// <summary>
/// T21.2: cuts a map into the tiles of its levels in one top-to-bottom pass.
/// It takes level 0's rows in bands of premultiplied BGRA, emits each tile
/// once its rows are in, and makes each next level by averaging 2 × 2
/// blocks (an odd last column or row averages what exists). Each level holds
/// one band of tile rows and one row waiting for its pair.
/// </summary>
public sealed class MapPyramidBuilder
{
    private readonly int tileSize;
    private readonly Level[] levels;
    private readonly Action<MapTileKey, int, int, byte[]> tile;

    public MapPyramidBuilder(int width, int height, int tileSize, int levels, Action<MapTileKey, int, int, byte[]> tile)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(levels);
        ArgumentNullException.ThrowIfNull(tile);
        this.tileSize = tileSize;
        this.tile = tile;
        this.levels = new Level[levels];
        for (int k = 0; k < levels; k++)
        {
            this.levels[k] = new Level(width, height, tileSize);
            width = (width + 1) / 2;
            height = (height + 1) / 2;
        }
    }

    public MapPyramidBuilder(MapPyramid pyramid, Action<MapTileKey, int, int, byte[]> tile)
        : this(pyramid.Width, pyramid.Height, pyramid.TileSize, Math.Max(1, pyramid.Levels), tile)
    {
    }

    public void AddBand(ReadOnlySpan<byte> bgra, int rows)
    {
        int stride = levels[0].Width * 4;
        if (rows < 0 || bgra.Length != rows * stride)
        {
            throw new ArgumentException("A band is whole rows of the map's width.", nameof(bgra));
        }
        for (int row = 0; row < rows; row++)
        {
            AddRow(0, bgra.Slice(row * stride, stride));
        }
    }

    public void Complete()
    {
        for (int k = 0; k < levels.Length; k++)
        {
            Level level = levels[k];
            if (level.Pending is byte[] lone && k + 1 < levels.Length)
            {
                AddRow(k + 1, Average(lone, null, level.Width));
                level.Pending = null;
            }
            if (level.Buffered > 0) Emit(k);
        }
    }

    private void AddRow(int k, ReadOnlySpan<byte> row)
    {
        Level level = levels[k];
        if (level.Added == level.Height)
        {
            throw new InvalidOperationException("More rows than the map has.");
        }
        int stride = level.Width * 4;
        row.CopyTo(level.Band.AsSpan(level.Buffered * stride, stride));
        level.Buffered++;
        level.Added++;
        if (level.Buffered == tileSize || level.Added == level.Height) Emit(k);
        if (k + 1 == levels.Length) return;
        if (level.Pending is null)
        {
            level.Pending = row.ToArray();
            return;
        }
        byte[] upper = level.Pending;
        level.Pending = null;
        AddRow(k + 1, Average(upper, row, level.Width));
    }

    private void Emit(int k)
    {
        Level level = levels[k];
        int bandRow = (level.Added - 1) / tileSize;
        int height = level.Buffered;
        int stride = level.Width * 4;
        for (int column = 0; column * tileSize < level.Width; column++)
        {
            int left = column * tileSize;
            int width = Math.Min(tileSize, level.Width - left);
            byte[] pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                level.Band.AsSpan(y * stride + left * 4, width * 4).CopyTo(pixels.AsSpan(y * width * 4));
            }
            tile(new MapTileKey(k, column, bandRow), width, height, pixels);
        }
        level.Buffered = 0;
    }

    private static byte[] Average(ReadOnlySpan<byte> upper, ReadOnlySpan<byte> lower, int width)
    {
        bool hasLower = !lower.IsEmpty;
        int outWidth = (width + 1) / 2;
        byte[] result = new byte[outWidth * 4];
        for (int x = 0; x < outWidth; x++)
        {
            int left = 2 * x, right = Math.Min(2 * x + 1, width - 1);
            int count = (right > left ? 2 : 1) * (hasLower ? 2 : 1);
            for (int c = 0; c < 4; c++)
            {
                int sum = upper[left * 4 + c] + (right > left ? upper[right * 4 + c] : 0);
                if (hasLower) sum += lower[left * 4 + c] + (right > left ? lower[right * 4 + c] : 0);
                result[x * 4 + c] = (byte)((sum + count / 2) / count);
            }
        }
        return result;
    }

    private sealed class Level(int width, int height, int tileSize)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public byte[] Band { get; } = new byte[(long)width * Math.Min(tileSize, height) * 4];
        public int Buffered { get; set; }
        public int Added { get; set; }
        public byte[]? Pending { get; set; }
    }
}
