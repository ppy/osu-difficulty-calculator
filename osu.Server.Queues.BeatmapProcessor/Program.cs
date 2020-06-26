// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Server.Queues.BeatmapProcessor
{
    public class Program
    {
        public static void Main(string[] args)
        {
            new BeatmapProcessor().Run();
        }
    }
}
