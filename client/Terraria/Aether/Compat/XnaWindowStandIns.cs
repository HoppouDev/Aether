// Compile-only stand-ins for the WinForms and WPF types that the game's XNA code paths use (its
// window as a WinForms Form, WeGame's WPF dispatcher). .NET 10 has neither outside Windows, and the
// client always runs on FNA, where those paths are dead: Program.IsXna is false, and the Linux
// build's own Terraria.Control.FromHandle already throws. Anything here that's reached throws,
// except Application.ApplicationExit, which ChromaInitializer subscribes to on Windows: it never
// fires (AppDomain.ProcessExit, which it also subscribes to, covers exiting).
using System;

namespace System.Windows.Forms
{
    internal enum FormBorderStyle
    {
        None,
        FixedSingle,
        Fixed3D,
        FixedDialog,
        Sizable,
        FixedToolWindow,
        SizableToolWindow
    }

    internal enum FormWindowState
    {
        Normal,
        Minimized,
        Maximized
    }

    internal class Form
    {
        private static PlatformNotSupportedException XnaOnly => new PlatformNotSupportedException("WinForms window (XNA only)");

        public static Form ActiveForm => throw XnaOnly;

        public System.Drawing.Point Location
        {
            get => throw XnaOnly;
            set => throw XnaOnly;
        }

        public FormBorderStyle FormBorderStyle
        {
            get => throw XnaOnly;
            set => throw XnaOnly;
        }

        public FormWindowState WindowState
        {
            get => throw XnaOnly;
            set => throw XnaOnly;
        }

        public int Width
        {
            get => throw XnaOnly;
            set => throw XnaOnly;
        }

        public int Height
        {
            get => throw XnaOnly;
            set => throw XnaOnly;
        }

        public System.Drawing.Rectangle Bounds => throw XnaOnly;

        public System.Drawing.Size MinimumSize
        {
            get => throw XnaOnly;
            set => throw XnaOnly;
        }

        public System.Drawing.Size ClientSize
        {
            get => throw XnaOnly;
            set => throw XnaOnly;
        }

        public void BringToFront()
        {
            throw XnaOnly;
        }

        public void SendToBack()
        {
            throw XnaOnly;
        }
    }

    internal static class Application
    {
        public static event EventHandler ApplicationExit
        {
            add
            {
            }
            remove
            {
            }
        }
    }

    internal class Screen
    {
        public static Screen[] AllScreens => throw new PlatformNotSupportedException("WinForms screen (XNA only)");

        public System.Drawing.Rectangle Bounds => throw new PlatformNotSupportedException("WinForms screen (XNA only)");

        public System.Drawing.Rectangle WorkingArea => throw new PlatformNotSupportedException("WinForms screen (XNA only)");

        public string DeviceName => throw new PlatformNotSupportedException("WinForms screen (XNA only)");

        public static Screen FromPoint(System.Drawing.Point point)
        {
            throw new PlatformNotSupportedException("WinForms screen (XNA only)");
        }
    }
}

namespace System.Windows.Threading
{
    internal class Dispatcher
    {
        public static Dispatcher CurrentDispatcher => throw new PlatformNotSupportedException("WPF dispatcher (WeGame only)");

        public void BeginInvoke(Action action)
        {
            throw new PlatformNotSupportedException("WPF dispatcher (WeGame only)");
        }
    }
}
