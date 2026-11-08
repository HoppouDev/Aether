using System;
using ReLogic.Localization.IME;

namespace ReLogic.OS.Windows;

// Aether: Windows under FNA. WindowsPlatform assumes XNA's WinForms window (message hook, Imm32
// IME, mouse notifier); with FNA the window is SDL's, so use the SDL-based services LinuxPlatform
// uses and keep only the Windows save path (Documents/My Games).
internal class FnaWindowsPlatform : Platform
{
    public FnaWindowsPlatform()
        : base(PlatformType.Windows)
    {
        RegisterService((IClipboard)new ReLogic.OS.Linux.Clipboard());
        RegisterService((IPathService)new PathService());
        RegisterService((IWindowService)new ReLogic.OS.Linux.WindowService());
        RegisterService((IImeService)new UnsupportedPlatformIme());
    }

    public override void InitializeClientServices(IntPtr windowHandle)
    {
        RegisterService((IImeService)new FnaIme());
    }
}
