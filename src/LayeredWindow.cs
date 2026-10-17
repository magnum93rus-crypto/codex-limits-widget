using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CodexLimits {
    public static class LayeredWindow {
        [StructLayout(LayoutKind.Sequential)] struct POINT {public int X,Y;public POINT(int x,int y){X=x;Y=y;}}
        [StructLayout(LayoutKind.Sequential)] struct SIZE {public int Width,Height;public SIZE(int width,int height){Width=width;Height=height;}}
        [StructLayout(LayoutKind.Sequential,Pack=1)] struct BLEND {public byte Operation,Flags,Alpha,Format;}
        [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr window,IntPtr dc);
        [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr bitmap);
        [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr handle);
        [DllImport("user32.dll",SetLastError=true)]static extern bool UpdateLayeredWindow(IntPtr window,IntPtr destinationDC,ref POINT destination,ref SIZE size,IntPtr sourceDC,ref POINT source,int key,ref BLEND blend,int flags);
        public static bool Present(IntPtr window,Bitmap bitmap,Point location) {
            IntPtr screen=GetDC(IntPtr.Zero),memory=CreateCompatibleDC(screen),image=bitmap.GetHbitmap(Color.FromArgb(0)),previous=SelectObject(memory,image);
            try {
                var target=new POINT(location.X,location.Y);var origin=new POINT(0,0);var size=new SIZE(bitmap.Width,bitmap.Height);
                var blend=new BLEND {Operation=0,Flags=0,Alpha=255,Format=1};
                return UpdateLayeredWindow(window,screen,ref target,ref size,memory,ref origin,0,ref blend,2);
            }finally {SelectObject(memory,previous);DeleteObject(image);DeleteDC(memory);ReleaseDC(IntPtr.Zero,screen);}
        }
    }
}
