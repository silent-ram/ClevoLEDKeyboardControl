using ColorfulLedKeyboard.Core;

Console.WriteLine("ClevoRGBControl zone test");
Console.WriteLine("This demo writes zone 1/2/3 independently: red, green, blue.");
Console.WriteLine("Run it next to InsydeDCHU.dll, or copy InsydeDCHU.dll into this output folder.");
Console.WriteLine();

var device = new DchuKeyboardDevice();

try
{
    device.SetZone(1, new RgbColor(255, 0, 0));
    Thread.Sleep(500);
    device.SetZone(2, new RgbColor(0, 255, 0));
    Thread.Sleep(500);
    device.SetZone(3, new RgbColor(0, 0, 255));

    Console.WriteLine("Expected result: left/middle/right zones show red/green/blue separately.");
    Console.WriteLine("Press Enter to turn all zones off and exit.");
    Console.ReadLine();
    device.SetAllZones(RgbColor.Black);
}
catch (DllNotFoundException)
{
    Console.WriteLine("InsydeDCHU.dll was not found. Copy it next to ColorfulLedKeyboard.ZoneTest.exe.");
    Environment.ExitCode = 1;
}
catch (Exception ex)
{
    Console.WriteLine($"Zone test failed: {ex.Message}");
    Environment.ExitCode = 1;
}
