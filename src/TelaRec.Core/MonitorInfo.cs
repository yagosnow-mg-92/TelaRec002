namespace TelaRec.Core;

/// <summary>Descreve um monitor detectado pelo Windows (coordenadas em pixels físicos do desktop virtual).</summary>
public sealed record MonitorInfo(
    int Number,
    string DeviceName,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsPrimary,
    int DpiPercent,
    int AdapterIndex = -1,
    int OutputIndex = -1)
{
    public string Name => $"Monitor {Number}";
    public string Resolution => $"{Width}×{Height}";
    public string Orientation => Height > Width ? "Retrato" : "Paisagem";

    public bool SameGeometryAs(MonitorInfo? other) =>
        other != null && DeviceName == other.DeviceName &&
        X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
}
