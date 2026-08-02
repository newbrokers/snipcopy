using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace SnipCopy
{
    static class IconMaker
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: IconMaker.exe <output.ico>");
                return 1;
            }

            using (Icon icon = CreateIcon())
            using (FileStream stream = File.Create(args[0]))
            {
                icon.Save(stream);
            }

            return 0;
        }

        private static Icon CreateIcon()
        {
            using (var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var background = new LinearGradientBrush(new Rectangle(0, 0, 32, 32), Color.FromArgb(36, 132, 255), Color.FromArgb(20, 184, 166), 45f))
            using (var whitePen = new Pen(Color.White, 2.6f))
            using (var darkPen = new Pen(Color.FromArgb(22, 52, 78), 2.2f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (GraphicsPath shape = RoundedRect(new Rectangle(3, 3, 26, 26), 7))
                {
                    g.FillPath(background, shape);
                }

                whitePen.StartCap = LineCap.Round;
                whitePen.EndCap = LineCap.Round;
                g.DrawLine(whitePen, 10, 19, 22, 7);
                g.DrawLine(whitePen, 10, 13, 22, 25);
                g.DrawEllipse(darkPen, 6, 10, 7, 7);
                g.DrawEllipse(darkPen, 6, 17, 7, 7);
                g.DrawLine(darkPen, 15, 16, 25, 16);

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
