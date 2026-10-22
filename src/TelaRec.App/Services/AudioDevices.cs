using NAudio.CoreAudioApi;

namespace TelaRec.Services;

internal sealed record MicDevice(string Id, string Name, bool IsDefault)
{
    public string Label => IsDefault ? Name + " (padrão)" : Name;
}

internal static class AudioDevices
{
    /// <summary>Lista todos os microfones (entradas de áudio) ativos no Windows.</summary>
    public static List<MicDevice> ListMics()
    {
        var result = new List<MicDevice>();
        using var en = new MMDeviceEnumerator();
        string? defId = null;
        try { defId = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console).ID; } catch { }
        foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            result.Add(new MicDevice(d.ID, d.FriendlyName, d.ID == defId));
        return result.OrderByDescending(m => m.IsDefault).ThenBy(m => m.Name).ToList();
    }
}
