$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing.Common,System.Drawing.Primitives,System.Runtime.InteropServices,System.Runtime -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class PortraitBounds {
 public static Rectangle Find(Bitmap b) {
  var d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
  try {
   byte[] a=new byte[Math.Abs(d.Stride)*b.Height]; Marshal.Copy(d.Scan0,a,0,a.Length);
   int x0=b.Width,y0=b.Height,x1=0,y1=0;
   for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++)if(a[y*d.Stride+x*4+3]>12){x0=Math.Min(x0,x);x1=Math.Max(x1,x);y0=Math.Min(y0,y);y1=Math.Max(y1,y);}
   if(x0>x1)return new Rectangle(0,0,b.Width,b.Height);
   x0=Math.Max(0,x0-12);y0=Math.Max(0,y0-12);x1=Math.Min(b.Width-1,x1+12);y1=Math.Min(b.Height-1,y1+12);
   return new Rectangle(x0,y0,x1-x0+1,y1-y0+1);
  }finally{b.UnlockBits(d);}
 }
}
'@
$wbRoot=Split-Path $PSScriptRoot
$releaseRoot=Join-Path (Split-Path $wbRoot) '深空娘化_剧情立绘Mod\Mod发布\Story'
$records=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'release-images.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach($rec in $records){
  $src=[Drawing.Image]::FromFile((Join-Path $releaseRoot $rec.rel))
  try {
    $rect=[PortraitBounds]::Find($src)
    foreach($size in @(@('p',300),@('t',96))){
      $target=Join-Path $wbRoot ('assets\'+$size[0]+'\release__'+$rec.rel.Replace('/','__'))
      $w=[int]$size[1]; $h=[Math]::Max(1,[int][Math]::Round($rect.Height*$w/$rect.Width))
      $bmp=New-Object Drawing.Bitmap($w,$h)
      $g=[Drawing.Graphics]::FromImage($bmp)
      try { $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic; $g.DrawImage($src,(New-Object Drawing.Rectangle(0,0,$w,$h)),$rect,[Drawing.GraphicsUnit]::Pixel); $bmp.Save($target,[Drawing.Imaging.ImageFormat]::Png) }
      finally {$g.Dispose();$bmp.Dispose()}
    }
  } finally {$src.Dispose()}
}
Write-Output ('Release thumbnails: '+$records.Count+' x 2')
