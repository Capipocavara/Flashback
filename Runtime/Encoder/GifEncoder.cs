/*
 * The MIT License (MIT)
 * 
 * Copyright (c) 2015 Simon Wittber
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using System;
using System.IO;
using UnityEngine;

namespace Flashback.Encoder
{
    public class GifEncoder
    {
        private const int PaletteSize = 7;
        private const int ColorDepth = 8;
        
        private readonly int width;
        private readonly int height;
        private readonly int repeat;
        private readonly int frameDelay;
        private readonly int sampleInterval;

        private bool hasStarted;
        private FileStream fileStream;
        private byte[] pixels;
        private byte[] indexedPixels;
        private byte[] colorTab;
        private bool isFirstFrame;

        public GifEncoder(int initWidth, int initHeight, int initRepeat, int quality, int ms)
        {
            width = initWidth;
            height = initHeight;
            repeat = Mathf.Max(-1, initRepeat);
            frameDelay = Mathf.RoundToInt(ms / 10f);
            sampleInterval = Mathf.Clamp(quality, 1, 100);
            isFirstFrame = true;
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
        
        public void AddFrame(byte[] framePixels)
        {
            if (!hasStarted) throw new InvalidOperationException("Call Start() before adding frames to the gif.");

            pixels = framePixels ?? throw new ArgumentNullException(nameof(framePixels));
            
            // Analyze pixels
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
            if (isFirstFrame) {
                // Write Lsd
                WriteShort(width);
                WriteShort(height);
                fileStream.WriteByte(Convert.ToByte(0x80 | 0x70 | 0x00 | PaletteSize));
                fileStream.WriteByte(0);
                fileStream.WriteByte(0);
                
                // Write palette
                fileStream.Write(colorTab, 0, colorTab.Length);
                var n = 3 * 256 - colorTab.Length;
                for (var i = 0; i < n; i++) fileStream.WriteByte(0);
                
                if (repeat >= 0) {
                    // Write Netscape ext
                    fileStream.WriteByte(0x21);
                    fileStream.WriteByte(0xff);
                    fileStream.WriteByte(11);
                    WriteString("NETSCAPE" + "2.0");
                    fileStream.WriteByte(3);
                    fileStream.WriteByte(1);
                    WriteShort(repeat);
                    fileStream.WriteByte(0);
                }
            }

            // Write graphic ctrl ext;
            fileStream.WriteByte(0x21);
            fileStream.WriteByte(0xf9);
            fileStream.WriteByte(4);
            fileStream.WriteByte(Convert.ToByte(0 | 0 | 0 | 0));
            WriteShort(frameDelay);
            fileStream.WriteByte(Convert.ToByte(0));
            fileStream.WriteByte(0);
            
            // Write image desc
            fileStream.WriteByte(0x2c);
            WriteShort(0);
            WriteShort(0);
            WriteShort(width);
            WriteShort(height);
            if (isFirstFrame) fileStream.WriteByte(0);
            else fileStream.WriteByte(Convert.ToByte(0x80 | 0 | 0 | 0 | PaletteSize));
            
            if (!isFirstFrame) {
                // Write palette
                fileStream.Write(colorTab, 0, colorTab.Length);
                var n = 3 * 256 - colorTab.Length;
                for (var i = 0; i < n; i++) fileStream.WriteByte(0);
            }

            // Write pixels
            var encoder = new LzwEncoder(indexedPixels, ColorDepth);
            encoder.Encode(fileStream);
            
            isFirstFrame = false;
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
                pixels[count++] = color.r;
                pixels[count++] = color.g;
                pixels[count++] = color.b;
            }

            return pixels;
        }
    }
}