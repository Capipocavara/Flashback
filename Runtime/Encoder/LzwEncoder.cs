/*
 * No copyright asserted on the source code of this class. May be used
 * for any purpose, however, refer to the Unisys LZW patent for restrictions
 * on use of the associated LZWEncoder class :
 *
 * The Unisys patent expired on 20 June 2003 in the USA, in Europe it expired
 * on 18 June 2004, in Japan the patent expired on 20 June 2004 and in Canada
 * it expired on 7 July 2004. The U.S. IBM patent expired 11 August 2006, The
 * Software Freedom Law Center says that after 1 October 2006, there will be
 * no significant patent claims interfering with employment of the GIF format.
 *
 * Original code by Kevin Weiner, FM Software.
 * Adapted from Jef Poskanzer's Java port by way of J. M. G. Elliott.
 */

using System;
using System.IO;

namespace Flashback.Encoder
{
    internal class LzwEncoder
    {
        private const int Eof = -1;
        private const int Bits = 12;
        private const int HSize = 5003;
        private const int MaxBits = Bits;
        private const int MaxMaxCode = 1 << Bits;

        private readonly byte[] pixAry;
        private readonly int initCodeSize;
        private readonly int[] hTab = new int[HSize];
        private readonly int[] codeTab = new int[HSize];
        private readonly int[] masks;
        private readonly byte[] accum = new byte[256];

        private int curPixel;
        private int nBits;
        private int maxCode;
        private int freeEnt;
        private bool clearFlg;
        private int gInitBits;
        private int clearCode;
        private int eofCode;
        private int curAccum;
        private int curBits;
        private int aCount;

        public LzwEncoder(byte[] pixels, int colorDepth)
        {
            pixAry = pixels;
            masks = new[] { 0x0000, 0x0001, 0x0003, 0x0007, 0x000F, 0x001F, 0x003F, 0x007F, 0x00FF, 0x01FF, 0x03FF, 0x07FF, 0x0FFF, 0x1FFF, 0x3FFF, 0x7FFF, 0xFFFF };
            initCodeSize = Math.Max(2, colorDepth);
        }

        private void Add(byte c, Stream outs)
        {
            accum[aCount++] = c;
            if (aCount >= 254) Flush(outs);
        }

        private void ClearTable(Stream outs)
        {
            ResetCodeTable(HSize);
            freeEnt = clearCode + 2;
            clearFlg = true;
            Output(clearCode, outs);
        }

        private void ResetCodeTable(int hSize)
        {
            for (var i = 0; i < hSize; ++i) hTab[i] = -1;
        }

        private void Compress(int initBits, Stream outs)
        {
            int fCode;
            int c;

            gInitBits = initBits;
            clearFlg = false;
            nBits = gInitBits;
            maxCode = MaxCode(nBits);
            clearCode = 1 << (initBits - 1);
            eofCode = clearCode + 1;
            freeEnt = clearCode + 2;
            aCount = 0;

            var ent = NextPixel();
            var hShift = 0;
            for (fCode = HSize; fCode < 65536; fCode *= 2) ++hShift;

            hShift = 8 - hShift;
            ResetCodeTable(HSize);
            Output(clearCode, outs);

            outer_loop:
            while ((c = NextPixel()) != Eof) {
                fCode = (c << MaxBits) + ent;
                var i = (c << hShift) ^ ent;
                if (hTab[i] == fCode) {
                    ent = codeTab[i];
                    continue;
                }

                if (hTab[i] >= 0) {
                    var disp = HSize - i;
                    if (i == 0) disp = 1;

                    do {
                        if ((i -= disp) < 0) i += HSize;
                        if (hTab[i] != fCode) continue;

                        ent = codeTab[i];
                        goto outer_loop;
                    } while (hTab[i] >= 0);
                }

                Output(ent, outs);
                ent = c;
                if (freeEnt < MaxMaxCode) {
                    codeTab[i] = freeEnt++;
                    hTab[i] = fCode;
                } else ClearTable(outs);
            }

            Output(ent, outs);
            Output(eofCode, outs);
        }

        public void Encode(Stream os)
        {
            os.WriteByte(Convert.ToByte(initCodeSize));
            curPixel = 0;
            Compress(initCodeSize + 1, os);
            os.WriteByte(0);
        }

        private void Flush(Stream outs)
        {
            if (aCount <= 0) return;

            outs.WriteByte(Convert.ToByte(aCount));
            outs.Write(accum, 0, aCount);
            aCount = 0;
        }

        private static int MaxCode(int nBits) => (1 << nBits) - 1;

        private int NextPixel()
        {
            if (curPixel == pixAry.Length) return Eof;

            curPixel++;
            return pixAry[curPixel - 1] & 0xff;
        }

        private void Output(int code, Stream outs)
        {
            curAccum &= masks[curBits];

            if (curBits > 0) curAccum |= (code << curBits);
            else curAccum = code;

            curBits += nBits;
            while (curBits >= 8) {
                Add((byte)(curAccum & 0xff), outs);
                curAccum >>= 8;
                curBits -= 8;
            }

            if (freeEnt > maxCode || clearFlg) {
                if (clearFlg) {
                    maxCode = MaxCode(nBits = gInitBits);
                    clearFlg = false;
                } else {
                    ++nBits;
                    maxCode = nBits == MaxBits ? MaxMaxCode : MaxCode(nBits);
                }
            }

            if (code != eofCode) return;

            while (curBits > 0) {
                Add((byte)(curAccum & 0xff), outs);
                curAccum >>= 8;
                curBits -= 8;
            }

            Flush(outs);
        }
    }
}