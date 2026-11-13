using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

static class InkpadWindows
{
    const string Classes = @"Software\Classes\SystemFileAssociations\";
    const string Verb = @"\shell\Inkpad.Edit";
    static readonly string[] Extensions = { ".txt", ".md", ".log", ".csv", ".json", ".ini", ".inknote" };
    static readonly string Settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Inkpad", "window.txt");
    internal static string Executable { get { return Process.GetCurrentProcess().MainModule.FileName; } }
    internal static string EditCommand(string executable) { return "\"" + executable + "\" \"%1\""; }

    internal static bool ExplorerMenuEnabled()
    {
        string expected = EditCommand(Executable);
        try
        {
            return Extensions.All(extension =>
            {
                using (var key = Registry.CurrentUser.OpenSubKey(Classes + extension + Verb + @"\command"))
                    return key != null && string.Equals(key.GetValue("") as string, expected, StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (System.Security.SecurityException) { return false; }
    }

    internal static void SetExplorerMenu(bool enabled)
    {
        string executable = Executable;
        foreach (string extension in Extensions)
        {
            string path = Classes + extension + Verb;
            if (enabled)
            {
                using (var key = Registry.CurrentUser.CreateSubKey(path))
                {
                    key.SetValue("", "Edit in Inkpad");
                    key.SetValue("Icon", "\"" + executable + "\",0");
                    using (var command = key.CreateSubKey("command")) command.SetValue("", EditCommand(executable));
                }
            }
            else Registry.CurrentUser.DeleteSubKeyTree(path, false);
        }
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    internal static void Restore(Window window, string path = null)
    {
        Rect bounds;
        bool maximized;
        if (!ReadPlacement(path ?? Settings, out bounds, out maximized)) return;
        window.Width = Math.Max(window.MinWidth, Math.Min(bounds.Width, SystemParameters.VirtualScreenWidth));
        window.Height = Math.Max(window.MinHeight, Math.Min(bounds.Height, SystemParameters.VirtualScreenHeight));
        var title = new Rect(bounds.Left, bounds.Top, Math.Min(window.Width, 300), 40);
        using (var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
        {
            double scaleX = graphics.DpiX / 96.0, scaleY = graphics.DpiY / 96.0;
            bool visible = System.Windows.Forms.Screen.AllScreens.Any(screen =>
            {
                var work = screen.WorkingArea;
                var area = new Rect(work.Left / scaleX, work.Top / scaleY, work.Width / scaleX, work.Height / scaleY);
                area.Intersect(title);
                return area.Width >= 120 && area.Height >= 24;
            });
            if (visible)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = bounds.Left;
                window.Top = bounds.Top;
            }
            else
            {
                window.Width = Math.Max(window.MinWidth, Math.Min(window.Width, SystemParameters.WorkArea.Width));
                window.Height = Math.Max(window.MinHeight, Math.Min(window.Height, SystemParameters.WorkArea.Height));
            }
        }
        if (maximized) window.WindowState = WindowState.Maximized;
    }

    internal static bool ReadPlacement(string path, out Rect bounds, out bool maximized)
    {
        bounds = Rect.Empty;
        maximized = false;
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 512) return false;
            string[] parts = File.ReadAllLines(path);
            double x, y, width, height;
            if (parts.Length != 5 || !ReadNumber(parts[0], out x) || !ReadNumber(parts[1], out y) ||
                !ReadNumber(parts[2], out width) || !ReadNumber(parts[3], out height) || width <= 0 || height <= 0 ||
                !bool.TryParse(parts[4], out maximized)) return false;
            bounds = new Rect(x, y, width, height);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    static bool ReadNumber(string value, out double number)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && !double.IsNaN(number) && !double.IsInfinity(number);
    }

    internal static void SavePlacement(Rect bounds, bool maximized, string path = null)
    {
        if (bounds.IsEmpty || double.IsNaN(bounds.Left) || double.IsNaN(bounds.Top)) return;
        path = path ?? Settings;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(temporary, new[] { bounds.Left, bounds.Top, bounds.Width, bounds.Height }
                .Select(value => value.ToString("R", CultureInfo.InvariantCulture)).Concat(new[] { maximized.ToString() }));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
