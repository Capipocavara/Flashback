/*
 * Original code by Kevin Weiner, FM Software.
 *                  Thomas Hourdel.
 */

using System;
using System.IO;
using UnityEngine;

namespace Flashback.Encoder
{
    public class GifEncoder
    {
        private readonly int width;
        private readonly int height;
        private readonly int repeat = -1; // -1: no repeat, 0: infinite, >0: repeat count
        private readonly int frameDelay; // Frame delay (milliseconds)
        private bool hasStarted;
        private FileStream fileStream;
        private byte[] pixels;
        private byte[] indexedPixels;
        private int colorDepth;
        private byte[] colorTab;
        private int paletteSize = 7; // Color table size (bits-1)
        private bool isFirstFrame = true;
        private readonly int sampleInterval; // Default sample interval for quantizer

        public GifEncoder(int width, int height, int repeat, int quality, int ms)
        {
            if (repeat >= 0) this.repeat = repeat;

            sampleInterval = Mathf.Clamp(quality, 1, 100);
            this.width = width;
            this.height = height;
            frameDelay = Mathf.RoundToInt(ms / 10f);
        }

        public void AddFrame(byte[] framePixels)
        {
            if (!hasStarted) throw new InvalidOperationException("Call Start() before adding frames to the gif.");

            pixels = framePixels ?? throw new ArgumentNullException(nameof(framePixels));
            AnalyzePixels();
            if (isFirstFrame) {
                WriteLsd();
                WritePalette();
                if (repeat >= 0) WriteNetscapeExt();
            }

            WriteGraphicCtrlExt();
            WriteImageDesc();
            if (!isFirstFrame) WritePalette();

            WritePixels();
            isFirstFrame = false;
        }

        public void Start(string file)
        {
            try {
                fileStream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
                if (fileStream == null) throw new ArgumentNullException(nameof(fileStream));

                WriteString("GIF89a");
                hasStarted = true;
            } catch (IOException) {
                throw new InvalidOperationException($"Can't start gif '{file}'.");
            }
        }

        public void Finish()
        {
            if (!hasStarted) throw new InvalidOperationException("Can't finish a non-started gif.");

            hasStarted = false;
            try {
                fileStream.WriteByte(0x3b);
                fileStream.Flush();
                fileStream.Close();
            } catch (IOException) {
                throw new InvalidOperationException("Can't finish gif. Write failed.");
            }

            fileStream = null;
            pixels = null;
            indexedPixels = null;
            colorTab = null;
            isFirstFrame = true;
        }

        private void AnalyzePixels()
        {
            var len = pixels.Length;
            var nPix = len / 3;
            indexedPixels = new byte[nPix];
            var nq = new NeuQuant(pixels, len, sampleInterval);
            colorTab = nq.Process();

            var k = 0;
            for (var i = 0; i < nPix; i++) {
                var index = nq.Map(pixels[k++] & 0xff, pixels[k++] & 0xff, pixels[k++] & 0xff);
                indexedPixels[i] = (byte)index;
            }

            pixels = null;
            colorDepth = 8;
            paletteSize = 7;
        }

        private void WriteGraphicCtrlExt()
        {
            fileStream.WriteByte(0x21);
            fileStream.WriteByte(0xf9);
            fileStream.WriteByte(4);
            fileStream.WriteByte(Convert.ToByte(0 | 0 | 0 | 0));
            WriteShort(frameDelay);
            fileStream.WriteByte(Convert.ToByte(0));
            fileStream.WriteByte(0);
        }

        private void WriteImageDesc()
        {
            fileStream.WriteByte(0x2c);
            WriteShort(0);
            WriteShort(0);
            WriteShort(width);
            WriteShort(height);
            if (isFirstFrame) fileStream.WriteByte(0);
            else fileStream.WriteByte(Convert.ToByte(0x80 | 0 | 0 | 0 | paletteSize));
        }

        private void WriteLsd()
        {
            WriteShort(width);
            WriteShort(height);
            fileStream.WriteByte(Convert.ToByte(0x80 | 0x70 | 0x00 | paletteSize));
            fileStream.WriteByte(0);
            fileStream.WriteByte(0);
        }

        private void WriteNetscapeExt()
        {
            fileStream.WriteByte(0x21);
            fileStream.WriteByte(0xff);
            fileStream.WriteByte(11);
            WriteString("NETSCAPE" + "2.0");
            fileStream.WriteByte(3);
            fileStream.WriteByte(1);
            WriteShort(repeat);
            fileStream.WriteByte(0);
        }

        private void WritePalette()
        {
            fileStream.Write(colorTab, 0, colorTab.Length);
            var n = 3 * 256 - colorTab.Length;
            for (var i = 0; i < n; i++) fileStream.WriteByte(0);
        }

        private void WritePixels()
        {
            var encoder = new LzwEncoder(indexedPixels, colorDepth);
            encoder.Encode(fileStream);
        }

        private void WriteShort(int value)
        {
            fileStream.WriteByte(Convert.ToByte(value & 0xff));
            fileStream.WriteByte(Convert.ToByte((value >> 8) & 0xff));
        }

        private void WriteString(String s)
        {
            var chars = s.ToCharArray();
            foreach (var t in chars) fileStream.WriteByte((byte)t);
        }
    }

    public class GifFrame
    {
        public int Width;
        public int Height;
        public Color32[] Data;
        public uint ID;

        public byte[] ExtractImagePixels()
        {
            var pixels = new Byte[3 * Width * Height];
            var count = 0;
            for (var th = Height - 1; th >= 0; th--)
            for (var tw = 0; tw < Width; tw++) {
                var color = Data[th * Width + tw];
                pixels[count] = color.r;
                count++;
                pixels[count] = color.g;
                count++;
                pixels[count] = color.b;
                count++;
            }

            return pixels;
        }
    }
}