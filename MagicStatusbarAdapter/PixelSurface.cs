using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace MagicStatusbarAdapter;
// One top-down premultiplied DIB per size. No HBITMAP/DC reconstruction during a text transition.
internal sealed class PixelSurface : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Info{public uint Size;public int Width,Height;public ushort Planes,Bits;public uint Compression,ImageSize;public int X,Y;public uint Used,Important,Color;}
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc,ref Info info,uint usage,out nint bits,nint section,uint offset);
    internal static long Created;
    internal readonly int Width,Height;
    internal nint DC {get;private set;}
    private nint bitmap,bits,previous;
    internal PixelSurface(int width,int height){Width=width;Height=height;DC=Native.CreateCompatibleDC(0);var info=new Info{Size=40,Width=width,Height=-height,Planes=1,Bits=32,ImageSize=(uint)(width*height*4)};bitmap=CreateDIBSection(DC,ref info,0,out bits,0,0);if(bitmap==0||DC==0){Dispose();throw new System.ComponentModel.Win32Exception();}previous=Native.SelectObject(DC,bitmap);Created++;}
    internal unsafe void Upload(Bitmap source){var data=source.LockBits(new Rectangle(0,0,Width,Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);try{for(int row=0;row<Height;row++)Buffer.MemoryCopy((byte*)data.Scan0+row*data.Stride,(byte*)bits+row*Width*4,Width*4,Width*4);}finally{source.UnlockBits(data);}}
    public void Dispose(){if(DC!=0){if(previous!=0)Native.SelectObject(DC,previous);if(bitmap!=0)Native.DeleteObject(bitmap);Native.DeleteDC(DC);}DC=0;bitmap=0;bits=0;}
}
