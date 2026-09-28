using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class DesktopGuidesAppearanceProbe
{
    private const uint SpiSetHighContrast = 0x0043;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendChange = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrast
    {
        public uint Size;
        public uint Flags;
        public IntPtr DefaultScheme;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(
        uint action, uint parameter, ref HighContrast value, uint update);

    public static void SetHighContrast(uint flags, string scheme)
    {
        IntPtr schemePointer = string.IsNullOrEmpty(scheme)
            ? IntPtr.Zero
            : Marshal.StringToHGlobalUni(scheme);
        try
        {
            HighContrast value = new HighContrast
            {
                Size = (uint)Marshal.SizeOf(typeof(HighContrast)),
                Flags = flags,
                DefaultScheme = schemePointer
            };
            if (!SystemParametersInfoW(
                SpiSetHighContrast, value.Size, ref value,
                SpifUpdateIniFile | SpifSendChange))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "Could not set high contrast.");
            }
        }
        finally
        {
            if (schemePointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(schemePointer);
            }
        }
    }
}
