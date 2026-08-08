using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace SnipCopy
{
    static class IconMaker
    {
        static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage: IconMaker.exe <source.png> <output.ico>");
                return 1;
            }

            if (!File.Exists(args[0]))
            {
                Console.Error.WriteLine("Source image not found: " + args[0]);
                return 1;
            }

            try
            {
                CreateIcon(args[0], args[1]);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static void CreateIcon(string sourcePath, string outputPath)
        {
            int[] sizes = { 256, 128, 64, 48, 32, 16 };
            byte[][] images = new byte[sizes.Length][];

            using (var source = new Bitmap(sourcePath))
            {
                for (int i = 0; i < sizes.Length; i++)
                {
                    images[i] = RenderPng(source, sizes[i]);
                }
            }

            using (var stream = File.Create(outputPath))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)sizes.Length);

                int offset = 6 + (16 * sizes.Length);
                for (int i = 0; i < sizes.Length; i++)
                {
                    int size = sizes[i];
                    writer.Write((byte)(size == 256 ? 0 : size));
                    writer.Write((byte)(size == 256 ? 0 : size));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write((uint)images[i].Length);
                    writer.Write((uint)offset);
                    offset += images[i].Length;
                }

                for (int i = 0; i < images.Length; i++)
                {
                    writer.Write(images[i]);
                }
            }
        }

        private static byte[] RenderPng(Bitmap source, int size)
        {
            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var stream = new MemoryStream())
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                Rectangle sourceRect = GetCenteredSquare(source);
                g.DrawImage(source, new Rectangle(0, 0, size, size), sourceRect, GraphicsUnit.Pixel);

                bitmap.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }

        private static Rectangle GetCenteredSquare(Bitmap source)
        {
            int side = Math.Min(source.Width, source.Height);
            return new Rectangle(
                (source.Width - side) / 2,
                (source.Height - side) / 2,
                side,
                side);
        }
    }
}
