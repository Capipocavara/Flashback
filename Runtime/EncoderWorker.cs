using System;
using System.Collections.Generic;
using System.Threading;
using Flashback.Encoder;
using ThreadPriority = System.Threading.ThreadPriority;

namespace Flashback
{
    internal sealed class EncoderWorker
    {
        private static int workerId = 1;

        internal List<GifFrame> GifFrames;
        internal int FramesToEncode;
        internal uint StartFrame;
        internal string FilePath;
        internal GifEncoder Encoder;
        internal Action<int, float> OnFileSaveProgress;
        internal Action<int, string> OnFileSaved;

        private readonly Thread thread;
        private readonly int currentId;
        private int frameToGet;
        private byte[] pixels;

        internal EncoderWorker(ThreadPriority priority)
        {
            currentId = workerId++;
            thread = new Thread(Run) {
                Priority = priority
            };
            frameToGet = 0;
            pixels = null;
        }

        internal void Start() => thread.Start();

        private void Run()
        {
            Encoder.Start(FilePath);
            while (FramesToEncode > 0) {
                if (GifFrames.Count > frameToGet) {
                    lock (GifFrames) {
                        if (GifFrames[frameToGet].ID < StartFrame)
                            GifFrames.RemoveAt(0);
                        else
                            pixels = GifFrames[frameToGet].ExtractImagePixels();
                    }

                    if (pixels == null)
                        continue;

                    Encoder.AddFrame(pixels);
                    OnFileSaveProgress?.Invoke(currentId, (float)frameToGet / (FramesToEncode + frameToGet));
                    frameToGet++;
                    FramesToEncode--;
                } else
                    Thread.Yield();
            }

            Encoder.Finish();
            OnFileSaved?.Invoke(currentId, FilePath);
        }
    }
}