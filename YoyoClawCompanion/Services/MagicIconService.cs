using System.IO;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace YoyoClawCompanion.Services;
internal static class MagicIconService
{
    private static ImageSource? cached;
    internal static ImageSource? Load(){
        if(cached!=null)return cached;
        var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"HONOR","MagicAnimation","MagicVisuals.exe");
        if(!File.Exists(path))return null;
        try{using var icon=Icon.ExtractAssociatedIcon(path);if(icon==null)return null;var image=Imaging.CreateBitmapSourceFromHIcon(icon.Handle,Int32Rect.Empty,BitmapSizeOptions.FromWidthAndHeight(64,64));image.Freeze();return cached=image;}catch{return null;}
    }
}
