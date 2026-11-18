using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
static class P {
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint tid);
    [DllImport("kernel32.dll")] static extern uint GetUserDefaultLCID();
    [DllImport("kernel32.dll")] static extern uint GetSystemDefaultLCID();
    [DllImport("kernel32.dll")] static extern ushort GetUserDefaultUILanguage();
    [DllImport("user32.dll")] static extern int GetKeyboardLayoutList(int n, IntPtr[] list);
    [STAThread] static void Main() {
        Console.WriteLine($"GetKeyboardLayout(0) = 0x{GetKeyboardLayout(0).ToInt64():X}");
        var l = new IntPtr[16]; int n = GetKeyboardLayoutList(16, l);
        for (int i = 0; i < n; i++) Console.WriteLine($"  layout[{i}] = 0x{l[i].ToInt64():X}");
        Console.WriteLine($"GetUserDefaultLCID = 0x{GetUserDefaultLCID():X}  System = 0x{GetSystemDefaultLCID():X}  UILang = 0x{GetUserDefaultUILanguage():X}");
        Console.WriteLine($"CurrentCulture = {CultureInfo.CurrentCulture.Name} (LCID 0x{CultureInfo.CurrentCulture.LCID:X})  UICulture = {CultureInfo.CurrentUICulture.Name}");
        try { Console.WriteLine($"InputLanguage.Handle = 0x{InputLanguage.CurrentInputLanguage.Handle.ToInt64():X}"); Console.WriteLine($"InputLanguage.Culture = {InputLanguage.CurrentInputLanguage.Culture.Name}"); }
        catch (Exception e) { Console.WriteLine("InputLanguage.Culture threw: " + e.Message.Replace("\r\n", " | ")); }
        using (var f = new Form { ShowInTaskbar = false, Opacity = 0 }) { f.Show(); Application.DoEvents();
            Console.WriteLine($"[with window] Current.Handle = 0x{InputLanguage.CurrentInputLanguage.Handle.ToInt64():X}  Default.Handle = 0x{InputLanguage.DefaultInputLanguage.Handle.ToInt64():X}");
            try { Console.WriteLine($"[with window] Current.Culture = {InputLanguage.CurrentInputLanguage.Culture.Name}"); } catch (Exception e) { Console.WriteLine("Current.Culture threw: " + e.Message.Replace("\r\n", " | ")); }
            try { Console.WriteLine($"[with window] Default.Culture = {InputLanguage.DefaultInputLanguage.Culture.Name}"); } catch (Exception e) { Console.WriteLine("Default.Culture threw: " + e.Message.Replace("\r\n", " | ")); }
            Console.WriteLine($"InstalledInputLanguages = {InputLanguage.InstalledInputLanguages.Count}");
            f.Close(); }
        Console.WriteLine($"OSVersion = {Environment.OSVersion}  64bitOS={Environment.Is64BitOperatingSystem}");
    }
}
