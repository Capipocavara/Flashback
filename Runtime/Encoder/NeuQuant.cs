/*
 * Copyright (c) 1994 Anthony Dekker
 *
 * NEUQUANT Neural-Net quantization algorithm by Anthony Dekker, 1994.
 * See "Kohonen neural networks for optimal colour quantization"
 * in "Network: Computation in Neural Systems" Vol. 5 (1994) pp 351-367.
 * for a discussion of the algorithm.
 *
 * Any party obtaining a copy of these files from the author, directly or
 * indirectly, is granted, free of charge, a full and unrestricted irrevocable,
 * world-wide, paid up, royalty-free, nonexclusive right and license to deal
 * in this software and documentation files (the "Software"), including without
 * limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons who receive
 * copies from any such party to do so, with the only requirement being
 * that this copyright notice remain intact.
 */

namespace Flashback.Encoder
{
    internal class NeuQuant
    {
        private const int Netsize = 256;
        private const int Prime1 = 499;
        private const int Prime2 = 491;
        private const int Prime3 = 487;
        private const int Prime4 = 503;
        private const int Minpicturebytes = 3 * Prime4;
        private const int Maxnetpos = Netsize - 1;
        private const int Netbiasshift = 4;
        private const int Ncycles = 100;
        private const int Intbiasshift = 16;
        private const int Intbias = 1 << Intbiasshift;
        private const int Gammashift = 10;
        private const int Betashift = 10;
        private const int Beta = Intbias >> Betashift;
        private const int Betagamma = Intbias << (Gammashift - Betashift);
        private const int Initrad = Netsize >> 3;
        private const int Radiusbiasshift = 6;
        private const int Radiusbias = 1 << Radiusbiasshift;
        private const int Initradius = Initrad * Radiusbias;
        private const int Radiusdec = 30;
        private const int Alphabiasshift = 10;
        private const int Initalpha = 1 << Alphabiasshift;
        private const int Radbiasshift = 8;
        private const int Radbias = 1 << Radbiasshift;
        private const int Alpharadbshift = Alphabiasshift + Radbiasshift;
        private const int Alpharadbias = 1 << Alpharadbshift;

        private readonly byte[] thepicture;
        private readonly int lengthcount;
        private readonly int[][] network;
        private readonly int[] netindex = new int[256];
        private readonly int[] bias = new int[Netsize];
        private readonly int[] freq = new int[Netsize];
        private readonly int[] radpower = new int[Initrad];

        private int alphadec;
        private int samplefac;

        public NeuQuant(byte[] thepic, int len, int sample)
        {
            thepicture = thepic;
            lengthcount = len;
            samplefac = sample;
            network = new int[Netsize][];
            for (var i = 0; i < Netsize; i++) {
                network[i] = new int[4];
                var p = network[i];
                p[0] = p[1] = p[2] = (i << (Netbiasshift + 8)) / Netsize;
                freq[i] = Intbias / Netsize;
                bias[i] = 0;
            }
        }
        
        public byte[] Process()
        {
            Learn();
            Unbiasnet();
            Inxbuild();
            return ColorMap();
        }
        
        public int Map(int b, int g, int r)
        {
            var bestd = 1000;
            var best = -1;
            var i = netindex[g];
            var j = i - 1;
            while (i < Netsize || j >= 0) {
                int dist;
                int a;
                int[] p;
                if (i < Netsize) {
                    p = network[i];
                    dist = p[1] - g;
                    if (dist >= bestd) i = Netsize;
                    else {
                        i++;
                        if (dist < 0) dist = -dist;
                        a = p[0] - b;
                        if (a < 0) a = -a;
                        dist += a;
                        if (dist < bestd) {
                            a = p[2] - r;
                            if (a < 0) a = -a;
                            dist += a;
                            if (dist < bestd) {
                                bestd = dist;
                                best = p[3];
                            }
                        }
                    }
                }
                if (j < 0) continue;
                p = network[j];
                dist = g - p[1];
                if (dist >= bestd) j = -1;
                else {
                    j--;
                    if (dist < 0) dist = -dist;
                    a = p[0] - b;
                    if (a < 0) a = -a;
                    dist += a;
                    if (dist >= bestd) continue;
                    a = p[2] - r;
                    if (a < 0) a = -a;
                    dist += a;
                    if (dist >= bestd) continue;
                    bestd = dist;
                    best = p[3];
                }
            }
            return best;
        }
        
        private void Learn()
        {
            int i;
            int step;
            if (lengthcount < Minpicturebytes) samplefac = 1;
            alphadec = 30 + (samplefac - 1) / 3;
            var p = thepicture;
            var pix = 0;
            var samplepixels = lengthcount / (3 * samplefac);
            var delta = samplepixels / Ncycles;
            var alpha = Initalpha;
            var radius = Initradius;
            var rad = radius >> Radiusbiasshift;
            for (i = 0; i < rad; i++) radpower[i] = alpha * ((rad * rad - i * i) * Radbias / (rad * rad));
            if (lengthcount < Minpicturebytes) step = 3;
            else if (lengthcount % Prime1 != 0) step = 3 * Prime1;
            else if (lengthcount % Prime2 != 0) step = 3 * Prime2;
            else if (lengthcount % Prime3 != 0) step = 3 * Prime3;
            else step = 3 * Prime4;
            i = 0;
            while (i < samplepixels) {
                var b = (p[pix + 0] & 0xff) << Netbiasshift;
                var g = (p[pix + 1] & 0xff) << Netbiasshift;
                var r = (p[pix + 2] & 0xff) << Netbiasshift;
                var j = Contest(b, g, r);
                Altersingle(alpha, j, b, g, r);
                if (rad != 0) Alterneigh(rad, j, b, g, r);
                pix += step;
                if (pix >= lengthcount) pix -= lengthcount;
                i++;
                if (delta == 0) delta = 1;
                if (i % delta != 0) continue;
                alpha -= alpha / alphadec;
                radius -= radius / Radiusdec;
                rad = radius >> Radiusbiasshift;
                if (rad <= 1) rad = 0;
                for (j = 0; j < rad; j++) radpower[j] = alpha * ((rad * rad - j * j) * Radbias / (rad * rad));
            }
        }
        
        private void Unbiasnet()
        {
            for (var i = 0; i < Netsize; i++) {
                network[i][0] >>= Netbiasshift;
                network[i][1] >>= Netbiasshift;
                network[i][2] >>= Netbiasshift;
                network[i][3] = i;
            }
        }
        
        private void Inxbuild()
        {
            int j;
            var previouscol = 0;
            var startpos = 0;
            for (var i = 0; i < Netsize; i++) {
                var p = network[i];
                var smallpos = i;
                var smallval = p[1];
                int[] q;
                for (j = i + 1; j < Netsize; j++) {
                    q = network[j];
                    if (q[1] >= smallval) continue;
                    smallpos = j;
                    smallval = q[1];
                }
                q = network[smallpos];
                if (i != smallpos) {
                    j = q[0];
                    q[0] = p[0];
                    p[0] = j;
                    j = q[1];
                    q[1] = p[1];
                    p[1] = j;
                    j = q[2];
                    q[2] = p[2];
                    p[2] = j;
                    j = q[3];
                    q[3] = p[3];
                    p[3] = j;
                }
                if (smallval == previouscol) continue;
                netindex[previouscol] = (startpos + i) >> 1;
                for (j = previouscol + 1; j < smallval; j++) netindex[j] = i;
                previouscol = smallval;
                startpos = i;
            }
            netindex[previouscol] = (startpos + Maxnetpos) >> 1;
            for (j = previouscol + 1; j < 256; j++) netindex[j] = Maxnetpos;
        }

        private byte[] ColorMap()
        {
            var map = new byte[3 * Netsize];
            var index = new int[Netsize];
            for (var i = 0; i < Netsize; i++) index[network[i][3]] = i;
            var k = 0;
            for (var i = 0; i < Netsize; i++) {
                var j = index[i];
                map[k++] = (byte)network[j][0];
                map[k++] = (byte)network[j][1];
                map[k++] = (byte)network[j][2];
            }
            return map;
        }
        
        private int Contest(int b, int g, int r)
        {
            int i;
            var bestd = ~(1 << 31);
            var bestbiasd = bestd;
            var bestpos = -1;
            var bestbiaspos = bestpos;
            for (i = 0; i < Netsize; i++) {
                var n = network[i];
                var dist = n[0] - b;
                if (dist < 0) dist = -dist;
                var a = n[1] - g;
                if (a < 0) a = -a;
                dist += a;
                a = n[2] - r;
                if (a < 0) a = -a;
                dist += a;
                if (dist < bestd) {
                    bestd = dist;
                    bestpos = i;
                }
                var biasdist = dist - (bias[i] >> (Intbiasshift - Netbiasshift));
                if (biasdist < bestbiasd) {
                    bestbiasd = biasdist;
                    bestbiaspos = i;
                }
                var betafreq = freq[i] >> Betashift;
                freq[i] -= betafreq;
                bias[i] += betafreq << Gammashift;
            }
            freq[bestpos] += Beta;
            bias[bestpos] -= Betagamma;
            return bestbiaspos;
        }

        private void Altersingle(int alpha, int i, int b, int g, int r)
        {
            var n = network[i];
            n[0] -= alpha * (n[0] - b) / Initalpha;
            n[1] -= alpha * (n[1] - g) / Initalpha;
            n[2] -= alpha * (n[2] - r) / Initalpha;
        }
        
        private void Alterneigh(int rad, int i, int b, int g, int r)
        {
            var lo = i - rad;
            if (lo < -1) lo = -1;
            var hi = i + rad;
            if (hi > Netsize) hi = Netsize;
            var j = i + 1;
            var k = i - 1;
            var m = 1;
            while (j < hi || k > lo) {
                var a = radpower[m++];
                int[] p;
                if (j < hi) {
                    p = network[j++];
                    p[0] -= a * (p[0] - b) / Alpharadbias;
                    p[1] -= a * (p[1] - g) / Alpharadbias;
                    p[2] -= a * (p[2] - r) / Alpharadbias;
                }
                if (k <= lo) continue;
                p = network[k--];
                p[0] -= a * (p[0] - b) / Alpharadbias;
                p[1] -= a * (p[1] - g) / Alpharadbias;
                p[2] -= a * (p[2] - r) / Alpharadbias;
            }
        }
    }
}