using System;
using System.Drawing;
using System.IO;

namespace VideoRenamer
{
    // System.Drawing.Icon.ToBitmap() cannot reliably decode the PNG frames in a
    // modern multi-resolution ICO. The splash screen therefore reads the
    // largest embedded PNG frame directly, while window and shortcut icons keep
    // using the native ICO path.
    internal static class StartupIconPreview
    {
        private const int IconDirectorySize = 6;
        private const int IconEntrySize = 16;
        private static readonly byte[] PngSignature = new byte[]
        {
            137, 80, 78, 71, 13, 10, 26, 10
        };

        public static Image ExtractLargestPngLayer(byte[] iconBytes)
        {
            if (!HasValidIconDirectory(iconBytes))
            {
                return null;
            }

            int count = ReadUInt16(iconBytes, 4);
            int selectedOffset = -1;
            long selectedArea = -1;
            for (int index = 0; index < count; index++)
            {
                int entryOffset = IconDirectorySize + (index * IconEntrySize);
                long imageSize = ReadUInt32(iconBytes, entryOffset + 8);
                long imageOffset = ReadUInt32(iconBytes, entryOffset + 12);
                if (!IsPngLayer(iconBytes, imageOffset, imageSize))
                {
                    continue;
                }

                int width = iconBytes[entryOffset] == 0 ? 256 : iconBytes[entryOffset];
                int height = iconBytes[entryOffset + 1] == 0 ? 256 : iconBytes[entryOffset + 1];
                long area = (long)width * height;
                if (area > selectedArea)
                {
                    selectedArea = area;
                    selectedOffset = (int)imageOffset;
                }
            }

            if (selectedOffset < 0)
            {
                return null;
            }

            try
            {
                using (MemoryStream stream = new MemoryStream(iconBytes, selectedOffset, iconBytes.Length - selectedOffset, false))
                using (Image image = Image.FromStream(stream, false, true))
                {
                    return new Bitmap(image);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool HasValidIconDirectory(byte[] iconBytes)
        {
            if (iconBytes == null || iconBytes.Length < IconDirectorySize)
            {
                return false;
            }

            int count = ReadUInt16(iconBytes, 4);
            return ReadUInt16(iconBytes, 0) == 0
                && ReadUInt16(iconBytes, 2) == 1
                && count > 0
                && iconBytes.Length >= IconDirectorySize + (count * IconEntrySize);
        }

        private static bool IsPngLayer(byte[] iconBytes, long imageOffset, long imageSize)
        {
            if (imageSize < PngSignature.Length
                || imageOffset > iconBytes.Length
                || imageSize > iconBytes.Length - imageOffset)
            {
                return false;
            }

            int offset = (int)imageOffset;
            for (int index = 0; index < PngSignature.Length; index++)
            {
                if (iconBytes[offset + index] != PngSignature[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static int ReadUInt16(byte[] bytes, int offset)
        {
            return bytes[offset] | (bytes[offset + 1] << 8);
        }

        private static long ReadUInt32(byte[] bytes, int offset)
        {
            return bytes[offset]
                | ((long)bytes[offset + 1] << 8)
                | ((long)bytes[offset + 2] << 16)
                | ((long)bytes[offset + 3] << 24);
        }
    }
}
