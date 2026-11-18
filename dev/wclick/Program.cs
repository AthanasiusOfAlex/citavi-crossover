using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

static class P
{
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, StringBuilder l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr h);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    struct RECT { public int L, T, R, B; }
    struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern IntPtr ChildWindowFromPointEx(IntPtr h, POINT p, uint flags);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    // for `icon`: reading the icons a program file carries
    delegate bool ResNameProc(IntPtr module, IntPtr type, IntPtr name, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool EnumResourceNames(IntPtr module, IntPtr type, ResNameProc cb, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr res);
    [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr h);
    [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr res);
    static byte[] Res(IntPtr module, IntPtr name, int type) {
        IntPtr r = FindResource(module, name, (IntPtr)type); if (r == IntPtr.Zero) return null;
        IntPtr p = LockResource(LoadResource(module, r)); if (p == IntPtr.Zero) return null;
        var b = new byte[SizeofResource(module, r)]; Marshal.Copy(p, b, 0, b.Length); return b;
    }
    // Writes the largest picture of a program file's first icon as a PNG and prints its size.
    // An icon is a group (resource type 14) that lists its pictures (type 3) by number and size;
    // a width of 0 stands for 256. The 256-pixel picture is normally a PNG already; a smaller
    // one is an old-style bitmap, which System.Drawing converts.
    static int IconToPng(string exe, string png) {
        IntPtr m = LoadLibraryEx(exe, IntPtr.Zero, 2 /* as a data file: nothing in it is run */);
        if (m == IntPtr.Zero) { Console.Error.WriteLine("cannot open " + exe); return 1; }
        byte[] group = null;
        EnumResourceNames(m, (IntPtr)14, (mod, type, name, l) => { group = Res(mod, name, 14); return false; }, IntPtr.Zero);
        if (group == null || group.Length < 6) { Console.Error.WriteLine("no icon in " + exe); return 1; }
        int best = -1, bestW = 0, bestBits = 0;
        for (int i = 0, n = BitConverter.ToUInt16(group, 4); i < n && 6 + 14 * i + 14 <= group.Length; i++) {
            int o = 6 + 14 * i, w = group[o] == 0 ? 256 : group[o], bits = BitConverter.ToUInt16(group, o + 6);
            if (w > bestW || (w == bestW && bits > bestBits)) { best = o; bestW = w; bestBits = bits; }
        }
        if (best < 0) return 1;
        byte[] pic = Res(m, (IntPtr)BitConverter.ToUInt16(group, best + 12), 3);
        if (pic == null) return 1;
        if (pic.Length > 8 && pic[0] == 0x89 && pic[1] == 0x50 && pic[2] == 0x4E && pic[3] == 0x47) System.IO.File.WriteAllBytes(png, pic);
        else {
            var ico = new System.IO.MemoryStream();          // a one-picture .ico file: 6 + 16 bytes of header, then the picture
            var w = new System.IO.BinaryWriter(ico);
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)1);
            w.Write(group, best, 12); w.Write((uint)22); w.Write(pic); w.Flush(); ico.Position = 0;
            using (var icon = new System.Drawing.Icon(ico, bestW, bestW)) using (var bmp = icon.ToBitmap())
                bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        }
        Console.WriteLine($"{bestW}x{bestW}");
        return 0;
    }
    // deepest visible child of top-level window `top` at window-relative point (x,y)
    static IntPtr Deepest(IntPtr top, int x, int y, out POINT client) {
        GetWindowRect(top, out RECT r); var screen = new POINT { X = r.L + x, Y = r.T + y };
        IntPtr cur = top;
        while (true) {
            var p = screen; ScreenToClient(cur, ref p);
            IntPtr next = ChildWindowFromPointEx(cur, p, 0x0001 | 0x0002 /* skip invisible, disabled */);
            if (next == IntPtr.Zero || next == cur) break;
            cur = next;
        }
        client = screen; ScreenToClient(cur, ref client);
        return cur;
    }

    static string Cls(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
    static string Txt(IntPtr h) { var s = new StringBuilder(2048); SendMessage(h, 0x000D, (IntPtr)2048, s); return s.ToString().Replace("\r", " ").Replace("\n", " "); }
    static int Depth(IntPtr h, IntPtr top) { int d = 0; while (h != top && h != IntPtr.Zero && d < 30) { h = GetParent(h); d++; } return d; }

    static int Main(string[] a)
    {
        // Citavi is DPI-aware; without this, with Retina mode on (192 dpi) the sizes that `list`
        // prints would be halved while clicks are still delivered in real pixels.
        try { SetProcessDPIAware(); } catch { }
        if (a.Length == 0) { Console.WriteLine("usage: wclick list [proc] | click <hwnd-hex> [x y] | bm <hwnd-hex> | key <hwnd-hex> <vk-hex> | close <hwnd-hex> | icon <exe> <out.png> | text <hwnd-hex> <string> | type <hwnd-hex> <string> | chord <top-hwnd-hex> <vk-hex>..."); return 2; }
        switch (a[0])
        {
            case "list":
                string filter = a.Length > 1 ? a[1].ToLowerInvariant() : "citavi";
                var pids = new Dictionary<uint, string>();
                foreach (var p in Process.GetProcesses()) { try { pids[(uint)p.Id] = p.ProcessName; } catch { } }
                EnumWindows((h, l) =>
                {
                    GetWindowThreadProcessId(h, out uint pid);
                    pids.TryGetValue(pid, out string pn); pn = pn ?? "?";
                    if (!pn.ToLowerInvariant().Contains(filter)) return true;
                    if (!IsWindowVisible(h)) return true;
                    GetWindowRect(h, out RECT r);
                    Console.WriteLine($"TOP {h.ToInt64():X} pid={pid} [{Cls(h)}] \"{Txt(h)}\" {r.L},{r.T} {r.R - r.L}x{r.B - r.T}");
                    EnumChildWindows(h, (c, l2) =>
                    {
                        if (!IsWindowVisible(c)) return true;
                        GetWindowRect(c, out RECT cr);
                        string t = Txt(c); if (t.Length > 100) t = t.Substring(0, 100) + "...";
                        Console.WriteLine($"{new string(' ', 2 * Depth(c, h))}{c.ToInt64():X} [{Cls(c)}] \"{t}\" {cr.L - r.L},{cr.T - r.T} {cr.R - cr.L}x{cr.B - cr.T}{(IsWindowEnabled(c) ? "" : " DISABLED")}");
                        return true;
                    }, IntPtr.Zero);
                    return true;
                }, IntPtr.Zero);
                return 0;
            case "click":
                {
                    var h = (IntPtr)Convert.ToInt64(a[1], 16);
                    GetWindowRect(h, out RECT r);
                    int x = a.Length > 3 ? int.Parse(a[2]) : (r.R - r.L) / 2, y = a.Length > 3 ? int.Parse(a[3]) : (r.B - r.T) / 2;
                    var lp = (IntPtr)((y << 16) | (x & 0xFFFF));
                    PostMessage(h, 0x0200, IntPtr.Zero, lp);
                    PostMessage(h, 0x0201, (IntPtr)1, lp);
                    PostMessage(h, 0x0202, IntPtr.Zero, lp);
                    return 0;
                }
            case "gettext": { var sb = new StringBuilder(65536); SendMessage((IntPtr)Convert.ToInt64(a[1], 16), 0x000D, (IntPtr)65536, sb); Console.WriteLine(sb.ToString()); return 0; }
            case "at": case "dblat": case "rat":
                {
                    var top = (IntPtr)Convert.ToInt64(a[1], 16);
                    var h = Deepest(top, int.Parse(a[2]), int.Parse(a[3]), out POINT c);
                    var lp = (IntPtr)((c.Y << 16) | (c.X & 0xFFFF));
                    Console.WriteLine($"target {h.ToInt64():X} [{Cls(h)}] client {c.X},{c.Y}");
                    PostMessage(h, 0x0200, IntPtr.Zero, lp);
                    if (a[0] == "rat") { PostMessage(h, 0x0204, (IntPtr)2, lp); PostMessage(h, 0x0205, IntPtr.Zero, lp); return 0; }
                    PostMessage(h, 0x0201, (IntPtr)1, lp); PostMessage(h, 0x0202, IntPtr.Zero, lp);
                    if (a[0] == "dblat") { PostMessage(h, 0x0203, (IntPtr)1, lp); PostMessage(h, 0x0202, IntPtr.Zero, lp); }
                    return 0;
                }
            case "bm": PostMessage((IntPtr)Convert.ToInt64(a[1], 16), 0x00F5, IntPtr.Zero, IntPtr.Zero); return 0;
            case "key":
                {
                    var h = (IntPtr)Convert.ToInt64(a[1], 16); var vk = (IntPtr)Convert.ToInt32(a[2], 16);
                    PostMessage(h, 0x0100, vk, IntPtr.Zero); PostMessage(h, 0x0101, vk, IntPtr.Zero); return 0;
                }
            case "icon": return IconToPng(a[1], a[2]);   // icon <program.exe> <out.png>: its largest icon as a PNG
            case "close": PostMessage((IntPtr)Convert.ToInt64(a[1], 16), 0x0010, IntPtr.Zero, IntPtr.Zero); return 0;
            case "text": SendMessage((IntPtr)Convert.ToInt64(a[1], 16), 0x000C, IntPtr.Zero, new StringBuilder(string.Join(" ", a, 2, a.Length - 2))); return 0;
            case "chord": // a real key combination for the bottle's foreground window, e.g. chord <top-hwnd> 12 39 = Alt+9
                {                 // (keybd_event, so shortcuts that test the modifier state see it; stays inside the bottle)
                    SetForegroundWindow((IntPtr)Convert.ToInt64(a[1], 16)); System.Threading.Thread.Sleep(300);
                    var vks = new List<byte>(); for (int i = 2; i < a.Length; i++) vks.Add(Convert.ToByte(a[i], 16));
                    foreach (var vk in vks) { keybd_event(vk, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(40); }
                    vks.Reverse();
                    foreach (var vk in vks) { keybd_event(vk, 0, 2, UIntPtr.Zero); System.Threading.Thread.Sleep(40); }
                    return 0;
                }
            case "focus": // give a control the keyboard focus without clicking it
                {
                    var h = (IntPtr)Convert.ToInt64(a[1], 16);
                    uint other = GetWindowThreadProcessId(h, out uint _), me = GetCurrentThreadId();
                    AttachThreadInput(me, other, true); SetFocus(h); AttachThreadInput(me, other, false);
                    return 0;
                }
            case "post": // post <hwnd> <msg-hex> [wparam-hex] [lparam-hex], e.g. post <hwnd> 2A3 = WM_MOUSELEAVE
                PostMessage((IntPtr)Convert.ToInt64(a[1], 16), Convert.ToUInt32(a[2], 16),
                    (IntPtr)(a.Length > 3 ? Convert.ToInt64(a[3], 16) : 0), (IntPtr)(a.Length > 4 ? Convert.ToInt64(a[4], 16) : 0));
                return 0;
            case "type": // as if typed: one WM_CHAR per character (use after `click` has given the control the focus)
                {
                    var h = (IntPtr)Convert.ToInt64(a[1], 16);
                    foreach (char ch in string.Join(" ", a, 2, a.Length - 2)) PostMessage(h, 0x0102, (IntPtr)ch, (IntPtr)1);
                    return 0;
                }
        }
        return 2;
    }
}
