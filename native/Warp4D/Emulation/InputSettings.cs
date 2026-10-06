using System.Runtime.InteropServices;
using System.Text.Json;

namespace Warp4D.Emulation;

internal sealed class InputSettings
{
    public Dictionary<string, Keys> Keyboard { get; set; } = Defaults();
    public bool ControllerEnabled { get; set; } = true;
    public int Volume { get; set; } = 100;
    public static Dictionary<string, Keys> Defaults() => new()
    {
        ["A"] = Keys.X, ["B"] = Keys.Z, ["Start"] = Keys.Enter, ["Select"] = Keys.RShiftKey,
        ["Up"] = Keys.Up, ["Down"] = Keys.Down, ["Left"] = Keys.Left, ["Right"] = Keys.Right
    };
    public int Mask(IReadOnlySet<Keys> held)
    {
        int mask = 0;
        foreach (NesButton button in Enum.GetValues<NesButton>())
            if (Keyboard.TryGetValue(button.ToString(), out Keys key) && held.Contains(key)) mask |= (int)button;
        return mask;
    }
    public bool IsMapped(Keys key) => Keyboard.Values.Contains(key);
    public InputSettings Clone() => new() { Keyboard = new(Keyboard), ControllerEnabled = ControllerEnabled, Volume = Volume };
    public void Normalize()
    {
        Keyboard ??= Defaults();
        foreach ((string name, Keys key) in Defaults())
            if (!Keyboard.TryGetValue(name, out Keys mapped) || !Enum.IsDefined(mapped)) Keyboard[name] = key;
        Volume = Math.Clamp(Volume, 0, 100);
    }
}
internal static class InputSettingsStore
{
    private static string PathName => Path.Combine(AppPaths.DataDirectory, "controls.json");
    public static InputSettings Load()
    {
        try { InputSettings settings = JsonSerializer.Deserialize<InputSettings>(File.ReadAllText(PathName)) ?? new(); settings.Normalize(); return settings; }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public static void Save(InputSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        string temp = PathName + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, PathName, true);
    }
}
internal static class ControllerInput
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Gamepad
    {
        public ushort Buttons; public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }
    [StructLayout(LayoutKind.Sequential)] private struct State { public uint Packet; public Gamepad Gamepad; }
    [DllImport("xinput1_4.dll")] private static extern uint XInputGetState(uint index, out State state);
    public static (int Mask, bool Connected) Poll()
    {
        try
        {
            for (uint index = 0; index < 4; index++) if (XInputGetState(index, out State state) == 0) return (Map(state.Gamepad), true);
        }
        catch (DllNotFoundException) { }
        return (0, false);
    }
    internal static int Map(Gamepad pad)
    {
        int mask = 0;
        if ((pad.Buttons & 0x1000) != 0) mask |= (int)NesButton.A;
        if ((pad.Buttons & (0x2000 | 0x4000)) != 0) mask |= (int)NesButton.B;
        if ((pad.Buttons & 0x10) != 0) mask |= (int)NesButton.Start;
        if ((pad.Buttons & 0x20) != 0) mask |= (int)NesButton.Select;
        if ((pad.Buttons & 1) != 0 || pad.LeftY > 12000) mask |= (int)NesButton.Up;
        if ((pad.Buttons & 2) != 0 || pad.LeftY < -12000) mask |= (int)NesButton.Down;
        if ((pad.Buttons & 4) != 0 || pad.LeftX < -12000) mask |= (int)NesButton.Left;
        if ((pad.Buttons & 8) != 0 || pad.LeftX > 12000) mask |= (int)NesButton.Right;
        return mask;
    }
}
